using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Climb;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;
using Worktown.Game.Npc;
using Worktown.Game.Save;
using Worktown.Game.Shop;
using Worktown.Sim.Climb;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;
using Menu = Worktown.Game.Npc.Menu;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 037: street-food prices in cents (D39) and the menu board: its lines and references, its buttons, what guests pay, how many
    /// come and how much they tip, the register's coins and notes (AC3: $9.50 with a $10 note), the v1 save's money, the wire.
    /// </summary>
    public class MenuBoardTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/Graybox.unity";
        private static readonly ChoiceWeights Weights = ClimbCatalog.CreateDefault().Weights;
        private static int Fries => MenuPrices.LineOfDish(Menu.Fries);

        [Test]
        public void EveryDishAndDrink_HasALine_AtAStreetFoodReference()
        {
            Assert.AreEqual(Menu.All.Count + Drinks.All.Count, MenuPrices.Lines.Count);
            foreach (MenuDish dish in Menu.All) Assert.GreaterOrEqual(MenuPrices.LineOfDish(dish.Id), 0, dish.Name);
            foreach (BaseItemDef drink in Drinks.All) Assert.GreaterOrEqual(MenuPrices.LineOfDrink(drink.Id), 0, drink.Id);
            Assert.AreEqual(MenuPrices.Lines.Count, MenuPrices.Lines.Select(l => l.Id).Distinct().Count(), "unique save ids");
            foreach (MenuPrices.Line line in MenuPrices.Lines)
            {
                Assert.AreEqual(0, line.Reference % MenuPricing.StepCents, line.Id + " is on the $0.50 grid");
                Assert.That(line.Reference, Is.InRange(250L, 1000L), line.Id + ": a street-food price");
                Assert.AreEqual(MenuPrices.LineOfId(line.Id), MenuPrices.Lines.ToList().IndexOf(line));
            }
            // The climb's own dishes take the climb's reference prices (spec 052, in cents): the board and the climb never drift.
            ClimbCatalog climb = ClimbCatalog.CreateDefault();
            Assert.AreEqual(400, MenuPrices.Reference(Fries));
            Assert.AreEqual(climb.Dish("fries").ReferencePrice, MenuPrices.Reference(Fries));
            Assert.AreEqual(climb.Dish("falafel_wrap").ReferencePrice, MenuPrices.Reference(MenuPrices.LineOfDish(Menu.FalafelWrap)));
            Assert.AreEqual(900, MenuPrices.Reference(MenuPrices.LineOfDish(Menu.FalafelWrap)), "a wrap is about $9.00");
            Assert.AreEqual(1000, MenuPrices.Reference(MenuPrices.LineOfDish(Menu.SeitanWrap)));
            Assert.AreEqual(800, MenuPrices.Reference(MenuPrices.LineOfDish(Menu.GrilledVeggies)));
            Assert.AreEqual(300, MenuPrices.Reference(MenuPrices.LineOfDrink("lemonade")), "a drink $3.00");
            Assert.AreEqual("falafel_4", MenuPrices.IdOf("Falafel (4)"));
            foreach (MenuPrices.Line line in MenuPrices.Lines) Assert.IsNull(ContentRules.FindForbidden(line.Name), line.Name + " (D27)");
        }

        [Test]
        public void TheBoard_ShowsEveryLineInTheOpenKitchen_AndOnlyTheFriesAtTheFoodCorner()
        {
            Assert.AreEqual(MenuPrices.Lines.Count, MenuBoardRules.VisibleLines(ShopStageRules.OpenKitchen).Count);
            CollectionAssert.AreEqual(new[] { Fries }, MenuBoardRules.VisibleLines(ShopStageRules.FoodCorner), "one dish (D34)");
            Assert.AreEqual("MENU\n> Fries  $4.00", MenuBoardRules.DisplayText(ShopStageRules.FoodCorner, null, Fries));
            string kitchen = MenuBoardRules.DisplayText(ShopStageRules.OpenKitchen, null, MenuPrices.LineOfDish(Menu.FalafelWrap));
            Assert.AreEqual(1 + MenuBoardRules.ShownLines, kitchen.Split('\n').Length, "a window of six lines");
            StringAssert.Contains("> Falafel wrap  $9.00", kitchen);
            Assert.AreEqual("Left click · price up $0.50",
                InteractionRules.PromptFor(Held.Nothing, AimTarget.Button(StationKind.MenuBoard, MenuBoardRules.DearerOption, 0)));
            Assert.AreEqual("Left click · price down $0.50",
                InteractionRules.PromptFor(Held.Nothing, AimTarget.Button(StationKind.MenuBoard, MenuBoardRules.CheaperOption, 0)));
            Assert.AreEqual("Left click · next dish",
                InteractionRules.PromptFor(Held.Food("potato", false), AimTarget.Button(StationKind.MenuBoard, MenuBoardRules.NextOption, 0)), "any hands");
            CollectionAssert.AreEqual(new[] { "<", ">", "-", "+" }, Enumerable.Range(0, MenuBoardRules.OptionCount).Select(MenuBoardRules.ButtonLabel));
            Assert.IsTrue(ShopStageRules.IsOpen(ShopStageRules.FoodCorner, StationKind.MenuBoard, MenuBoardRules.DearerOption));
            StringAssert.Contains("first person", StationRules.Prompt(StationKind.MenuBoard, "Menu board", "E", "V"));
        }

        [Test]
        public void AClick_MovesTheSelectionOrThePrice_InFiftyCentSteps_WithinItsBounds()
        {
            var desk = new MenuBoardDesk(Weights);
            int changed = 0;
            desk.Changed += () => changed++;
            byte corner = ShopStageRules.FoodCorner;
            Assert.AreEqual(400, desk.PriceOf(Fries));
            Assert.AreEqual(CookOutcome.Ok, desk.Use(MenuBoardRules.DearerOption, corner));
            Assert.AreEqual(450, desk.PriceOf(Fries));
            Assert.AreEqual(Fries, desk.Selected);
            Assert.AreEqual(CookOutcome.Ok, desk.Use(MenuBoardRules.CheaperOption, corner));
            Assert.AreEqual(CookOutcome.Ok, desk.Use(MenuBoardRules.CheaperOption, corner));
            Assert.AreEqual(350, desk.PriceOf(Fries));
            for (int i = 0; i < 20; i++) desk.Use(MenuBoardRules.CheaperOption, corner);
            Assert.AreEqual(MenuPricing.LowestPrice(400), desk.PriceOf(Fries), "never below $0.50");
            for (int i = 0; i < 40; i++) desk.Use(MenuBoardRules.DearerOption, corner);
            Assert.AreEqual(800, desk.PriceOf(Fries), "at most $4.00 above the reference");
            Assert.AreEqual(CookOutcome.Ok, desk.Use(MenuBoardRules.NextOption, corner));
            Assert.AreEqual(Fries, desk.Selected, "the corner has one line");
            Assert.AreEqual(CookOutcome.InvalidOption, desk.Use(MenuBoardRules.OptionCount, corner));
            Assert.AreEqual(CookOutcome.InvalidOption, desk.Use(-1, corner));
            Assert.Greater(changed, 0);

            var kitchen = new MenuBoardDesk(Weights);
            byte open = ShopStageRules.OpenKitchen;
            kitchen.Use(MenuBoardRules.PreviousOption, open);
            Assert.AreEqual(MenuPrices.Lines.Count - 1, kitchen.Selected, "wraps to the last line");
            kitchen.Use(MenuBoardRules.NextOption, open);
            kitchen.Use(MenuBoardRules.NextOption, open);
            Assert.AreEqual(1, kitchen.Selected);
            kitchen.Use(MenuBoardRules.DearerOption, open);
            Assert.AreEqual(MenuPrices.Reference(1) + 50, kitchen.PriceOf(1));
            Assert.AreEqual(400, kitchen.PriceOf(Fries), "only the selected line moved");
            kitchen.Reset();
            Assert.AreEqual(MenuPrices.Reference(1), kitchen.PriceOf(1));
            Assert.AreEqual(0, kitchen.Selected);
        }

        [Test]
        public void GuestsPayTheBoardsPrice_FewerComeWhenItIsDear_AndTheyTipLess()
        {
            var desk = new MenuBoardDesk(Weights);
            byte corner = ShopStageRules.FoodCorner;
            Assert.AreEqual(400, desk.BillOf(Menu.Fries, false, null, 999));
            Assert.AreEqual(300, desk.BillOf(Menu.None, true, "lemonade", 999), "a drink line pays the ordered drink's price");
            Assert.AreEqual(999, desk.BillOf(Menu.None, true, "moon_juice", 999), "no such line: the sim's payout");
            CollectionAssert.AreEquivalent(new[] { "snack" }, desk.DemandPermille(corner).Keys, "the corner sells to the snack market only");
            Assert.AreEqual(1000, desk.DemandPermille(corner)["snack"], "at the reference: as before");
            Assert.AreEqual(1000, desk.TipPermilleOf(Menu.Fries));
            desk.Use(MenuBoardRules.DearerOption, corner);
            Assert.AreEqual(450, desk.BillOf(Menu.Fries, false, null, 0));
            Assert.AreEqual(MenuPricing.DemandPermille(Weights, 450, 400), desk.DemandPermille(corner)["snack"]);
            Assert.AreEqual(834, desk.DemandPermille(corner)["snack"], "fewer guests (052's guest choice)");
            Assert.AreEqual(675, desk.TipPermilleOf(Menu.Fries), "a smaller tip");
            desk.Use(MenuBoardRules.CheaperOption, corner);
            desk.Use(MenuBoardRules.CheaperOption, corner);
            Assert.AreEqual(1112, desk.DemandPermille(corner)["snack"], "cheap: more guests");
            Assert.AreEqual(1000, desk.TipPermilleOf(Menu.Fries), "cheap: the tip is not raised");
            IReadOnlyDictionary<string, int> open = new MenuBoardDesk(Weights).DemandPermille(ShopStageRules.OpenKitchen);
            CollectionAssert.AreEquivalent(new[] { "meal", "snack", "drink" }, open.Keys);
            Assert.IsTrue(open.Values.All(v => v == 1000));
        }

        [Test]
        public void TheBillAndTheTip_AreWholeCoins_AndTheTipFollowsTheBoard()
        {
            var pick = new GuestPick(1, Menu.Fries, new OrderFit(OrderMatch.Close, 1, 0.8f, 4), OrderRules.ClosePermille, 100, 1200);
            Assert.AreEqual(270, GuestTab.LineBill(pick, 450), "60 % of $4.50 is $2.70");
            Assert.AreEqual(240, GuestTab.LineBill(pick, 405), "$2.43 rounds down to whole coins");
            var flow = new RegisterFlow(new Shift());
            var exact = new GuestPick(2, Menu.Fries, new OrderFit(OrderMatch.Exact, 0, 1f, 5), OrderRules.ExactPermille, 100, 1200);
            RegisterPayment full = flow.HandOver(exact, 1, 450, new GuestOrder(new OrderModifier[0], 60, true, 1000), null, 0f);
            Assert.AreEqual(450, full.Bill);
            Assert.AreEqual(130, full.Tip, "30 % of $4.50 is $1.35: $1.30 in coins");
            var dear = new RegisterFlow(new Shift()) { TipShare = p => 675 };
            RegisterPayment less = dear.HandOver(exact, 1, 450, new GuestOrder(new OrderModifier[0], 60, true, 1000), null, 0f);
            Assert.AreEqual(90, less.Tip, "675 permille of $1.35 is $0.91: $0.90");
            Assert.AreEqual(0, less.Due % CashRules.CoinUnit);
        }

        [Test]
        public void AC3_AGuestPays950WithATenDollarNote_AndTheChangeIsCountedInCoins()
        {
            var flow = new RegisterFlow(new Shift());
            // 037 review: through the real path. The guest joined with the board at $9.50 (its quote), got an exact five-star dish (the
            // full price, BoardBill) at the very end of its patience (TipRules: no speed, no tip), so it owes $9.50 and hands a $10 note.
            var desk = new MenuBoardDesk(Weights);
            for (int i = 0; i < 7; i++) desk.Use(MenuBoardRules.NextOption, ShopStageRules.OpenKitchen);
            Assert.AreEqual(MenuPrices.LineOfDish(Menu.GrilledVeggies), desk.Selected);
            for (int i = 0; i < 3; i++) desk.Use(MenuBoardRules.DearerOption, ShopStageRules.OpenKitchen);
            BoardQuote quote = desk.Quote(Menu.GrilledVeggies, null);
            Assert.AreEqual(950, quote.DishPrice);
            var fit = new OrderFit(OrderMatch.Exact, 0, 0.95f, 5);
            var pick = new GuestPick(7, Menu.GrilledVeggies, fit, BoardBill.Permille(fit), 1200, 1200);
            long line = GuestTab.LineBill(pick, BoardBill.Base(quote.DishPrice, 4000, fit.Match));
            Assert.AreEqual(950, line);
            RegisterPayment p = flow.HandOver(pick, 1, line, new GuestOrder(new OrderModifier[0], 60, false, 1000), null, 0f, quote.TipPermille);
            Assert.AreEqual(0, p.Tip, "served as its patience ran out: no tip");
            Assert.AreEqual(950, p.Due);
            Assert.AreEqual(1000, p.Handed);
            Assert.AreEqual(50, p.ChangeDue);
            CollectionAssert.AreEqual(new[] { 50 }, CashRules.Coins(p.ChangeDue), "one 50-cent coin");
            Assert.AreEqual("CHANGE $0.00 / $0.50", RegisterRules.DisplayText(RegisterRules.PackPayment(true, false, p.ChangeDue), 0));
            var wallet = new Wallet();
            int fifty = CashRules.Tray.ToList().IndexOf(50);
            Assert.AreEqual(CookOutcome.Ok, flow.Use(1, fifty, wallet, null, 1f));
            Assert.AreEqual(CookOutcome.Ok, flow.Use(1, RegisterRules.GiveChangeOption, wallet, null, 2f));
            Assert.AreEqual(950, wallet.Paid, "paid once: the bill");
            Assert.AreEqual(1, wallet.Calls);
            Assert.AreEqual("50c", RegisterRules.TrayLabel(50));
            Assert.AreEqual("$10", RegisterRules.TrayLabel(1000));
            CollectionAssert.AreEqual(new[] { "10c", "20c", "50c", "$1", "$2", "$5", "$10", "$20" }, CashRules.Tray.Select(RegisterRules.TrayLabel));
            Assert.AreEqual(CashRules.Tray.Count + 3, RegisterRules.SlotCount, "the tray, take back, hand over, the card");
        }

        // ---- 037 review fixes ----

        [Test]
        public void Review1_TheBillAndTheTip_UseThePriceWhenTheGuestJoined_NotTheBoardAtTheHandOver()
        {
            var desk = new MenuBoardDesk(Weights);
            byte corner = ShopStageRules.FoodCorner;
            BoardQuote joined = desk.Quote(Menu.Fries, null);
            for (int i = 0; i < 8; i++) desk.Use(MenuBoardRules.DearerOption, corner); // + pressed right before the hand-over
            Assert.AreEqual(800, desk.PriceOf(Fries), "the board says $8.00 now");
            var fit = new OrderFit(OrderMatch.Exact, 0, 0.9f, 5);
            var pick = new GuestPick(1, Menu.Fries, fit, BoardBill.Permille(fit), 100, 1200);
            Assert.AreEqual(400, GuestTab.LineBill(pick, BoardBill.Base(joined.DishPrice, 460, fit.Match)), "billed at the $4.00 it joined at");
            Assert.AreEqual(1000, joined.TipPermille, "and tipped as at $4.00");
            RegisterPayment p = new RegisterFlow(new Shift()) { TipShare = x => desk.TipPermilleOf(x.Dish) }
                .HandOver(pick, 1, 400, new GuestOrder(new OrderModifier[0], 60, true, 1000), null, 0f, joined.TipPermille);
            Assert.AreEqual(120, p.Tip, "the stored share wins over the board's now");
            BoardQuote later = desk.Quote(Menu.Fries, "lemonade");
            Assert.AreEqual(800, later.DishPrice, "a guest joining now pays $8.00");
            Assert.AreEqual(300, later.DrinkPrice);
            Assert.AreEqual(-1, desk.Quote(Menu.Fries, null).DrinkPrice, "no drink ordered");
            Assert.Less(later.TipPermille, 1000);
        }

        [Test]
        public void Review2_ABill_NeverExceedsTheBoardsPrice_ThroughTheRealOrderFit()
        {
            // The old share (order quality over item quality) could reach 1.5 x; on board prices it is the match times the stars, at most 1.
            Assert.Greater(OrderRules.BillPermille(OrderMatch.Exact, 0.2f, 1f), 1000, "032's share overpays a fixed price");
            foreach (OrderMatch match in new[] { OrderMatch.Exact, OrderMatch.Close, OrderMatch.Wrong })
                for (int stars = 0; stars <= 6; stars++)
                    for (float q = 0f; q <= 1f; q += 0.1f)
                    {
                        var fit = new OrderFit(match, match == OrderMatch.Exact ? 0 : match == OrderMatch.Close ? 1 : 2, q, stars);
                        Assert.LessOrEqual(BoardBill.Permille(fit), 1000);
                        Assert.LessOrEqual(BoardBill.LineBill(fit, 900, 99999), 900, match + " " + stars + " stars");
                    }
            // Real items through OrderRules.Fit, the share TryPick uses: plated fries of every doneness for a fries guest.
            var kitchen = KitchenState.CreateDemo();
            foreach (int pan in new[] { 1, 2, 3 })
            {
                kitchen.TryCreateIngredient("potato", out Item potato);
                kitchen.Register(10 + pan, potato);
                HeatTestKit.Cut(kitchen, 10 + pan, 3);
                HeatTestKit.PanPreset(kitchen, 10 + pan, pan);
                Assert.AreEqual(CookOutcome.Ok, kitchen.TryPlate(10 + pan, out Item plated));
                OrderFit fit = OrderRules.Fit(plated, Menu.ById(Menu.Fries), 0);
                var pick = new GuestPick(1, Menu.Fries, fit, BoardBill.Permille(fit), 100, 1200);
                long bill = GuestTab.LineBill(pick, BoardBill.Base(400, 9999, fit.Match));
                Assert.LessOrEqual(bill, 400, "pan " + pan + ": " + fit.Match + ", " + fit.Stars + " stars");
                Assert.AreEqual(BoardBill.LineBill(fit, 400, 9999), bill);
            }
        }

        [Test]
        public void Review2b_AGuestWithoutADrink_PaysItsShareOnce()
        {
            // Found re-measuring the corner: a guest without a tab (no drink) handed its line on as the whole pick, so the register applied
            // the share again (fries at 875 permille paid $3.00, not $3.50).
            var tab = new GuestTab();
            var fit = new OrderFit(OrderMatch.Exact, 0, 0.7f, 4);
            var line = new GuestPick(5, Menu.Fries, fit, BoardBill.Permille(fit), 100, 1200);
            long lineBill = GuestTab.LineBill(line, 400);
            Assert.AreEqual(350, lineBill);
            Assert.IsTrue(tab.Serve(line, 1, lineBill, out GuestPick whole, out long bill));
            Assert.AreEqual(OrderRules.ExactPermille, whole.BillPermille, "the share is in the line's bill already");
            RegisterPayment p = new RegisterFlow(new Shift()).HandOver(whole, 1, bill, new GuestOrder(new OrderModifier[0], 60, true, 1000), null, 0f);
            Assert.AreEqual(350, p.Bill, "4 stars: $3.50, once");
        }

        [Test]
        public void Review3_AWrongDish_IsBilledOnWhatWasServed_AShareOfItsOwnValue()
        {
            // Fries (a potato) handed to a falafel wrap guest: a wrong dish. It pays a share of the fries' own sim payout, not of the $9.00 wrap.
            var kitchen = KitchenState.CreateDemo();
            kitchen.TryCreateIngredient("potato", out Item potato);
            kitchen.Register(1, potato);
            HeatTestKit.Cut(kitchen, 1, 3);
            HeatTestKit.PanPreset(kitchen, 1, 2);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPlate(1, out Item plated));
            OrderFit fit = OrderRules.Fit(plated, Menu.ById(Menu.FalafelWrap), 0);
            Assert.AreEqual(OrderMatch.Wrong, fit.Match);
            TownEconomy economy = TownEconomy.CreateDemo();
            Assert.IsTrue(economy.TryOpenAccount("p", 0));
            SaleResult sale = economy.TrySellForGuest("p", plated);
            Assert.IsTrue(sale.Ok, sale.Error.ToString());
            long payout = sale.Payout;
            Assert.Less(payout, 900, "the fries are worth less than the wrap");
            Assert.AreEqual(payout, BoardBill.Base(900, payout, fit.Match), "the served item's own value");
            long bill = BoardBill.LineBill(fit, 900, payout);
            Assert.LessOrEqual(bill, payout * OrderRules.WrongPermille / 1000, "only a share of the fries");
            Assert.AreEqual(900, BoardBill.Base(900, 99999, OrderMatch.Wrong), "never above the board's price");
            Assert.AreEqual(900, BoardBill.Base(900, 10, OrderMatch.Close), "a close dish is the ordered one");
        }

        [Test]
        public void Review4_QualityScalesTheBill_FiveStarsPayTheFullPrice_NothingPaysMore()
        {
            CollectionAssert.AreEqual(new[] { 500, 625, 750, 875, 1000 }, Enumerable.Range(1, 5).Select(BoardBill.QualityPermille));
            Assert.AreEqual(500, BoardBill.QualityPermille(0));
            Assert.AreEqual(1000, BoardBill.QualityPermille(9));
            Assert.AreEqual(400, BoardBill.LineBill(new OrderFit(OrderMatch.Exact, 0, 0.9f, 5), 400, 0), "5 stars: $4.00");
            Assert.AreEqual(350, BoardBill.LineBill(new OrderFit(OrderMatch.Exact, 0, 0.7f, 4), 400, 0), "4 stars: $3.50");
            Assert.AreEqual(200, BoardBill.LineBill(new OrderFit(OrderMatch.Exact, 0, 0f, 1), 400, 0), "burnt: half");
            Assert.AreEqual(210, BoardBill.LineBill(new OrderFit(OrderMatch.Close, 1, 0.7f, 4), 400, 0), "close, 4 stars: 525 permille, $2.10");
            Assert.AreEqual(OrderRules.ClosePermille * 875 / 1000, BoardBill.Permille(new OrderFit(OrderMatch.Close, 1, 0.7f, 4)));
        }

        [Test]
        public void Review5_UnknownMenuLines_SurviveASave()
        {
            ShopSave loaded = ShopSaveCodecTests.Full();
            loaded.menu = new[] { new ShopSave.MenuPart { id = "fries", cents = 450 }, new ShopSave.MenuPart { id = "space_soup", cents = 700 } };
            List<ShopSave.MenuPart> unknown = SaveService.UnknownMenu(loaded);
            Assert.AreEqual(1, unknown.Count);
            var desk = new MenuBoardDesk(Weights);
            desk.Restore(SaveService.MenuOf(loaded));
            ShopSave.MenuPart[] next = SaveService.MenuParts(desk.Capture(), unknown);
            CollectionAssert.AreEquivalent(new[] { "fries", "space_soup" }, next.Select(m => m.id));
            Assert.AreEqual(700, next.Single(m => m.id == "space_soup").cents, "as it came");
            Assert.AreEqual(1, SaveService.MenuParts(desk.Capture(), new[] { new ShopSave.MenuPart { id = "fries", cents = 999 } }).Length,
                "a known line is never doubled");
        }

        [Test]
        public void AVersion1Save_LoadsWithItsMoneyInCents_AndKeepsItsProgressToTheGoal()
        {
            const string v1 = "{\"format\":\"worktown-shop-save\",\"formatVersion\":1,\"saveId\":\"0123456789abcdef0123456789abcdef\","
                + "\"createdUtc\":\"2026-11-11T10:00:00.0000000Z\",\"savedUtc\":\"2026-11-11T12:30:00.0000000Z\",\"shopName\":\"Your food corner\","
                + "\"climb\":{\"stage\":1,\"unlocks\":[\"dish_fries\"],\"goalReached\":false},"
                + "\"money\":{\"balance\":17250,\"earnedTotal\":23000,\"spentTotal\":8625},\"clock\":{\"day\":3,\"servicesPlayed\":4,\"playSeconds\":1700},"
                + "\"stock\":[{\"slot\":\"shelf:potato\",\"count\":5}],\"hygienePercent\":90,"
                + "\"stats\":{\"guestsServed\":17,\"guestsMissed\":1,\"starSum\":70,\"bestServiceEarned\":2876}}";
            SaveLoadResult r = ShopSaveCodec.Default.TryParse(v1);
            Assert.IsTrue(r.Ok, r.Error.ToString());
            Assert.AreEqual(4, r.Save.formatVersion); // spec 062a: then v2 to v3 (the layout); spec 057: v3 to v4
            Assert.AreEqual(15000, r.Save.money.balance, "$17,250 of the old scale is $150.00: half the $300.00 goal, as before ($34,500)");
            Assert.AreEqual(20000, r.Save.money.earnedTotal);
            Assert.AreEqual(7500, r.Save.money.spentTotal);
            Assert.AreEqual(2500, r.Save.stats.bestServiceEarned, "2876 x 100 / 115 = 2500.8, rounded down");
            Assert.IsEmpty(r.Save.menu, "a v1 save has no prices: the board starts at its references");
            Assert.AreEqual(3, r.Save.clock.day, "nothing else changes");
            Assert.AreEqual(5, r.Save.stock[0].count);
            long goal = FoodCornerRules.RentGoal(EconomyCatalog.CreateDefault(), ClimbCatalog.CreateDefault());
            Assert.AreEqual(FoodCornerRules.GoalPercent(17250, 34500), FoodCornerRules.GoalPercent(r.Save.money.balance, goal));
            Assert.AreEqual(long.MaxValue / 115 * 100 + long.MaxValue % 115 * 100 / 115, SaveMigrations.ToCents(long.MaxValue), "no overflow");
            Assert.AreEqual(0, SaveMigrations.ToCents(0));
            Assert.AreEqual(-5, SaveMigrations.ToCents(-5), "left for the clamp");
        }

        [Test]
        public void TheBoardsPrices_AreSaved_AndALoadPutsThemBackOnTheGrid()
        {
            var desk = new MenuBoardDesk(Weights);
            desk.Use(MenuBoardRules.DearerOption, ShopStageRules.FoodCorner);
            ShopSave save = ShopSaveCodecTests.Full();
            save.menu = SaveService.MenuParts(desk.Capture());
            Assert.AreEqual(1, save.menu.Length, "only what differs from its reference");
            Assert.AreEqual(("fries", 450L), (save.menu[0].id, save.menu[0].cents));
            SaveLoadResult r = ShopSaveCodec.Default.TryParse(ShopSaveCodec.Default.ToJson(save));
            Assert.IsTrue(r.Ok, r.Error.ToString());
            var loaded = new MenuBoardDesk(Weights);
            loaded.Restore(SaveService.MenuOf(r.Save));
            Assert.AreEqual(450, loaded.PriceOf(Fries));

            save.menu = new[] { new ShopSave.MenuPart { id = "fries", cents = 9999 }, new ShopSave.MenuPart { id = "space_soup", cents = 1 } };
            r = ShopSaveCodec.Default.TryParse(ShopSaveCodec.Default.ToJson(save));
            Assert.IsTrue(r.Ok);
            Assert.AreEqual(800, r.Save.menu[0].cents, "clamped to the board's bounds");
            Assert.IsTrue(r.Notes.Any(n => n.StartsWith("menu fries")));
            Assert.AreEqual("space_soup", r.Save.menu[1].id, "an unknown line is kept for the next save");
            loaded.Restore(SaveService.MenuOf(r.Save));
            Assert.AreEqual(800, loaded.PriceOf(Fries));
        }

        [Test]
        public void TheWire_GrowsByTheBoardsTwoSyncedFields_AndTheTrayIsCents()
        {
            List<string> lines = ProtocolSurface.Lines(typeof(NetBootstrap).Assembly);
            CollectionAssert.AreEquivalent(new[]
            {
                "sync Worktown.Game.Shop.MenuBoard.Prices:FishNet.Object.Synchronizing.SyncList`1<System.Int32>",
                "sync Worktown.Game.Shop.MenuBoard.Selected:FishNet.Object.Synchronizing.SyncVar`1<System.Byte>",
            }, lines.Where(l => l.Contains("Worktown.Game.Shop.MenuBoard.")).ToList(), "no RPC: two synced fields");
            Assert.Contains("sync Worktown.Game.Shop.Register.Tray:FishNet.Object.Synchronizing.SyncVar`1<System.UInt16>", lines);
            Assert.Contains("revision 7", lines); // spec 062a, spec 054b, then spec 057: Revision + 1 each
            Assert.AreEqual(3, lines.Count(l => l.StartsWith("broadcast ", StringComparison.Ordinal)), "still the three connection messages");
        }

        [Test]
        public void BuiltScene_HasTheMenuBoard_AboveTheCounter_WiredToTheEconomy()
        {
            Scene scene = LayoutScene.Open();
            try
            {
                List<Component> boards = VesselTestKit.AllOf(scene, typeof(MenuBoard));
                Assert.AreEqual(1, boards.Count);
                var station = boards[0].GetComponent<Station>();
                Assert.AreEqual(StationKind.MenuBoard, station.Kind);
                Assert.AreEqual(MenuBoardRules.OptionCount, station.SlotCount);
                Assert.IsNotNull(boards[0].GetComponent<MenuBoardView>());
                List<Component> economies = VesselTestKit.AllOf(scene, typeof(EconomyService));
                Assert.AreEqual(1, economies.Count);
                // Spec 062a: the economy and the kitchen find the board in the LayoutRegistry (it is a spawned piece, no scene reference).
                Assert.AreSame(station, Worktown.Game.Layout.LayoutRegistry.First(StationKind.MenuBoard), "the economy's menu board");
                Assert.AreSame(station, Worktown.Game.Layout.LayoutRegistry.Station(station.Id), "the board is station " + station.Id);
                Station serving = VesselTestKit.AllOf(scene, typeof(Station)).Cast<Station>().Single(s => s.Kind == StationKind.Counter);
                for (int option = 0; option < MenuBoardRules.OptionCount; option++)
                {
                    Assert.IsTrue(station.TryGetSlot(option, out StockSlot slot), "button " + option);
                    // A cook in the aisle in front of the serving counter reaches every button.
                    Vector3 cook = new Vector3(slot.Anchor.x, 0f, serving.transform.position.z + 1.1f);
                    Assert.IsTrue(StationRules.IsInReach(cook, slot.Anchor), "button " + option + " in reach from the aisle");
                    Assert.IsTrue(StationRules.IsInReach(cook, station.transform.position), "the station in reach (the server's check)");
                }
                Assert.IsTrue(AimHitSort.SlotFollowsTheRay(StationKind.MenuBoard), "a button only near the ray");
            }
            finally
            {
                LayoutScene.Close(scene);
            }
        }

        private sealed class Wallet : IRegisterWallet
        {
            public long Paid;
            public int Calls;

            public CookOutcome Pay(int payee, long amount, out long paid)
            {
                Calls++;
                Paid += amount;
                paid = amount;
                return CookOutcome.Ok;
            }
        }
    }
}
