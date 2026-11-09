using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Game.Net.Rules;
using Worktown.Game.Npc;
using Worktown.Game.Shop;
using Worktown.Game.UI;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 032 AC3 (server, pure): the register (change built click by click and validated, card in one click, wrong change costs
    /// the difference and a star, paid exactly once, a refusal pays nothing), the patience (drains only at the slot, never at the
    /// register; a guest leaves when it runs out), the order match with modifiers on real builds, the bill and the hold of a guest
    /// sale, the ticket with modifiers, the shift's new numbers, and the synced surface (small presentation fields only).
    /// </summary>
    public class OrdersTwoTests
    {
        private const int Board = 8;
        private const int Spot = 90;
        private const int Fryer = 6;
        private const int Basket = 70;
        private const int Owner = 3;
        private const uint Start = HeatTestKit.StartTick;
        private const double Rate = HeatTestKit.TickRate;
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static uint Real(double seconds) => Start + (uint)Math.Round(seconds * Rate);

        // ---- the register (RegisterDesk) ----

        /// <summary>Counts the register's payouts; refuses with <see cref="Refuse"/>.</summary>
        private sealed class CountingWallet : IRegisterWallet
        {
            public readonly List<(int Payee, long Amount)> Paid = new List<(int, long)>();
            public CookOutcome Refuse = CookOutcome.Ok;
            public int Calls;

            public CookOutcome Pay(int payee, long amount, out long paid)
            {
                Calls++;
                paid = 0;
                if (Refuse != CookOutcome.Ok) return Refuse;
                Paid.Add((payee, amount));
                paid = amount;
                return CookOutcome.Ok;
            }
        }

        private static RegisterPayment Cash(int guest, long bill, long tip, long handed, int payee = Owner) => new RegisterPayment
        {
            GuestId = guest, Payee = payee, Dish = Menu.FalafelWrap, Bill = bill, Tip = tip, Card = false, Handed = handed, OrderStars = 5,
            WaitedTenths = 100, PatienceTenths = 1200,
        };

        private static RegisterPayment Card(int guest, long bill, long tip, int payee = Owner) => new RegisterPayment
        {
            GuestId = guest, Payee = payee, Dish = Menu.Fries, Bill = bill, Tip = tip, Card = true, Handed = bill + tip, OrderStars = 4,
            WaitedTenths = 100, PatienceTenths = 1200,
        };

        private static int Slot(int value) => CashRules.Tray.ToList().IndexOf(value);

        [Test]
        public void Cash_TheChangeIsBuiltClickByClick_AndTheRightChangePaysTheDueOnce()
        {
            var desk = new RegisterDesk();
            var wallet = new CountingWallet();
            desk.Open(Cash(1, 352, 30, 400)); // due 382, change 18
            Assert.AreEqual(18, desk.Head.ChangeDue);
            foreach (int coin in new[] { 10, 5, 2, 1 })
            {
                Assert.AreEqual(CookOutcome.Ok, desk.Use(Owner, Slot(coin), wallet, out RegisterSettlement none));
                Assert.IsNull(none.Payment, "a coin settles nothing");
            }
            Assert.AreEqual(18, desk.Head.Tray);
            Assert.AreEqual(0, wallet.Calls, "nothing is paid while the change is built");
            Assert.AreEqual(CookOutcome.Ok, desk.Use(Owner, RegisterRules.GiveChangeOption, wallet, out RegisterSettlement paid));
            Assert.AreEqual(1, wallet.Calls, "paid exactly once");
            Assert.AreEqual((Owner, 382L), wallet.Paid[0]);
            Assert.AreEqual(382, paid.Paid);
            Assert.IsFalse(paid.WrongChange);
            Assert.IsNull(desk.Head, "the guest left the line");
            Assert.AreEqual(CookOutcome.NoPayment, desk.Use(Owner, RegisterRules.GiveChangeOption, wallet, out _), "a second hand-over pays nothing");
            Assert.AreEqual(1, wallet.Calls);
        }

        [Test]
        public void Cash_TakeBack_EmptiesTheTray_AndTheTrayHasALimit()
        {
            var desk = new RegisterDesk();
            desk.Open(Cash(1, 10, 0, 20));
            Assert.AreEqual(CookOutcome.TrayEmpty, desk.Use(Owner, RegisterRules.TakeBackOption, null, out _));
            desk.Use(Owner, Slot(5), null, out _);
            desk.Use(Owner, Slot(2), null, out _);
            Assert.AreEqual(7, desk.Head.Tray);
            Assert.AreEqual(CookOutcome.Ok, desk.Use(Owner, RegisterRules.TakeBackOption, null, out _));
            Assert.AreEqual(0, desk.Head.Tray);
            for (int i = 0; i < 5; i++) Assert.AreEqual(CookOutcome.Ok, desk.Use(Owner, Slot(50), null, out _));
            Assert.AreEqual(CookOutcome.TrayFull, desk.Use(Owner, Slot(10), null, out _), "250 + 10 is more than the byte holds");
            Assert.AreEqual(250, desk.Head.Tray, "a refused click adds nothing");
            Assert.AreEqual(CookOutcome.InvalidOption, desk.Use(Owner, RegisterRules.SlotCount, null, out _));
        }

        [Test]
        public void WrongChange_CostsTheDifference_EitherWay_AndAStarOfTheReview()
        {
            foreach (int[] coins in new[] { new[] { 10, 5, 2, 1, 5 }, new[] { 10, 2, 1 } })
            {
                var desk = new RegisterDesk();
                var wallet = new CountingWallet();
                desk.Open(Cash(1, 352, 30, 400)); // change 18
                foreach (int coin in coins) desk.Use(Owner, Slot(coin), wallet, out _);
                Assert.AreEqual(CookOutcome.Ok, desk.Use(Owner, RegisterRules.GiveChangeOption, wallet, out RegisterSettlement s));
                Assert.AreEqual(382 - 5, s.Paid, string.Join(",", coins) + ": five off either way costs five");
                Assert.IsTrue(s.WrongChange);
                GuestReview right = ReviewRules.Review(5, 100, 1200, 0, false);
                GuestReview wrong = ReviewRules.Review(s.Payment.OrderStars, s.Payment.WaitedTenths, s.Payment.PatienceTenths, s.Payment.Misses,
                    s.WrongChange);
                Assert.AreEqual(right.Stars - 1, wrong.Stars);
                Assert.AreEqual(ReviewKey.WrongChange, wrong.Key);
            }
        }

        [Test]
        public void Card_IsOneClick_AndTheOtherWayIsRefused()
        {
            var desk = new RegisterDesk();
            var wallet = new CountingWallet();
            desk.Open(Card(1, 600, 90));
            Assert.AreEqual(CookOutcome.PaysByCard, desk.Use(Owner, Slot(5), wallet, out _));
            Assert.AreEqual(CookOutcome.PaysByCard, desk.Use(Owner, RegisterRules.GiveChangeOption, wallet, out _));
            Assert.AreEqual(0, wallet.Calls);
            Assert.AreEqual(CookOutcome.Ok, desk.Use(Owner, RegisterRules.CardOption, wallet, out RegisterSettlement s));
            Assert.AreEqual(690, s.Paid);
            Assert.AreEqual(1, wallet.Calls);
            Assert.IsFalse(s.WrongChange);

            desk.Open(Cash(2, 10, 0, 20));
            Assert.AreEqual(CookOutcome.PaysCash, desk.Use(Owner, RegisterRules.CardOption, wallet, out _));
            Assert.AreEqual(1, wallet.Calls);
        }

        [Test]
        public void ARefusal_PaysNothing_AndChangesNothing_AndOnlyThePayeeWorksItsPayment()
        {
            var desk = new RegisterDesk();
            var refusing = new CountingWallet { Refuse = CookOutcome.SimRefused };
            desk.Open(Cash(1, 352, 30, 400));
            desk.Use(Owner, Slot(10), refusing, out _);
            Assert.AreEqual(CookOutcome.NotYourGuest, desk.Use(Owner + 1, Slot(5), refusing, out _), "another cook");
            Assert.AreEqual(CookOutcome.NotYourGuest, desk.Use(Owner + 1, RegisterRules.GiveChangeOption, refusing, out _));
            Assert.AreEqual(10, desk.Head.Tray);
            Assert.AreEqual(CookOutcome.SimRefused, desk.Use(Owner, RegisterRules.GiveChangeOption, refusing, out RegisterSettlement none));
            Assert.IsNull(none.Payment);
            Assert.AreEqual(0, refusing.Paid.Count);
            Assert.IsNotNull(desk.Head, "still waiting");
            Assert.AreEqual(10, desk.Head.Tray, "the tray is kept");
            var wallet = new CountingWallet();
            Assert.AreEqual(CookOutcome.Ok, desk.Use(Owner, RegisterRules.GiveChangeOption, wallet, out _), "a later try goes through");
            Assert.AreEqual(1, wallet.Calls);
            Assert.AreEqual(CookOutcome.NoPayment, new RegisterDesk().Use(Owner, Slot(1), wallet, out _));
        }

        [Test]
        public void TheRegister_SettlesByItself_WhenTheKitchenClosesOrThePayeeLeft_Once()
        {
            var desk = new RegisterDesk();
            var wallet = new CountingWallet();
            desk.Open(Cash(1, 352, 30, 400));
            desk.Open(Card(2, 100, 0));
            desk.Open(Card(2, 100, 0)); // the same guest twice is one payment
            Assert.AreEqual(2, desk.Line.Count);
            desk.Use(Owner, Slot(50), wallet, out _); // a wrong tray does not count when the register settles by itself
            Assert.IsTrue(desk.SettleAutomatically(1, wallet, out RegisterSettlement s));
            Assert.IsTrue(s.Automatic);
            Assert.AreEqual(382, s.Paid);
            Assert.IsFalse(s.WrongChange);
            Assert.IsFalse(desk.SettleAutomatically(1, wallet, out _), "once");
            Assert.AreEqual(1, wallet.Calls);
            Assert.IsTrue(desk.Remove(2));
            Assert.IsFalse(desk.IsWaiting(2));
        }

        [Test]
        public void ThePayment_IsPackedForClients_AndTheRulesSayWhatAClickDoes()
        {
            ushort none = RegisterRules.PackPayment(false, false, 0);
            ushort card = RegisterRules.PackPayment(true, true, 0);
            ushort cash = RegisterRules.PackPayment(true, false, 18);
            Assert.IsFalse(RegisterRules.HasPayer(none));
            Assert.IsTrue(RegisterRules.IsCard(card));
            Assert.IsTrue(RegisterRules.IsCash(cash));
            Assert.AreEqual(18, RegisterRules.ChangeDue(cash));
            Assert.AreEqual("CHANGE $8 / $18", RegisterRules.DisplayText(cash, 8));
            Assert.AreEqual("CARD", RegisterRules.DisplayText(card, 0));
            Assert.AreEqual(InteractionAction.Pay, InteractionRules.ActionFor(Held.Nothing, AimTarget.RegisterSlot(Slot(5), cash, 0)));
            Assert.AreEqual("Click · give $5", InteractionRules.PromptFor(Held.Nothing, AimTarget.RegisterSlot(Slot(5), cash, 0)));
            Assert.AreEqual(InteractionAction.Pay, InteractionRules.ActionFor(Held.Food("potato", false), AimTarget.RegisterSlot(Slot(5), cash, 0)),
                "any hands");
            Assert.AreEqual("Click · hand over $18", InteractionRules.PromptFor(Held.Nothing,
                AimTarget.RegisterSlot(RegisterRules.GiveChangeOption, cash, 18)));
            Assert.AreEqual("They pay by card", InteractionRules.PromptFor(Held.Nothing, AimTarget.RegisterSlot(Slot(5), card, 0)));
            Assert.AreEqual("Click · card payment", InteractionRules.PromptFor(Held.Nothing, AimTarget.RegisterSlot(RegisterRules.CardOption, card, 0)));
            Assert.AreEqual("They pay cash", InteractionRules.PromptFor(Held.Nothing, AimTarget.RegisterSlot(RegisterRules.CardOption, cash, 0)));
            Assert.AreEqual("Nobody is paying", InteractionRules.PromptFor(Held.Nothing, AimTarget.RegisterSlot(Slot(1), none, 0)));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Nothing, AimTarget.RegisterSlot(Slot(1), none, 0).TooFar()));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Nothing, AimTarget.RegisterSlot(-1, cash, 0)), "the body");
            Assert.IsNull(InteractionRules.PromptFor(Held.Nothing, AimTarget.RegisterSlot(-1, cash, 0)), "no prompt on the register's body");
        }

        [Test]
        public void TheTray_ShowsOurOwnClickAtOnce_UntilTheServerConfirmsOrRefuses()
        {
            var tray = new TrayPrediction();
            tray.OnServer(0);
            tray.Add(10, 1f);
            tray.Add(5, 1f);
            Assert.AreEqual(15, tray.Shown);
            Assert.IsTrue(tray.OnServer(10), "the first click arrived: our own rise");
            Assert.AreEqual(15, tray.Shown, "no flicker back to 10");
            tray.Refused();
            Assert.AreEqual(10, tray.Shown);
            tray.Add(2, 1f);
            tray.Expire(2f);
            Assert.AreEqual(10, tray.Shown, "a click the server never answered ends");
            Assert.IsFalse(tray.OnServer(20), "someone else's rise");
            tray.Add(1, 5f);
            tray.OnServer(0); // paid or taken back
            Assert.AreEqual(0, tray.Shown);
            Assert.AreEqual(0, tray.PendingCount);
        }

        // ---- patience ----

        [Test]
        public void Patience_DrainsOnlyAtTheSlot_AndAGuestLeavesWhenItRunsOut()
        {
            var book = new PatienceBook();
            var queue = new CustomerQueue();
            queue.Add(1, "meal", Menu.FalafelWrap, 0);
            queue.Add(2, "meal", Menu.SeitanWrap, 0);
            book.Add(1, 10f);
            book.Add(2, 10f);
            var ranOut = new List<int>();
            var bytes = new List<KeyValuePair<int, byte>>();
            bool twoAtSlot = false;
            float now = 0f;
            for (int i = 0; i < 99; i++)
            {
                now += 0.1f;
                book.Tick(0.1f, now, queue.Entries, id => id == 1 || twoAtSlot, ranOut, bytes);
                Assert.IsEmpty(ranOut, "9.9 s of 10");
            }
            book.Get(2, out float waited2, out _);
            Assert.AreEqual(0f, waited2, "still walking: no drain");
            book.Get(1, out float waited1, out float total1);
            Assert.AreEqual(9.9f, waited1, 0.01f);
            Assert.AreEqual(10f, total1);
            now += 0.2f;
            book.Tick(0.2f, now, queue.Entries, id => id == 1, ranOut, bytes);
            CollectionAssert.AreEqual(new[] { 1 }, ranOut, "out of patience: the guest leaves (missed)");
            Assert.IsFalse(book.Contains(1));
            book.Tick(0.1f, now + 0.1f, queue.Entries, id => true, ranOut, bytes);
            Assert.IsEmpty(ranOut, "reported once");
        }

        [Test]
        public void Patience_NeverDrains_ForAGuestAtTheRegister_AndTheRingByteIsRateLimited()
        {
            var book = new PatienceBook();
            var queue = new CustomerQueue();
            queue.Add(1, "meal", Menu.FalafelWrap, 0);
            book.Add(1, 100f);
            var ranOut = new List<int>();
            var bytes = new List<KeyValuePair<int, byte>>();
            book.Tick(0.05f, 0f, queue.Entries, id => true, ranOut, bytes);
            Assert.AreEqual(1, bytes.Count);
            book.Tick(0.05f, 0.1f, queue.Entries, id => true, ranOut, bytes);
            Assert.IsEmpty(bytes, "at most every 0.25 s");
            book.Tick(10f, 0.3f, queue.Entries, id => true, ranOut, bytes);
            Assert.AreEqual(1, bytes.Count);
            Assert.Less(bytes[0].Value, 255);
            // Handed its food: out of the queue (and the book); its patience stays where it was, nothing drains at the register.
            queue.Remove(1);
            book.Get(1, out float before, out _);
            for (int i = 0; i < 100; i++) book.Tick(10f, 1f + i, queue.Entries, id => true, ranOut, bytes);
            book.Get(1, out float after, out _);
            Assert.AreEqual(before, after);
            Assert.IsEmpty(ranOut);
        }

        [Test]
        public void ThePatienceRing_DrainsAndTurnsTerracottaNearTheEnd()
        {
            Assert.AreEqual(Worktown.Game.Art.Palette.Sage, PatienceRingRules.FillColor(0.9f));
            Assert.AreEqual(Worktown.Game.Art.Palette.Mustard, PatienceRingRules.FillColor(0.4f));
            Assert.AreEqual(Worktown.Game.Art.Palette.Terracotta, PatienceRingRules.FillColor(0.2f));
            Assert.AreEqual(1f, PatienceRingRules.Pulse(0.8f, 0.3f));
            var v = new List<UnityEngine.Vector3>();
            var c = new List<UnityEngine.Color>();
            var t = new List<int>();
            PatienceRingRules.Build(1f, v, c, t);
            int full = t.Count;
            PatienceRingRules.Build(0.3f, v, c, t);
            Assert.Less(t.Count, full, "less fill");
            PatienceRingRules.Build(0f, v, c, t);
            Assert.Greater(t.Count, 0, "the track stays");
            Assert.IsTrue(PatienceRingRules.IsShown(true));
            Assert.IsFalse(PatienceRingRules.IsShown(false), "not while paying");
        }

        // ---- orders with modifiers on real builds ----

        private static KitchenState Kitchen()
        {
            var kitchen = KitchenState.CreateDemo();
            kitchen.RegisterAssemblySpot(Spot, Board);
            kitchen.RegisterBasket(Basket, Fryer, 0);
            return kitchen;
        }

        private static void Ingredient(KitchenState kitchen, string baseId, int id, int cut = 0)
        {
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateIngredient(baseId, out Item item));
            kitchen.Register(id, item);
            if (cut > 0) HeatTestKit.Cut(kitchen, id, cut);
        }

        private static void Put(KitchenState kitchen, int id) => Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(id, Spot, VesselKind.Assembly, Start));

        /// <summary>A falafel wrap in paper: wrap, falafel from the fryer, (lettuce slices,) garlic-herb for <paramref name="sauce"/> s.</summary>
        private static Item Wrap(bool lettuce, double sauce)
        {
            KitchenState kitchen = Kitchen();
            Ingredient(kitchen, "wrap", 40);
            Put(kitchen, 40);
            Ingredient(kitchen, "falafel_mix", 41);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryToggleFryer(Fryer, Start));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(41, Basket, VesselKind.Basket, Start));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryLowerBasket(Basket, Start));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryRaiseBasket(Basket, Real(25), Rate));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewTakeOut(Basket, VesselKind.Basket, Real(28), Rate, out Item balls));
            kitchen.ClearContents(Basket, VesselKind.Basket);
            kitchen.Register(41, balls);
            Put(kitchen, 41);
            if (lettuce)
            {
                Ingredient(kitchen, "lettuce", 42, 2);
                Put(kitchen, 42);
            }
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryStartSqueeze(Board, 0, Owner, Real(30)));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryEndSqueeze(Board, 0, Owner, Real(30 + sauce), Rate, out Item build, out float total));
            kitchen.CommitBuild(Spot, build, BuildRules.SauceOf(0), total);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryFold(Spot));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewPackaging(Spot, BuildRules.Paper, out Item wrapped));
            return wrapped;
        }

        private static byte Mods(byte dish, params string[] texts)
        {
            RecipeTemplate t = RecipeTemplates.ById(Menu.ById(dish).Template);
            return OrderModifiers.Pack(t, OrderModifiers.Allowed(t).Where(m => texts.Contains(m.Text)).ToList());
        }

        [Test]
        public void TheOrderMatch_ChecksEachLayerWithTheModifiers()
        {
            MenuDish dish = Menu.ById(Menu.FalafelWrap);
            Item plain = Wrap(true, 1.0), lots = Wrap(true, 2.0), noLettuce = Wrap(false, 1.0);
            byte extra = Mods(Menu.FalafelWrap, "extra garlic");
            byte without = Mods(Menu.FalafelWrap, "no lettuce");
            Assert.AreEqual(OrderMatch.Exact, OrderRules.Match(plain, dish, 0));
            Assert.AreEqual(OrderMatch.Exact, OrderRules.Match(lots, dish, extra), "extra garlic: lots");
            OrderFit off = OrderRules.Fit(plain, dish, extra);
            Assert.AreEqual(OrderMatch.Close, off.Match, "the right amount is not extra");
            Assert.AreEqual(1, off.Misses);
            Assert.AreEqual(OrderMatch.Exact, OrderRules.Match(noLettuce, dish, without));
            Assert.AreEqual(OrderMatch.Close, OrderRules.Match(plain, dish, without), "lettuce on it anyway");
            Assert.AreEqual(OrderMatch.Close, OrderRules.Match(noLettuce, dish, 0), "without the modifier a missing lettuce is one short (031)");
            Assert.AreEqual(OrderRules.MatchBuild(plain, dish), OrderRules.Match(plain, dish, 0), "the 031 match without modifiers");
            // The order's quality: no lettuce asked for costs nothing.
            Assert.Greater(OrderRules.Fit(noLettuce, dish, without).Quality, KitchenState.QualityOf(noLettuce));
            Assert.AreEqual(OrderMatch.Wrong, OrderRules.Fit(MenuKitchen.ExactFries(), dish, without).Match);
            Assert.AreEqual(OrderMatch.Exact, OrderRules.Fit(MenuKitchen.ExactFries(), Menu.ById(Menu.Fries), 0).Match, "other dishes as in 022");
        }

        [Test]
        public void TheQueue_GivesTheDishToTheGuestWhoseModifiersItKeeps()
        {
            Item lots = Wrap(true, 2.0);
            var queue = new CustomerQueue();
            queue.Add(1, "meal", Menu.FalafelWrap, Mods(Menu.FalafelWrap, "no lettuce"));
            queue.Add(2, "meal", Menu.FalafelWrap, Mods(Menu.FalafelWrap, "extra garlic"));
            Assert.IsTrue(queue.TryPick(e => OrderRules.Match(lots, Menu.ById(e.Dish), e.Modifiers), out QueuedCustomer picked, out OrderMatch m));
            Assert.AreEqual(2, picked.Id);
            Assert.AreEqual(OrderMatch.Exact, m);
        }

        [Test]
        public void TheBill_PaysTheOrdersQuality_AndAGuestSaleIsHeldUntilTheRegister()
        {
            Assert.AreEqual(1000, OrderRules.BillPermille(OrderMatch.Exact, 0.7f, 0.7f));
            Assert.Greater(OrderRules.BillPermille(OrderMatch.Exact, 0.5f, 0.75f), 1000, "the order rates higher than the plain recipe");
            Assert.AreEqual(600, OrderRules.BillPermille(OrderMatch.Close, 0.6f, 0.6f));
            Assert.AreEqual(250, OrderRules.BillPermille(OrderMatch.Wrong, 0.6f, 0.6f));
            Assert.AreEqual(1000, OrderRules.GuestSharePermille(OrderMatch.Exact), "no star tip any more: the tip is TipRules");

            TownEconomy economy = TownEconomy.CreateDemo();
            Assert.IsTrue(economy.TryOpenAccount("p0", 500));
            SaleResult sale = economy.TrySellForGuest("p0", MenuKitchen.ExactFries());
            Assert.IsTrue(sale.Ok, sale.Error.ToString());
            Assert.AreEqual(SaleChannel.NpcDemand, sale.Channel);
            Assert.Greater(sale.Payout, 0, "the sim's payout is the bill's base");
            Assert.AreEqual(500, economy.BalanceOf("p0"), "nothing credited at the hand-over");
            Assert.IsTrue(economy.TryPayGuest("p0", 77, out long paid));
            Assert.AreEqual(77, paid);
            Assert.AreEqual(577, economy.BalanceOf("p0"));
            Assert.IsFalse(economy.TryPayGuest("nobody", 5, out _));
        }

        // ---- tickets, bubbles, the shift ----

        [Test]
        public void TheTicket_CarriesTheModifiers_InPlainWords()
        {
            byte mods = Mods(Menu.FalafelWrap, "no lettuce", "extra garlic");
            int ticket = CustomerRules.PackTicket(Menu.FalafelWrap, 12, mods);
            Assert.AreEqual(Menu.FalafelWrap, CustomerRules.TicketDish(ticket));
            Assert.AreEqual(12, CustomerRules.TicketMinutes(ticket));
            Assert.AreEqual(mods, CustomerRules.TicketModifiers(ticket));
            IReadOnlyList<OrderModifier> list = CustomerRules.ModifiersOf(Menu.FalafelWrap, mods);
            Assert.AreEqual(new[] { "no lettuce", "+ extra garlic" }, list.Select(CustomerRules.TicketLine).ToArray());
            Assert.AreEqual(new[] { true, false }, list.Select(CustomerRules.IsStruck).ToArray());
            Assert.AreEqual("Falafel wrap\nno lettuce, extra garlic", CustomerRules.OrderText(Menu.FalafelWrap, mods));
            Assert.AreEqual("Fries", CustomerRules.OrderText(Menu.Fries, 0));
            Assert.AreEqual(CustomerRules.PackTicket(Menu.Fries, 3), CustomerRules.PackTicket(Menu.Fries, 3, 0), "022 tickets unchanged");
            Assert.AreEqual(4, CustomerRules.LayersOf(Menu.ById(Menu.FalafelWrap), 0));
            Assert.AreEqual(3, CustomerRules.LayersOf(Menu.ById(Menu.FalafelWrap), Mods(Menu.FalafelWrap, "no lettuce")));
            Assert.AreEqual(3, CustomerRules.LayersOf(Menu.ById(Menu.Fries), 0), "potato, cut, fried");
            Assert.AreEqual("Thanks! Here's $400.", CustomerRules.PayText(false, 400));
            Assert.AreEqual(ArtKeyOf(Menu.FalafelWrap), Worktown.Game.Art.ArtKey.FoodWrapPaper);
        }

        private static Worktown.Game.Art.ArtKey ArtKeyOf(byte dish) => HandoffRules.ParcelOf(dish);

        [Test]
        public void TheShift_CountsTipsReviewsAndMissedByReason()
        {
            var shift = new Shift();
            shift.OnClock(8, 0f, 0);
            shift.RecordSale(5, 400, 40, new GuestReview(5, ReviewKey.Great));
            shift.RecordSale(3, 100, 0, new GuestReview(2, ReviewKey.WrongChange));
            shift.RecordMissed(2, MissReason.Patience);
            shift.RecordMissed(1);
            shift.OnClock(14, 10f, 3);
            ShiftSummary s = shift.Last;
            Assert.AreEqual(2, s.Served);
            Assert.AreEqual(500, s.Earned);
            Assert.AreEqual(40, s.Tips);
            Assert.AreEqual(2, s.Reviews);
            Assert.AreEqual(35, s.AverageReviewTenths);
            Assert.AreEqual(6, s.Missed);
            Assert.AreEqual(2, s.MissedPatience);
            Assert.AreEqual(1, s.MissedGone);
            Assert.AreEqual(3, s.MissedClosed);
            Assert.AreEqual(new GuestReview(2, ReviewKey.WrongChange), GuestReview.Unpack(s.Review1), "newest first");
            Assert.AreEqual(new GuestReview(5, ReviewKey.Great), GuestReview.Unpack(s.Review2));
            shift.RecordSale(4, 50, 5, new GuestReview(4, ReviewKey.Good)); // paid by the register just after closing
            Assert.AreEqual(3, shift.Last.Served);
            ShiftReport r = ShiftReport.From(shift.Last);
            Assert.AreEqual(45, r.Tips);
            Assert.AreEqual(3, r.Reviews);
            StringAssert.Contains("out of patience 2", ShiftSummaryView.MissedText(6, r));
            StringAssert.Contains("kitchen closed 3", ShiftSummaryView.MissedText(6, r));
            StringAssert.Contains("Tasty", ShiftSummaryView.ReviewLine(r.Review1));
            Assert.AreEqual(string.Empty, ShiftSummaryView.ReviewLine(0));
        }

        // ---- the synced surface ----

        private static Type SyncArgument(FieldInfo field)
        {
            if (!field.FieldType.IsGenericType || !field.FieldType.Name.StartsWith("Sync")) return null;
            return field.FieldType.GetGenericArguments()[0];
        }

        private static string[] Synced(Type type) => type.GetFields(Instance).Where(f => SyncArgument(f) != null).Select(f => f.Name).ToArray();

        [Test]
        public void TheSyncedSurface_GrowsOnlyBySmallPresentationFields()
        {
            CollectionAssert.AreEquivalent(new[] { "Wish", "Bubble", "Dish", "Mood", "Patience" }, Synced(typeof(Customer)));
            Assert.AreEqual(typeof(byte), SyncArgument(typeof(Customer).GetField("Patience", Instance)), "the patience ring: one byte");
            CollectionAssert.AreEquivalent(new[] { "Payment", "Tray" }, Synced(typeof(Register)));
            Assert.AreEqual(typeof(ushort), SyncArgument(typeof(Register).GetField("Payment", Instance)));
            Assert.AreEqual(typeof(byte), SyncArgument(typeof(Register).GetField("Tray", Instance)));
            Assert.Contains("LiveReport", Synced(typeof(TicketRailView)));
            Assert.Contains("LastReport", Synced(typeof(TicketRailView)));
            Assert.AreEqual(15, Synced(typeof(TicketRailView)).Length, "022's thirteen plus the two reports");
            foreach (FieldInfo f in typeof(ShiftReport).GetFields(Instance))
                Assert.IsTrue(f.FieldType == typeof(int) || f.FieldType == typeof(short) || f.FieldType == typeof(byte),
                    "ShiftReport." + f.Name + " is " + f.FieldType.Name + " (whole numbers and keys only, D21)");
            Assert.IsEmpty(typeof(ShiftReport).GetProperties(Instance), "no hidden state");
            Assert.IsFalse(typeof(Register).GetMethods(Instance | BindingFlags.DeclaredOnly)
                .Any(m => m.GetCustomAttributes(false).Any(a => a.GetType().Name.EndsWith("RpcAttribute"))), "no RPC on the register");
        }
    }
}
