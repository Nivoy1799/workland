using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Cooking;
using Worktown.Game.Net.Rules;
using Worktown.Game.Npc;
using Worktown.Game.Player;
using Worktown.Game.Shop;
using Worktown.Game.UI;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 034: stock and supply on the server and the client. The stock room (a take decrements, an empty slot refuses with
    /// OutOfStock, an order charges once, a delivery's boxes, an unpack fills up to the capacity with the rest in the box), the refund
    /// backstop (StockTakeSequence, the assembly board, the drink dispenser, the terminal's order), the stock wallet (squeezes, packaging,
    /// pours drawn from stock), the catalog and the terminal's rules, the client's rules and looks, the synced surface, and the 033 review
    /// fixes (the pour record, the tab).
    /// </summary>
    public class StockSupplyTests
    {
        private const int Owner = 3;
        private const uint Start = HeatTestKit.StartTick;
        private const double Rate = HeatTestKit.TickRate;
        private static readonly EconomyCatalog Catalog = EconomyCatalog.CreateDefault();

        private static uint Real(double seconds) => Start + (uint)Math.Round(seconds * Rate);
        private static int Line(string id) => StockCatalog.IndexOf(id);

        /// <summary>Counts the terminal's charges; refuses with <see cref="Refuse"/>; <see cref="During"/> runs inside the charge.</summary>
        private sealed class CountingSupplyWallet : ISupplyWallet
        {
            public CookOutcome Refuse = CookOutcome.Ok;
            public bool Broke;
            public int Charges, Refunds;
            public long Paid, Refunded;
            public Action During;

            public bool CanAfford(long cost) => !Broke;

            public CookOutcome Charge(SupplierOffer offer, int packs, out long cost)
            {
                Charges++;
                cost = 0;
                if (Refuse != CookOutcome.Ok) return Refuse;
                cost = Supplier.OrderCost(Catalog, offer, packs);
                Paid += cost;
                During?.Invoke();
                return CookOutcome.Ok;
            }

            public void Refund(long cost)
            {
                Refunds++;
                Refunded += cost;
            }
        }

        // ---- the catalog ----

        [Test]
        public void TheCatalog_StocksEveryFridgeAndShelfGood_PackagingCupsAndKegs_AtTheirCapacities()
        {
            foreach (string id in StockLayout.Fridge.Concat(StockLayout.Shelf)) Assert.GreaterOrEqual(Line(id), 0, id);
            Assert.AreEqual(12, StockCatalog.Goods[Line("lettuce")].Capacity, "vegetables 12");
            Assert.AreEqual(12, StockCatalog.Goods[Line("tomato")].Capacity);
            Assert.AreEqual(8, StockCatalog.Goods[Line("wrap")].Capacity, "bread 8");
            Assert.AreEqual(8, StockCatalog.Goods[Line("flatbread")].Capacity);
            Assert.AreEqual(6, StockCatalog.Goods[Line("chili_sauce")].Capacity, "sauces 6");
            foreach (string id in new[] { "wrap_paper", "takeaway_box", "paper_bag", Drinks.CupId })
                Assert.AreEqual(20, StockCatalog.Goods[Line(id)].Capacity, id + ": packaging 20");
            foreach (BaseItemDef drink in Drinks.All)
            {
                StockCatalog.Good keg = StockCatalog.Goods[Line(drink.Id)];
                Assert.IsTrue(keg.IsKeg);
                Assert.AreEqual(Supplier.CupsPerKeg, keg.GrocerItemsPerUnit, "a keg is 20 cups");
            }
            Assert.AreEqual(StockCatalog.Count, StockCatalog.Goods.Select(g => g.Id).Distinct().Count());
            Assert.LessOrEqual(StockCatalog.Count, TerminalRules.Pages * TerminalRules.Rows);
            foreach (StockCatalog.Good g in StockCatalog.Goods)
                Assert.IsTrue(g.Id == StockCatalog.ConeId ? Catalog.TryGetGrocerPrice(Worktown.Sim.Cooking.Spit.MeatId, out _)
                    : Catalog.TryGetGrocerPrice(g.Id, out _), g.Id + " has a grocer price (the supplier's offer; spec 036: a cone is priced as seitan)");
            for (int line = 0; line < StockCatalog.Count; line++)
                Assert.IsTrue(StockCatalog.TryOffer(Catalog, line, out _), StockCatalog.Goods[line].Id + " has a supplier offer");
        }

        [Test]
        public void ANewGame_StartsAtHalfCapacity()
        {
            ShopStock start = StockCatalog.Starting();
            Assert.AreEqual(StockCatalog.Count, start.Lines.Count);
            for (int i = 0; i < start.Lines.Count; i++)
            {
                Assert.AreEqual(StockCatalog.Goods[i].Id, start.Lines[i].GoodId, "the synced order");
                Assert.AreEqual(StockCatalog.Goods[i].Capacity / 2, start.Lines[i].Count, start.Lines[i].GoodId);
            }
            Assert.AreEqual(6, start.Lines[Line("tomato")].Count);
            Assert.AreEqual(1, start.Lines[Line("lemonade")].Count, "one keg of two");
            Assert.AreEqual(20, StockCatalog.Presented(start.Lines[Line("lemonade")]), "a keg shows as its 20 cups");
        }

        [Test]
        public void SlotsAndOptions_MapToTheirLines_BothWays()
        {
            for (int s = 0; s < StockLayout.Fridge.Count; s++)
                Assert.AreEqual(Line(StockLayout.Fridge[s]), StockCatalog.LineOfSlot(StationKind.Fridge, s));
            for (int s = 0; s < StockLayout.Shelf.Count; s++)
                Assert.AreEqual(Line(StockLayout.Shelf[s]), StockCatalog.LineOfSlot(StationKind.Shelf, s));
            Assert.AreEqual(Line("wrap_paper"), StockCatalog.LineOfSlot(StationKind.Assembly, BuildRules.DispenserCount + BuildRules.Paper));
            Assert.AreEqual(Line("paper_bag"), StockCatalog.LineOfSlot(StationKind.Assembly, BuildRules.DispenserCount + BuildRules.Bag));
            Assert.AreEqual(-1, StockCatalog.LineOfSlot(StationKind.Assembly, 0), "a sauce dispenser draws from the shelf's bottles");
            Assert.AreEqual(Line("chili_sauce"), StockCatalog.SauceLine(1));
            Assert.AreEqual(Line("lemonade"), StockCatalog.LineOfSlot(StationKind.DrinkDispenser, 0));
            Assert.AreEqual(Line(Drinks.CupId), StockCatalog.LineOfSlot(StationKind.DrinkDispenser, DrinkRules.CupTool));
            Assert.AreEqual(-1, StockCatalog.LineOfSlot(StationKind.DrinkDispenser, DrinkRules.LidTool), "lids are free");
            Assert.AreEqual(-1, StockCatalog.LineOfSlot(StationKind.Stove, 0));
            foreach (StationKind kind in new[] { StationKind.Fridge, StationKind.Shelf, StationKind.Assembly, StationKind.DrinkDispenser })
                for (int slot = 0; slot < 12; slot++)
                {
                    int option = StockCatalog.UnpackOptionOf(kind, slot);
                    if (StockCatalog.LineOfSlot(kind, slot) < 0)
                    {
                        Assert.AreEqual(-1, option, kind + " " + slot);
                        continue;
                    }
                    Assert.AreEqual(slot, StockCatalog.UnpackSlotOf(kind, option), kind + " " + slot + " round trip");
                }
            Assert.AreEqual(-1, StockCatalog.UnpackSlotOf(StationKind.DrinkDispenser, DrinkRules.PourStopOption(0)), "a release is no unpack");
            Assert.AreEqual(-1, StockCatalog.UnpackSlotOf(StationKind.Assembly, BuildRules.SqueezeStartOption(0)));
        }

        // ---- the stock room ----

        [Test]
        public void ATake_DecrementsTheStock_AndAnEmptySlotRefusesWithOutOfStock()
        {
            var room = new StockRoom();
            int tomato = Line("tomato");
            int before = room.Version;
            Assert.AreEqual(CookOutcome.Ok, room.Take(tomato));
            Assert.AreEqual(5, room.LineOf(tomato).Count);
            Assert.Greater(room.Version, before, "the counts are sent again");
            while (room.LineOf(tomato).Count > 0) room.Take(tomato);
            int empty = room.Version;
            Assert.AreEqual(CookOutcome.OutOfStock, room.Take(tomato));
            Assert.AreEqual(empty, room.Version, "a refusal changes nothing");
            Assert.IsFalse(room.CanTake(tomato));
            Assert.AreEqual(CookOutcome.InvalidOption, room.Take(-1));
        }

        [Test]
        public void AnOrder_ChargesOnce_TheBulkPrice_AndARefusalChargesNothing()
        {
            var room = new StockRoom();
            var wallet = new CountingSupplyWallet();
            int tomato = Line("tomato");
            Assert.AreEqual(CookOutcome.Ok, room.Order(tomato, 2, "p3", 600, Catalog, wallet, out SupplyOrder order));
            Assert.AreEqual(1, wallet.Charges, "charged exactly once");
            Assert.AreEqual(2 * 6 * 56, wallet.Paid, "2 packs × 6 × $70 × 0.8");
            Assert.AreEqual(wallet.Paid, order.Cost);
            Assert.AreEqual(660, order.DueMinute, "one game hour later");
            Assert.AreEqual(1, room.Book.Pending.Count);
            Assert.AreEqual(TerminalRules.PriceOf(Catalog, tomato, 2), order.Cost, "the terminal shows what is charged");

            var broke = new CountingSupplyWallet { Broke = true };
            Assert.AreEqual(CookOutcome.NotEnoughMoney, room.Order(tomato, 1, "p3", 600, Catalog, broke, out _));
            Assert.AreEqual(0, broke.Charges);
            var refusing = new CountingSupplyWallet { Refuse = CookOutcome.NotEnoughMoney };
            Assert.AreEqual(CookOutcome.NotEnoughMoney, room.Order(tomato, 1, "p3", 600, Catalog, refusing, out _));
            Assert.AreEqual(0, refusing.Refunds, "a refused charge is no charge");
            Assert.AreEqual(CookOutcome.InvalidOption, room.Order(tomato, 0, "p3", 600, Catalog, wallet, out _));
            Assert.AreEqual(CookOutcome.InvalidOption, room.Order(tomato, Supplier.MaxPacksPerOrder + 1, "p3", 600, Catalog, wallet, out _));
            Assert.AreEqual(CookOutcome.InvalidOption, room.Order(99, 1, "p3", 600, Catalog, wallet, out _));
            for (int i = 1; i < SupplyBook.MaxPending; i++) room.Order(tomato, 1, "p3", 600, Catalog, wallet, out _);
            int charges = wallet.Charges;
            Assert.AreEqual(CookOutcome.TooManyOrders, room.Order(tomato, 1, "p3", 600, Catalog, wallet, out _));
            Assert.AreEqual(charges, wallet.Charges, "too many orders: nothing charged");
            Assert.AreEqual(1, room.Book.Pending.Count(o => o.Packs == 2));
        }

        [Test]
        public void TheRefundBackstop_GivesTheMoneyBack_WhenAPaidOrderCannotBeBooked()
        {
            var room = new StockRoom();
            int tomato = Line("tomato");
            var filler = new CountingSupplyWallet();
            for (int i = 1; i < SupplyBook.MaxPending; i++) room.Order(tomato, 1, "p", 0, Catalog, filler, out _);
            // While this order is being paid another one takes the last place on the book: the paid order cannot be booked.
            var wallet = new CountingSupplyWallet();
            wallet.During = () => room.Order(tomato, 1, "q", 0, Catalog, filler, out _);
            Assert.AreEqual(CookOutcome.TooManyOrders, room.Order(tomato, 1, "p", 0, Catalog, wallet, out SupplyOrder none));
            Assert.IsNull(none);
            Assert.AreEqual(1, wallet.Charges);
            Assert.AreEqual(1, wallet.Refunds, "refunded once");
            Assert.AreEqual(wallet.Paid, wallet.Refunded, "all of it");
            Assert.AreEqual(SupplyBook.MaxPending, room.Book.Pending.Count);
        }

        [Test]
        public void ADelivery_ComesAfterTheDelay_OneBoxPerPack_AndWaitsForAFreeSpot()
        {
            var room = new StockRoom();
            int wrap = Line("wrap");
            room.Order(wrap, 2, "p", 600, Catalog, null, out _);
            var arrived = new List<SupplyOrder>();
            room.Deliver(659, arrived);
            Assert.IsEmpty(arrived);
            Assert.AreEqual(0, room.WaitingBoxes, "not before the hour");
            room.Deliver(660, arrived);
            Assert.AreEqual(1, arrived.Count);
            Assert.AreEqual(2, room.WaitingBoxes, "one box per pack");
            Assert.IsEmpty(room.Book.Pending);
            Assert.IsTrue(room.TryPeekWaiting(out BoxContents box));
            Assert.AreEqual((wrap, 6), (box.Line, box.Units));
            room.ConfirmSpawned(1001);
            Assert.AreEqual(1, room.WaitingBoxes);
            Assert.AreEqual(1, room.BoxesInShop);
            Assert.IsTrue(room.TryGetBox(1001, out BoxContents spawned));
            Assert.AreEqual(6, spawned.Units);
        }

        [Test]
        public void AnUnpack_FillsUpToTheCapacity_TheRestStaysInTheBox_AnEmptyBoxIsGone()
        {
            var room = new StockRoom();
            int wrap = Line("wrap"); // 4 of 8 at the start
            room.Order(wrap, 2, "p", 0, Catalog, null, out _);
            room.Deliver(60, null);
            room.ConfirmSpawned(7);
            room.ConfirmSpawned(8);
            Assert.AreEqual(CookOutcome.WrongBox, room.Unpack(7, Line("tomato"), out _, out int kept));
            Assert.AreEqual(6, kept);
            Assert.AreEqual(CookOutcome.Ok, room.Unpack(7, wrap, out int accepted, out int left));
            Assert.AreEqual((4, 2), (accepted, left), "4 fit (to 8), 2 stay in the box");
            Assert.AreEqual(8, room.LineOf(wrap).Count);
            Assert.IsTrue(room.TryGetBox(7, out BoxContents rest));
            Assert.AreEqual(2, rest.Units);
            Assert.AreEqual(CookOutcome.StockFull, room.Unpack(8, wrap, out _, out int full));
            Assert.AreEqual(6, full, "a full slot takes nothing");
            room.Take(wrap);
            room.Take(wrap);
            room.Take(wrap);
            Assert.AreEqual(CookOutcome.Ok, room.Unpack(7, wrap, out accepted, out left));
            Assert.AreEqual((2, 0), (accepted, left));
            Assert.IsFalse(room.TryGetBox(7, out _), "the empty box is gone");
            Assert.AreEqual(CookOutcome.NotFood, room.Unpack(7, wrap, out _, out _));
        }

        [Test]
        public void TheCounts_AreOneBytePerLine_InTheCatalogOrder()
        {
            var room = new StockRoom();
            var bytes = new byte[StockCatalog.Count];
            room.WriteCounts(bytes);
            Assert.AreEqual(6, bytes[Line("tomato")]);
            Assert.AreEqual(10, bytes[Line(Drinks.CupId)]);
            Assert.AreEqual(20, bytes[Line("soda")], "cups left in the soda kegs");
            room.Draw(Line("soda"), 90, 1, out int drawn);
            Assert.AreEqual(90, drawn);
            room.WriteCounts(bytes);
            Assert.AreEqual(19, bytes[Line("soda")], "19.1 cups");
            Assert.AreEqual(3, bytes[Line("garlic_herb_sauce")], "sealed bottles");
            room.Draw(Line("garlic_herb_sauce"), 10, 1, out _);
            room.WriteCounts(bytes);
            Assert.AreEqual(2, bytes[Line("garlic_herb_sauce")], "the opened bottle is in the dispenser");
        }

        // ---- the stock wallet: the board and the dispenser draw from stock ----

        [Test]
        public void TheStockWallet_DrawsSqueezesPackagingAndPours_FromStock_AndPutsBackARefund()
        {
            var room = new StockRoom();
            var wallet = new StockWallet(room);
            Assert.AreEqual(CookOutcome.OutOfStock, wallet.Short);
            int garlic = Line("garlic_herb_sauce");
            Assert.AreEqual(CookOutcome.Ok, wallet.Charge(Owner, "garlic_herb_sauce", 30, out long cost));
            Assert.AreEqual(0, cost, "no money: the order paid for it");
            Assert.AreEqual((2, 70), (room.LineOf(garlic).Count, room.LineOf(garlic).OpenPercent), "a bottle opened, 30 % used");
            wallet.Refund(Owner, "garlic_herb_sauce", 30, 0);
            Assert.AreEqual((3, 0), (room.LineOf(garlic).Count, room.LineOf(garlic).OpenPercent), "put back");
            int paper = Line("wrap_paper");
            Assert.AreEqual(CookOutcome.Ok, wallet.Charge(Owner, "wrap_paper", 100, out _));
            Assert.AreEqual(9, room.LineOf(paper).Count);
            wallet.Refund(Owner, "wrap_paper", 100, 0);
            Assert.AreEqual(10, room.LineOf(paper).Count);
            int lemonade = Line("lemonade");
            IDrinkWallet drinks = wallet;
            Assert.AreEqual(CookOutcome.Ok, drinks.Charge(Owner, "lemonade", 90, true, out _));
            Assert.AreEqual(1910, room.LineOf(lemonade).AvailablePercent);
            drinks.Refund(Owner, "lemonade", 90, true, 0);
            Assert.AreEqual(2000, room.LineOf(lemonade).AvailablePercent);
            while (room.CanDraw(lemonade, 1)) room.Draw(lemonade, 100, 1, out _);
            Assert.IsFalse(drinks.CanAfford(Owner, "lemonade", 1, false), "the kegs are empty");
            Assert.AreEqual(CookOutcome.OutOfStock, drinks.Charge(Owner, "lemonade", 50, false, out _));
            Assert.IsFalse(wallet.CanAfford(Owner, "basil", 10), "034 review: a good the shop does not stock is never free");
        }

        [Test]
        public void ASqueeze_AtAnEmptySauce_IsRefusedAtThePress_WithOutOfStock()
        {
            var kitchen = KitchenState.CreateDemo();
            kitchen.RegisterAssemblySpot(90, 8);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateIngredient("wrap", out Item wrap));
            kitchen.Register(40, wrap);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(40, 90, VesselKind.Assembly, Start));
            var room = new StockRoom();
            int garlic = Line("garlic_herb_sauce");
            while (room.CanDraw(garlic, 1)) room.Draw(garlic, 30, 1, out _);
            var desk = new AssemblyDesk(kitchen);
            Assert.AreEqual(CookOutcome.OutOfStock, desk.Press(8, 0, Owner, Start, new StockWallet(room)));
            Assert.AreEqual(0, kitchen.AssemblyFlags(8), "the dispenser is not taken");
            Assert.AreEqual(CookOutcome.Ok, desk.Press(8, 1, Owner, Start, new StockWallet(room)), "the chili still has bottles");
            Assert.AreEqual(CookOutcome.Ok, desk.Release(8, 1, Owner, Real(1.0), Rate, new StockWallet(room), out _));
            Assert.AreEqual((2, 80), (room.LineOf(Line("chili_sauce")).Count, room.LineOf(Line("chili_sauce")).OpenPercent), "1 s = 20 % of a bottle");
        }

        // ---- the refund backstop ----

        private sealed class FakeTake : IStockTakeSteps
        {
            public bool HandAnswer = true;
            public CookOutcome PayAnswer = CookOutcome.Ok;
            public int Pays, Refunds;
            public CookOutcome Check() => CookOutcome.Ok;
            public CookOutcome Create() => CookOutcome.Ok;
            public bool CanHandOver() => true;
            public CookOutcome Pay() { Pays++; return PayAnswer; }
            public bool HandOver() => HandAnswer;
            public void Refund() => Refunds++;
        }

        [Test]
        public void StockTakeSequence_RefundsOnce_WhenTheHandOverFailsAfterThePayment()
        {
            var failing = new FakeTake { HandAnswer = false };
            Assert.AreEqual(CookOutcome.SimRefused, StockTakeSequence.Run(failing));
            Assert.AreEqual((1, 1), (failing.Pays, failing.Refunds), "paid once, given back once");
            var ok = new FakeTake();
            Assert.AreEqual(CookOutcome.Ok, StockTakeSequence.Run(ok));
            Assert.AreEqual(0, ok.Refunds);
            var empty = new FakeTake { PayAnswer = CookOutcome.OutOfStock };
            Assert.AreEqual(CookOutcome.OutOfStock, StockTakeSequence.Run(empty));
            Assert.AreEqual(0, empty.Refunds, "nothing taken, nothing to give back");
        }

        /// <summary>A wallet whose charge runs <see cref="During"/> (the world changes while it charges), counting refunds.</summary>
        private sealed class ChangingWallet : IAssemblyWallet, IDrinkWallet
        {
            public Action During;
            public int Charges, Refunds;
            public CookOutcome Short => CookOutcome.NotEnoughMoney;

            public CookOutcome Charge(int owner, string grocerId, int percent, out long cost)
            {
                Charges++;
                cost = 7;
                During?.Invoke();
                return CookOutcome.Ok;
            }

            public bool CanAfford(int owner, string grocerId, int percent) => true;
            public void Refund(int owner, string grocerId, int percent, long cost) => Refunds++;

            public CookOutcome Charge(int owner, string drinkId, int percent, bool withCup, out long cost)
            {
                Charges++;
                cost = 7;
                During?.Invoke();
                return CookOutcome.Ok;
            }

            public bool CanAfford(int owner, string drinkId, int percent, bool withCup) => true;
            public void Refund(int owner, string drinkId, int percent, bool withCup, long cost) => Refunds++;
        }

        [Test]
        public void TheAssemblyBoard_RefundsASqueezeAndAPackaging_ThatCouldNotBeCommitted()
        {
            var kitchen = KitchenState.CreateDemo();
            kitchen.RegisterAssemblySpot(90, 8);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateIngredient("wrap", out Item wrap));
            kitchen.Register(40, wrap);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(40, 90, VesselKind.Assembly, Start));
            var desk = new AssemblyDesk(kitchen);
            var wallet = new ChangingWallet();
            wallet.During = () => kitchen.Clear(); // the board is gone while the portion is charged
            Assert.AreEqual(CookOutcome.Ok, desk.Press(8, 0, Owner, Start, wallet));
            Assert.AreEqual(CookOutcome.SimRefused, desk.Release(8, 0, Owner, Real(0.8), Rate, wallet, out _));
            Assert.AreEqual((1, 1), (wallet.Charges, wallet.Refunds), "charged once, refunded once");

            var ok = new ChangingWallet();
            KitchenState fresh = KitchenState.CreateDemo();
            fresh.RegisterAssemblySpot(90, 8);
            fresh.TryCreateIngredient("wrap", out Item wrap2);
            fresh.Register(40, wrap2);
            fresh.TryPutInto(40, 90, VesselKind.Assembly, Start);
            var deskTwo = new AssemblyDesk(fresh);
            deskTwo.Press(8, 0, Owner, Start, ok);
            Assert.AreEqual(CookOutcome.Ok, deskTwo.Release(8, 0, Owner, Real(0.8), Rate, ok, out _));
            Assert.AreEqual((1, 0), (ok.Charges, ok.Refunds), "committed: no refund");
        }

        [Test]
        public void TheDrinkDispenser_RefundsAPour_ThatCouldNotBeCommitted()
        {
            var kitchen = KitchenState.CreateDemo();
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateCup(out Item cup));
            kitchen.Register(50, cup);
            var desk = new DrinkDesk(kitchen);
            var wallet = new ChangingWallet();
            wallet.During = () => kitchen.TryRemove(50, out _); // the cup is gone while the pour is charged
            Assert.AreEqual(CookOutcome.Ok, desk.Press(10, 0, Owner, 50, Start, wallet));
            Assert.AreEqual(CookOutcome.SimRefused, desk.Release(10, 0, Owner, Real(1.8), Rate, 50, wallet, out _));
            Assert.AreEqual((1, 1), (wallet.Charges, wallet.Refunds), "charged once, refunded once");
            Assert.IsFalse(kitchen.TryGet(50, out _), "no cup came back from nowhere");
        }

        // ---- the 033 review: the pour record ----

        [Test]
        public void APour_FillsOnlyTheItemOfThePress_AndGoesWithTheCup()
        {
            var kitchen = KitchenState.CreateDemo();
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateCup(out Item cup));
            kitchen.Register(50, cup);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryStartPour(10, 0, Owner, 50, Start));
            // A new item under the same id (a despawned cup's id reused): the pour is dropped, it never fills the new one.
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateCup(out Item other));
            kitchen.Register(50, other);
            Assert.IsFalse(kitchen.IsPouring(10, 0, out _, out _, out _), "Register cancels the pour into that id");
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryStartPour(10, 0, Owner, 50, Start));
            Assert.IsTrue(kitchen.TryRemove(50, out _));
            Assert.IsFalse(kitchen.IsPouring(10, 0, out _, out _, out _), "TryRemove cancels it too");
            Assert.AreEqual(0, kitchen.DrinkFlags(10), "the tap is free");
        }

        [Test]
        public void APour_RefusesAnotherItemThanThePresss_AndTheCupsMlLeavesWithIt()
        {
            var kitchen = KitchenState.CreateDemo();
            var desk = new DrinkDesk(kitchen);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateCup(out Item cup));
            kitchen.Register(50, cup);
            desk.Press(10, 0, Owner, 50, Start, null);
            Assert.AreEqual(CookOutcome.Ok, desk.Release(10, 0, Owner, Real(1.8), Rate, 50, null, out _));
            Assert.Greater(kitchen.CupMl(50), 300f);
            // The cup goes onto... nothing takes it (033 review 2), so take it away the other way: the registry forgets its ml.
            Assert.IsTrue(kitchen.TryRemove(50, out _));
            Assert.AreEqual(0f, kitchen.CupMl(50), "the exact ml leaves with the cup");
        }

        // ---- the 033 review: the tab ----

        private static GuestPick FoodLine(int guest) =>
            new GuestPick(guest, Menu.FalafelWrap, new OrderFit(OrderMatch.Exact, 0, 0.7f, 4), 1000, 300, 1200);

        private static GuestPick DrinkLine(int guest) =>
            new GuestPick(guest, Menu.FalafelWrap, new OrderFit(OrderMatch.Exact, 0, 0.9f, 5), OrderRules.GuestSharePermille(OrderMatch.Exact), 450, 1200, true);

        [Test]
        public void TheTab_RefusesALineAlreadyServed_ChangingNothing()
        {
            var tab = new GuestTab();
            tab.Open(1, "lemonade");
            Assert.IsFalse(tab.Serve(FoodLine(1), Owner, 800, out _, out _));
            Assert.IsTrue(tab.IsDuplicate(FoodLine(1)));
            Assert.IsFalse(tab.Serve(FoodLine(1), Owner, 900, out _, out long refused), "a second dish is refused");
            Assert.AreEqual(0, refused);
            Assert.IsTrue(tab.WantsDrink(1));
            Assert.IsTrue(tab.Serve(DrinkLine(1), Owner, 100, out _, out long bill));
            Assert.AreEqual(900, bill, "only the first dish's bill (800) and the drink (100)");
            tab.Open(2, null);
            Assert.IsFalse(tab.IsDuplicate(DrinkLine(2)), "no tab: nothing to refuse");
        }

        [Test]
        public void ACompletedOrder_WhoseHandOverFails_IsPaidWhole_ByTheFallback_Once()
        {
            var tab = new GuestTab();
            tab.Open(1, "lemonade");
            tab.Serve(FoodLine(1), Owner, 800, out _, out _);
            Assert.IsTrue(tab.Serve(DrinkLine(1), Owner, 150, out _, out long bill));
            Assert.AreEqual(950, bill);
            // The register's line never took it (a throw between the tab and the desk): the fallback pays both lines, once.
            Assert.IsTrue(CustomerService.FallbackPaysFor(false, false, tab.IsBilled(1, true)));
            Assert.AreEqual(950, CustomerService.FallbackBill(tab, DrinkLine(1), 300));
            Assert.AreEqual(GuestTab.LineBill(DrinkLine(1), 300), CustomerService.FallbackBill(tab, DrinkLine(1), 300), "once");
            // Once the register's line took it, nothing is kept for the fallback.
            tab.Open(2, "soda");
            tab.Serve(FoodLine(2), Owner, 800, out _, out _);
            tab.Serve(DrinkLine(2), Owner, 150, out _, out _);
            tab.Close(2);
            Assert.IsFalse(tab.TakeCompletedBill(2, out _));
            // A duplicate line that was billed before its hand-over is no commitment for this dish: the fallback pays it.
            Assert.IsTrue(CustomerService.FallbackPaysFor(false, true, true));
            Assert.IsFalse(CustomerService.FallbackPaysFor(false, false, true), "a first line on the tab is paid there");
            Assert.IsFalse(CustomerService.FallbackPaysFor(true, false, false), "handed over: the register pays");
        }

        // ---- the terminal ----

        [Test]
        public void TheTerminal_OrdersALineByItsOption_PagesAndPacksCycle()
        {
            for (int line = 0; line < StockCatalog.Count; line++)
                for (int packs = 1; packs <= Supplier.MaxPacksPerOrder; packs++)
                {
                    int option = TerminalRules.OrderOption(line, packs);
                    Assert.GreaterOrEqual(option, TerminalRules.OrderBase);
                    Assert.IsTrue(TerminalRules.TryParseOrder(option, out int l, out int p));
                    Assert.AreEqual((line, packs), (l, p));
                }
            for (int slot = 0; slot < TerminalRules.SlotCount; slot++) Assert.IsFalse(TerminalRules.TryParseOrder(slot, out _, out _));
            Assert.IsFalse(TerminalRules.TryParseOrder(TerminalRules.OrderOption(0, 0), out _, out _));
            Assert.IsFalse(TerminalRules.TryParseOrder(TerminalRules.OrderOption(StockCatalog.Count, 1), out _, out _));
            Assert.AreEqual(5, TerminalRules.Pages); // spec 036: the cones are the 25th line
            Assert.AreEqual(TerminalRules.PageOf(Line("tomato")), Line("tomato") / TerminalRules.Rows);
            Assert.AreEqual(Line("tomato"), TerminalRules.LineAt(TerminalRules.PageOf(Line("tomato")), Line("tomato") % TerminalRules.Rows));
            Assert.AreEqual(-1, TerminalRules.LineAt(TerminalRules.Pages - 1, TerminalRules.Rows), "no row past the list");
            int page = 0, packs1 = 1;
            TerminalView.Apply(TerminalRules.PrevSlot, ref page, ref packs1);
            Assert.AreEqual(TerminalRules.Pages - 1, page, "page back from the first wraps round");
            TerminalView.Apply(TerminalRules.NextSlot, ref page, ref packs1);
            Assert.AreEqual(0, page);
            TerminalView.Apply(TerminalRules.PacksSlot, ref page, ref packs1);
            TerminalView.Apply(TerminalRules.PacksSlot, ref page, ref packs1);
            Assert.AreEqual(3, packs1);
            TerminalView.Apply(TerminalRules.PacksSlot, ref page, ref packs1);
            Assert.AreEqual(1, packs1);
            Assert.AreEqual("order 1 pack of tomato ($336, 1 h)", TerminalRules.OrderWords(Line("tomato"), 1, 336, 60));
            Assert.AreEqual("order a keg of lemonade ($960, 1 h)", TerminalRules.OrderWords(Line("lemonade"), 1, 960, 60));
            StringAssert.Contains("0/12 !", TerminalRules.RowText(Line("tomato"), 0, 1, 336), "an empty line is marked");
            Assert.AreEqual("14:05", TerminalRules.ClockOf(14 * 60 + 5));
        }

        // ---- the client: rules, looks, HUD ----

        [Test]
        public void AnEmptySlot_OffersNothing_AndSaysOutOfStock_ElseWhatIsLeft()
        {
            int tomato = Line("tomato");
            AimTarget empty = AimTarget.Crate("tomato", 70).WithStock(tomato, 0);
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Nothing, empty));
            Assert.AreEqual(InteractionRules.OutOfStockText, InteractionRules.PromptFor(Held.Nothing, empty));
            Assert.AreEqual(InteractionRules.OutOfStockText, InteractionRules.PromptFor(Held.Food("tomato", false), empty), "with full hands too");
            Assert.IsNull(InteractionRules.PromptFor(Held.Food("tomato", false), AimTarget.Crate("tomato", 70).WithStock(tomato, 3)),
                "full hands at a stocked slot: nothing to say, as before");
            AimTarget some = AimTarget.Crate("tomato", 70).WithStock(tomato, 4);
            Assert.AreEqual(InteractionAction.Buy, InteractionRules.ActionFor(Held.Nothing, some));
            Assert.AreEqual("Click · take tomato (4 left)", InteractionRules.PromptFor(Held.Nothing, some));
            AimTarget crate = AimTarget.Crate("potato", 60);
            Assert.AreEqual("Click · take potato ($60)", InteractionRules.PromptFor(Held.Nothing, crate), "a crate keeps its price");
            AimTarget noCups = AimTarget.DrinkTool(DrinkRules.CupTool, 0, "a cup", -1).WithStock(Line(Drinks.CupId), 0);
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Nothing, noCups));
            Assert.AreEqual(InteractionRules.OutOfStockText, InteractionRules.PromptFor(Held.Nothing, noCups));
            AimTarget dryTap = AimTarget.DrinkTool(0, 0, "lemonade", -1).WithStock(Line("lemonade"), 0);
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Cup("empty cup", -1, DrinkRules.LevelEmpty, false), dryTap));
        }

        [Test]
        public void ACarriedBox_UnpacksOnlyIntoItsOwnSlot_WithRoom()
        {
            int tomato = Line("tomato");
            Held box = Held.Box("box of tomato", tomato, 6);
            Assert.IsTrue(box.IsBox);
            AimTarget slot = AimTarget.Crate("tomato", 70).WithStock(tomato, 8);
            Assert.AreEqual(InteractionAction.Unpack, InteractionRules.ActionFor(box, slot));
            Assert.AreEqual("Click · unpack 6 tomato (4 fit)", InteractionRules.PromptFor(box, slot));
            AimTarget roomy = AimTarget.Crate("tomato", 70).WithStock(tomato, 0);
            Assert.AreEqual("Click · unpack 6 tomato", InteractionRules.PromptFor(box, roomy));
            AimTarget full = AimTarget.Crate("tomato", 70).WithStock(tomato, 12);
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(box, full));
            Assert.AreEqual("That slot is full", InteractionRules.PromptFor(box, full));
            AimTarget other = AimTarget.Crate("lettuce", 45).WithStock(Line("lettuce"), 2);
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(box, other));
            Assert.AreEqual("This box is for the tomato", InteractionRules.PromptFor(box, other));
            Assert.IsTrue(Held.Pending(box).IsBox, "a box on its way into the hands stays a box");
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Pending(box), slot), "but does nothing yet");
            Held keg = Held.Box("box of lemonade", Line("lemonade"), 1);
            AimTarget tap = AimTarget.DrinkTool(0, 0, "lemonade", -1).WithStock(Line("lemonade"), 20);
            Assert.AreEqual(InteractionAction.Unpack, InteractionRules.ActionFor(keg, tap), "a keg goes into its tap");
            Assert.AreEqual("Click · unpack a keg of lemonade", InteractionRules.PromptFor(keg, tap));
        }

        [Test]
        public void TheTerminal_RowsOrder_ButtonsPage()
        {
            AimTarget row = AimTarget.TerminalSlot(2, "order 1 pack of tomato ($336, 1 h)", 336);
            Assert.AreEqual(InteractionAction.Order, InteractionRules.ActionFor(Held.Nothing, row));
            Assert.AreEqual("Click · order 1 pack of tomato ($336, 1 h)", InteractionRules.PromptFor(Held.Nothing, row));
            Assert.AreEqual(InteractionAction.Order, InteractionRules.ActionFor(Held.Knife, row), "any hands order");
            AimTarget next = AimTarget.TerminalSlot(TerminalRules.NextSlot, "next page (2/4)", -1);
            Assert.AreEqual(InteractionAction.Page, InteractionRules.ActionFor(Held.Nothing, next));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Nothing, AimTarget.TerminalSlot(5, null, -1)), "past the list");
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Nothing, AimTarget.TerminalSlot(-1, null, -1)), "the frame");
            Assert.IsNull(InteractionRules.PromptFor(Held.Nothing, AimTarget.TerminalSlot(-1, null, -1)));
        }

        [Test]
        public void ASlotLook_ShowsTheCount_InSamplesAndItsTag()
        {
            Assert.AreEqual(0, StockSlotLook.VisibleSamples(0, 3), "empty: none");
            Assert.AreEqual(1, StockSlotLook.VisibleSamples(1, 3));
            Assert.AreEqual(3, StockSlotLook.VisibleSamples(6, 3), "at most all of them");
            Assert.AreEqual(2, StockSlotLook.VisibleSamples(5, 2));
            int tomato = Line("tomato");
            Assert.AreEqual("tomato · empty", StockSlotLook.TagText("tomato", tomato, 0));
            Assert.AreEqual("tomato 4/12", StockSlotLook.TagText("tomato", tomato, 4));
            Assert.AreEqual(Palette.Terracotta, StockSlotLook.TagColour(tomato, 0));
            Assert.AreEqual(Palette.Mustard, StockSlotLook.TagColour(tomato, 2), "below a quarter");
            Assert.AreEqual(Palette.Cream, StockSlotLook.TagColour(tomato, 3));
        }

        [Test]
        public void TheHud_ListsTheLowLines()
        {
            var counts = new byte[StockCatalog.Count];
            for (int i = 0; i < counts.Length; i++) counts[i] = (byte)StockCatalog.PresentedCapacity(i);
            Assert.AreEqual(string.Empty, HudView.LowStockText(counts));
            counts[Line("tomato")] = 2;
            counts[Line(Drinks.CupId)] = 4;
            Assert.AreEqual("Low stock: tomato, cups", HudView.LowStockText(counts));
            counts[Line("tomato")] = 3;
            Assert.AreEqual("Low stock: cups", HudView.LowStockText(counts), "3 of 12 is a quarter, not below");
            Assert.AreEqual(string.Empty, HudView.LowStockText(null));
        }

        [Test]
        public void ABox_Packs_ItsContents_InOneUshort_AndTipsWhenUnpacked()
        {
            var box = new BoxContents(Line("lemonade"), 1);
            Assert.IsTrue(BoxContents.TryUnpack(box.Pack(), out BoxContents back));
            Assert.AreEqual((box.Line, box.Units), (back.Line, back.Units));
            Assert.AreEqual(0, new BoxContents(-1, 6).Pack());
            Assert.AreEqual(0, new BoxContents(0, 0).Pack());
            Assert.IsFalse(BoxContents.TryUnpack(0, out _));
            Assert.AreEqual(0f, BoxLookRules.UnpackAngle(0f));
            Assert.Greater(BoxLookRules.UnpackAngle(BoxLookRules.UnpackSeconds * 0.5f), 20f);
            Assert.AreEqual(0f, BoxLookRules.UnpackAngle(BoxLookRules.UnpackSeconds));
        }

        [Test]
        public void ACarriedBox_IsDrawnLowInTheMiddle_ItsTopBelowTheViewCentre()
        {
            const float fov = 70f;
            float scale = HoldViewRules.BoxFitScale(ArtSizeTable.DeliveryBoxSize * 1.6f, fov); // the box's diagonal
            float share = HoldViewRules.Share(ArtSizeTable.DeliveryBoxSize * 1.6f, fov) * scale;
            Assert.LessOrEqual(share, HoldViewRules.BoxMaxShare + 1e-4f);
            Assert.Less(HoldViewRules.BoxCentreY + share * 0.5f, 0.5f, "its top stays below the view centre");
            Assert.AreEqual(0f, HoldViewRules.BoxCentreInView(fov, 16f / 9f).x, 1e-4f, "in the middle");
        }

        // ---- the synced surface ----

        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static Type SyncArgument(FieldInfo field) =>
            field.FieldType.IsGenericType && field.FieldType.Name.StartsWith("Sync") ? field.FieldType.GetGenericArguments()[0] : null;

        private static string[] Synced(Type type) => type.GetFields(Instance).Where(f => SyncArgument(f) != null).Select(f => f.Name).ToArray();

        [Test]
        public void TheSyncedSurface_IsAByteperLine_AndABoxsUshort_NoRpc()
        {
            CollectionAssert.AreEquivalent(new[] { "Counts" }, Synced(typeof(StockCounts)));
            FieldInfo counts = typeof(StockCounts).GetField("Counts", Instance);
            StringAssert.StartsWith("SyncList", counts.FieldType.Name);
            Assert.AreEqual(typeof(byte), SyncArgument(counts), "one byte per stock line");
            CollectionAssert.AreEquivalent(new[] { "Contents" }, Synced(typeof(SupplyBox)));
            Assert.AreEqual(typeof(ushort), SyncArgument(typeof(SupplyBox).GetField("Contents", Instance)), "a box's line and units");
            foreach (Type t in new[] { typeof(StockCounts), typeof(SupplyBox), typeof(SupplyService), typeof(TerminalView) })
                Assert.IsFalse(t.GetMethods(Instance | BindingFlags.DeclaredOnly)
                    .Any(m => m.GetCustomAttributes(false).Any(a => a.GetType().Name.EndsWith("RpcAttribute"))), "no RPC on " + t.Name);
            Assert.IsEmpty(Synced(typeof(TerminalView)), "the terminal syncs nothing");
        }
    }
}
