using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Game.Net.Rules;
using Worktown.Game.Npc;
using Worktown.Game.Player;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 031 AC3: the assembly board's server rules through KitchenState (layers in order, the dispenser amount measured by the
    /// server between the press and the release, packaging, wrong steps refused without a change), plus the pure rules the
    /// clients use (the board's bytes, the actions and prompts, the held squeeze, the step prediction), the four dishes and
    /// their order matching, and the looks of the stack.
    /// </summary>
    public class AssemblyStationTests
    {
        private const int Board = 8;
        private const int Spot = 90;
        private const int Fryer = 6;
        private const int Basket = 70;
        private const int Owner = 3;
        private const uint Start = HeatTestKit.StartTick;
        private const double Rate = HeatTestKit.TickRate;

        private static uint Real(double seconds) => Start + (uint)System.Math.Round(seconds * Rate);

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

        /// <summary>Falafel balls from the fryer (25 s, drained) under <paramref name="id"/>.</summary>
        private static void Falafel(KitchenState kitchen, int id)
        {
            Ingredient(kitchen, "falafel_mix", id);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryToggleFryer(Fryer, Start));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(id, Basket, VesselKind.Basket, Start));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryLowerBasket(Basket, Start));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryRaiseBasket(Basket, Real(25), Rate));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewTakeOut(Basket, VesselKind.Basket, Real(28), Rate, out Item balls));
            kitchen.ClearContents(Basket, VesselKind.Basket);
            kitchen.TryToggleFryer(Fryer, Real(28));
            kitchen.Register(id, balls);
        }

        private static void Put(KitchenState kitchen, int id) => Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(id, Spot, VesselKind.Assembly, Start));

        private static void Squeeze(KitchenState kitchen, double fromSeconds, double seconds, int dispenser = 0)
        {
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryStartSqueeze(Board, dispenser, Owner, Real(fromSeconds)));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryEndSqueeze(Board, dispenser, Owner, Real(fromSeconds + seconds), Rate, out Item build,
                out float total));
            kitchen.CommitBuild(Spot, build, BuildRules.SauceOf(dispenser), total);
        }

        /// <summary>Wrap, falafel, lettuce slices, then a 1 s squeeze of garlic-herb sauce: the open falafel wrap.</summary>
        private static KitchenState FalafelWrap()
        {
            KitchenState kitchen = Kitchen();
            Ingredient(kitchen, "wrap", 40);
            Put(kitchen, 40);
            Falafel(kitchen, 41);
            Put(kitchen, 41);
            Ingredient(kitchen, "lettuce", 42, 2);
            Put(kitchen, 42);
            Squeeze(kitchen, 0, 1.0);
            return kitchen;
        }

        // ---- AC3: layer rules ----

        [Test]
        public void Layers_GoOnInTheOrderTheyArrive_AndTheLabelIsTheTakeName()
        {
            KitchenState kitchen = FalafelWrap();
            Item build = kitchen.BuildOn(Spot);
            IReadOnlyList<Item> layers = Assembly.LayersOf(build);
            Assert.AreEqual(4, layers.Count);
            Assert.AreEqual("wrap", Assembly.BaseOf(layers[0]));
            Assert.AreEqual("falafel_mix", Assembly.BaseOf(layers[1]));
            Assert.AreEqual("lettuce", Assembly.BaseOf(layers[2]));
            Assert.AreEqual("garlic_herb_sauce", Assembly.BaseOf(layers[3]));
            Assert.AreEqual(4, kitchen.ContentCount(Spot, VesselKind.Assembly));
            Assert.AreEqual("falafel wrap", kitchen.ResultName(Spot, VesselKind.Assembly, Start, Rate));
            CollectionAssert.AreEqual(new[] { "falafel wrap" }, kitchen.ContentNames(Spot, VesselKind.Assembly, Start, Rate));
            IReadOnlyList<string> names = kitchen.ContentNamesAndTitle(Spot, VesselKind.Assembly, Start, Rate, out string title);
            Assert.AreEqual("falafel wrap", title);
            CollectionAssert.AreEqual(new[] { "falafel wrap" }, names);
            IReadOnlyList<FoodLook> looks = kitchen.ContentLooks(Spot, VesselKind.Assembly, Start, Rate);
            Assert.AreEqual(4, looks.Count, "one look per layer, in order: the stack");
            Assert.AreEqual(FoodLookRules.IngredientIndex("wrap"), looks[0].Ingredient);
            Assert.AreEqual(FoodLook.HeatDeepFried, looks[1].Heat);
            Assert.AreEqual(FoodLook.ShapeSlices, looks[2].Shape);
            Assert.IsTrue(VesselRules.IsFixed(VesselKind.Assembly));
            Assert.IsFalse(VesselRules.IsHeat(VesselKind.Assembly));
            Assert.IsFalse(VesselRules.RespawnsAtHome(VesselKind.Assembly));
        }

        [Test]
        public void AClosedBuild_TakesNoMoreLayers_AFullOneNeither_AndFinishedFoodNever()
        {
            KitchenState kitchen = FalafelWrap();
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryFold(Spot));
            Ingredient(kitchen, "cucumber", 43, 2);
            string before = kitchen.BuildOn(Spot).Hash;
            Assert.AreEqual(CookOutcome.BuildClosed, kitchen.TryPutInto(43, Spot, VesselKind.Assembly, Start));
            Assert.IsTrue(kitchen.TryGet(43, out _), "refused: still in the hands");
            Assert.AreEqual(before, kitchen.BuildOn(Spot).Hash, "nothing changed");
            Assert.AreEqual(CookOutcome.BuildClosed, kitchen.TryStartSqueeze(Board, 0, Owner, Start), "no sauce on a folded wrap");

            KitchenState full = Kitchen();
            for (int i = 0; i < CookingLimits.MaxBuildLayers; i++)
            {
                Ingredient(full, "lettuce", 100 + i, 2);
                Put(full, 100 + i);
            }
            Ingredient(full, "lettuce", 200, 2);
            Assert.AreEqual(CookOutcome.VesselFull, full.TryPutInto(200, Spot, VesselKind.Assembly, Start));

            KitchenState plated = Kitchen();
            Ingredient(plated, "wrap", 40);
            Put(plated, 40);
            Ingredient(plated, "lettuce", 44);
            Assert.AreEqual(CookOutcome.Ok, plated.TryPlate(44, out Item plate));
            plated.Register(45, plate);
            Assert.AreEqual(CookOutcome.AlreadyPlated, plated.TryPutInto(45, Spot, VesselKind.Assembly, Start));
            Assert.AreEqual(1, plated.LayerCount(Spot));
        }

        [Test]
        public void ABuildTakenOff_GoesBackOnAnEmptyBoardAsItWas()
        {
            KitchenState kitchen = FalafelWrap();
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewTakeOut(Spot, VesselKind.Assembly, Start, Rate, out Item taken));
            kitchen.ClearContents(Spot, VesselKind.Assembly);
            Assert.AreEqual(0, kitchen.LayerCount(Spot));
            Assert.AreEqual(CookOutcome.StationEmpty, kitchen.TryPreviewTakeOut(Spot, VesselKind.Assembly, Start, Rate, out _));
            kitchen.Register(50, taken);
            Put(kitchen, 50);
            Assert.AreEqual(taken.Hash, kitchen.BuildOn(Spot).Hash);
            Ingredient(kitchen, "wrap", 51);
            kitchen.Register(52, taken);
            Assert.AreEqual(CookOutcome.VesselFull, kitchen.TryPutInto(52, Spot, VesselKind.Assembly, Start), "a build only on an empty board");
        }

        // ---- AC3: the dispenser amount (server-measured) ----

        [Test]
        public void TheSauceAmount_IsTheTimeBetweenThePressAndTheRelease_OnTheServersTicks()
        {
            string Amount(double seconds)
            {
                KitchenState kitchen = Kitchen();
                Ingredient(kitchen, "wrap", 40);
                Put(kitchen, 40);
                Squeeze(kitchen, 2, seconds);
                return Assembly.SauceAmountOf(Assembly.LayersOf(kitchen.BuildOn(Spot))[1]);
            }
            Assert.AreEqual(Assembly.Little, Amount(0.25));
            Assert.AreEqual(Assembly.Right, Amount(0.6));
            Assert.AreEqual(Assembly.Right, Amount(1.4));
            Assert.AreEqual(Assembly.Lots, Amount(2.0));
            Assert.AreEqual(1f, BuildRules.SqueezeSeconds(Start, Real(1), Rate), 1e-4f);
            Assert.AreEqual(0f, BuildRules.SqueezeSeconds(Start, Start, 0d));
        }

        [Test]
        public void TwoSqueezesOfTheSameSauce_AreOneLayer_TheAmountsAdd()
        {
            KitchenState kitchen = Kitchen();
            Ingredient(kitchen, "wrap", 40);
            Put(kitchen, 40);
            Squeeze(kitchen, 0, 0.3);
            Assert.AreEqual(Assembly.Little, Assembly.SauceAmountOf(Assembly.LayersOf(kitchen.BuildOn(Spot))[1]));
            Squeeze(kitchen, 1, 0.4);
            IReadOnlyList<Item> layers = Assembly.LayersOf(kitchen.BuildOn(Spot));
            Assert.AreEqual(2, layers.Count, "one sauce layer");
            Assert.AreEqual(Assembly.Right, Assembly.SauceAmountOf(layers[1]), "0.3 + 0.4 s");
            Squeeze(kitchen, 2, 0.6, 1);
            Assert.AreEqual(3, kitchen.LayerCount(Spot), "another sauce is another layer");
        }

        [Test]
        public void TheDispenser_RefusesWithoutABuild_SomeoneElsesSqueeze_AndAReleaseWithoutAPress()
        {
            KitchenState kitchen = Kitchen();
            Assert.AreEqual(CookOutcome.StationEmpty, kitchen.TryStartSqueeze(Board, 0, Owner, Start));
            Ingredient(kitchen, "wrap", 40);
            Put(kitchen, 40);
            Assert.AreEqual(CookOutcome.InvalidOption, kitchen.TryEndSqueeze(Board, 0, Owner, Start, Rate, out _, out _), "nothing to stop");
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryStartSqueeze(Board, 0, Owner, Start));
            Assert.AreEqual(CookOutcome.DispenserBusy, kitchen.TryStartSqueeze(Board, 0, Owner + 1, Start));
            Assert.AreEqual(CookOutcome.InvalidOption, kitchen.TryEndSqueeze(Board, 0, Owner + 1, Real(1), Rate, out _, out _));
            Assert.IsTrue(BuildRules.IsSqueezing(kitchen.AssemblyFlags(Board), 0), "the board's byte shows it to everyone");
            Assert.IsFalse(BuildRules.IsSqueezing(kitchen.AssemblyFlags(Board), 1));
            Assert.AreEqual(CookOutcome.InvalidOption, kitchen.TryStartSqueeze(Board, 2, Owner, Start), "no third dispenser");
            Assert.AreEqual(1, kitchen.LayerCount(Spot), "no sauce until the release is committed");
        }

        [Test]
        public void AHeldSqueeze_IsStoppedByTheServer_AfterTheAutoStop()
        {
            KitchenState kitchen = Kitchen();
            Ingredient(kitchen, "wrap", 40);
            Put(kitchen, 40);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryStartSqueeze(Board, 1, Owner, Start));
            Assert.IsFalse(kitchen.TryFindOverdueSqueeze(Real(2.9), Rate, out _, out _, out _));
            Assert.IsTrue(kitchen.TryFindOverdueSqueeze(Real(3.0), Rate, out int board, out int d, out int owner));
            Assert.AreEqual((Board, 1, Owner), (board, d, owner));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryEndSqueeze(Board, 1, Owner, Real(9), Rate, out Item build, out float seconds));
            Assert.AreEqual(BuildRules.AutoStopSeconds, seconds, 1e-4f, "capped at the auto-stop");
            Assert.AreEqual(Assembly.Lots, Assembly.SauceAmountOf(Assembly.LayersOf(build)[1]));
            Assert.IsFalse(kitchen.TryFindOverdueSqueeze(Real(9), Rate, out _, out _, out _));
            kitchen.CancelSqueeze(Board, 1);
            Assert.AreEqual(0, kitchen.AssemblyFlags(Board));
        }

        // ---- AC3: packaging and wrong steps ----

        [Test]
        public void Packaging_PaperOnAFoldedWrap_ABoxWithoutBread_ABagOnAFoldedFlatbread()
        {
            KitchenState kitchen = FalafelWrap();
            Assert.AreEqual(CookOutcome.FoldFirst, kitchen.TryPreviewPackaging(Spot, BuildRules.Paper, out Item none));
            Assert.IsNull(none);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryFold(Spot));
            Assert.AreEqual(BuildRules.CloseFolded, BuildRules.CloseOf(kitchen.BuildMode(Spot)));
            Assert.AreEqual(CookOutcome.BuildClosed, kitchen.TryFold(Spot), "folded once");
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewPackaging(Spot, BuildRules.Paper, out Item wrapped));
            Assert.AreEqual(BuildRules.CloseFolded, BuildRules.CloseOf(kitchen.BuildMode(Spot)), "a preview changes nothing");
            kitchen.CommitBuild(Spot, wrapped);
            Assert.IsTrue(BuildRules.IsFinished(kitchen.BuildMode(Spot)));
            Assert.AreEqual("falafel wrap in paper", kitchen.ResultName(Spot, VesselKind.Assembly, Start, Rate));
            Assert.AreEqual(CookOutcome.BuildClosed, kitchen.TryPreviewPackaging(Spot, BuildRules.Bag, out _));

            KitchenState box = Kitchen();
            Ingredient(box, "potato", 40, 3);
            Put(box, 40);
            Assert.AreEqual(CookOutcome.NothingToFold, box.TryFold(Spot));
            Assert.AreEqual(CookOutcome.NothingToFold, box.TryPreviewPackaging(Spot, BuildRules.Paper, out _));
            Assert.AreEqual(CookOutcome.Ok, box.TryPreviewPackaging(Spot, BuildRules.Box, out Item boxed));
            Assert.AreEqual(Assembly.Box, Assembly.PackOf(boxed));

            KitchenState bag = Kitchen();
            Ingredient(bag, "flatbread", 40);
            Put(bag, 40);
            Ingredient(bag, "red_cabbage", 41, 2);
            Put(bag, 41);
            Assert.AreEqual(CookOutcome.FoldFirst, bag.TryPreviewPackaging(Spot, BuildRules.Bag, out _));
            Assert.AreEqual(CookOutcome.Ok, bag.TryFold(Spot));
            Assert.AreEqual(CookOutcome.Ok, bag.TryPreviewPackaging(Spot, BuildRules.Bag, out Item bagged));
            Assert.AreEqual(Assembly.Bag, Assembly.PackOf(bagged));

            KitchenState empty = Kitchen();
            Assert.AreEqual(CookOutcome.StationEmpty, empty.TryFold(Spot));
            Assert.AreEqual(CookOutcome.StationEmpty, empty.TryPreviewPackaging(Spot, BuildRules.Box, out _));
            Assert.AreEqual(CookOutcome.InvalidOption, empty.TryPreviewPackaging(Spot, 7, out _));
        }

        [Test]
        public void PackagingStock_HasAPrice_AndEveryToolIsAStationOption()
        {
            EconomyCatalog prices = EconomyCatalog.CreateDefault();
            for (int p = 0; p < BuildRules.PackagingCount; p++)
            {
                Assert.IsTrue(prices.TryGetGrocerPrice(BuildRules.PackagingId(p), out long price), BuildRules.PackagingName(p));
                Assert.Greater(price, 0);
                Assert.IsTrue(BuildRules.TryParsePackaging(BuildRules.PackagingOption(p), out int back));
                Assert.AreEqual(p, back);
                Assert.IsFalse(BuildRules.TryParseSqueeze(BuildRules.PackagingOption(p), out _, out _));
            }
            for (int d = 0; d < BuildRules.DispenserCount; d++)
            {
                Assert.IsTrue(prices.TryGetGrocerPrice(BuildRules.SauceOf(d), out _), "a squeeze costs its sauce");
                Assert.IsTrue(BuildRules.TryParseSqueeze(BuildRules.SqueezeStartOption(d), out int a, out bool start));
                Assert.AreEqual((d, true), (a, start));
                Assert.IsTrue(BuildRules.TryParseSqueeze(BuildRules.SqueezeStopOption(d), out int b, out bool stop));
                Assert.AreEqual((d, false), (b, stop));
                Assert.IsFalse(BuildRules.TryParsePackaging(BuildRules.SqueezeStartOption(d), out _));
            }
            Assert.IsFalse(BuildRules.TryParseSqueeze(0, out _, out _));
            Assert.AreEqual(BuildRules.ToolCount, BuildRules.DispenserCount + BuildRules.PackagingCount);
            Assert.AreEqual(-1, BuildRules.PackagingOf(0));
            Assert.AreEqual(BuildRules.Paper, BuildRules.PackagingOf(2));
            Assert.IsNull(BuildRules.PackagingId(9));
            Assert.AreEqual(5, VesselRules.ActionFold, "a new RequestUse action number, no new RPC");
        }

        [Test]
        public void ARefusedStep_ChangesNothing()
        {
            KitchenState kitchen = FalafelWrap();
            Item before = kitchen.BuildOn(Spot);
            Assert.AreEqual(CookOutcome.FoldFirst, kitchen.TryPreviewPackaging(Spot, BuildRules.Paper, out _));
            Assert.AreEqual(CookOutcome.FoldFirst, kitchen.TryPreviewPackaging(Spot, BuildRules.Bag, out _));
            Assert.AreEqual(CookOutcome.InvalidOption, kitchen.TryEndSqueeze(Board, 0, Owner, Real(5), Rate, out _, out _));
            Assert.AreSame(before, kitchen.BuildOn(Spot));
            Assert.AreEqual(BuildRules.Mode(BuildRules.CloseOpen, BuildRules.PackNone, true), kitchen.BuildMode(Spot));
        }

        // ---- the dishes ----

        [Test]
        public void TheFalafelWrap_InPaper_IsAnExactOrder_SoldAsItIs_AndFoldedOnlyIsClose()
        {
            KitchenState kitchen = FalafelWrap();
            MenuDish dish = Menu.ById(Menu.FalafelWrap);
            Assert.AreEqual("Falafel wrap", dish.Name);
            Assert.AreEqual(OrderMatch.Close, OrderRules.Match(kitchen.BuildOn(Spot), dish), "open: the close is short");
            kitchen.TryFold(Spot);
            Assert.AreEqual(OrderMatch.Close, OrderRules.Match(kitchen.BuildOn(Spot), dish), "folded, no paper: one step short");
            kitchen.TryPreviewPackaging(Spot, BuildRules.Paper, out Item wrapped);
            kitchen.CommitBuild(Spot, wrapped);
            Assert.AreEqual(OrderMatch.Exact, OrderRules.Match(wrapped, dish));
            Assert.AreEqual(OrderMatch.Wrong, OrderRules.Match(wrapped, Menu.ById(Menu.SeitanWrap)), "no seitan");
            Assert.AreEqual(OrderMatch.Wrong, OrderRules.Match(wrapped, Menu.ById(Menu.Fries)));
            Assert.IsTrue(AssemblyRules.IsPlated(wrapped), "a wrap in paper is finished");
            kitchen.Register(60, wrapped);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPlate(60, out Item sold));
            Assert.AreSame(wrapped, sold, "sold as it is");
            TownEconomy economy = TownEconomy.CreateDemo();
            Assert.IsTrue(economy.TryOpenAccount("p0", 500));
            SaleResult sale = economy.TrySell("p0", wrapped);
            Assert.IsTrue(sale.Ok, "falafel wrap: " + sale.Error);
            Assert.AreEqual(dish.Market, sale.MarketId);
            Assert.GreaterOrEqual(StarRating.Stars(KitchenState.QualityOf(wrapped)), 3, "q=" + KitchenState.QualityOf(wrapped));
            TestContext.WriteLine("falafel wrap q=" + KitchenState.QualityOf(wrapped).ToString("0.000"));
        }

        [Test]
        public void TheMenu_HasTheFourBuilds_WithTemplates_AndTheyCanBeMade()
        {
            foreach (byte id in new[] { Menu.SeitanWrap, Menu.FalafelWrap, Menu.SeitanFlatbread, Menu.VeggieBox })
            {
                MenuDish dish = Menu.ById(id);
                Assert.IsNotNull(dish);
                Assert.IsNotNull(RecipeTemplates.ById(dish.Template), dish.Name);
                Assert.AreEqual("meal", dish.Market);
                Assert.IsTrue(Menu.CanMake(dish, Menu.CrateIngredients), dish.Name);
            }
            Assert.IsNull(Menu.ById(Menu.Fries).Template);
            Assert.AreEqual(OrderMatch.Wrong, OrderRules.MatchBuild(null, Menu.ById(Menu.VeggieBox)));
        }

        // ---- the clients' rules ----

        [Test]
        public void TheBuildSpot_TakesFood_FoldsByHand_AndTakesTheFinishedBuild()
        {
            byte open = BuildRules.Mode(BuildRules.CloseOpen, BuildRules.PackNone, true);
            byte folded = BuildRules.Mode(BuildRules.CloseFolded, BuildRules.PackNone, true);
            byte loose = BuildRules.Mode(BuildRules.CloseOpen, BuildRules.PackNone, false);
            AimTarget Target(int layers, byte mode) => AimTarget.Cookware(VesselKind.Assembly, layers, layers > 0 ? "falafel wrap" : null, mode);
            Held lettuce = Held.Food("lettuce slices", false);
            Assert.AreEqual(InteractionAction.PutInto, InteractionRules.ActionFor(lettuce, Target(0, 0)));
            Assert.AreEqual(InteractionAction.PutInto, InteractionRules.ActionFor(lettuce, Target(3, open)));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(lettuce, Target(3, folded)));
            Assert.AreEqual("It is closed", InteractionRules.PromptFor(lettuce, Target(3, folded)));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(lettuce, Target(CookingLimits.MaxBuildLayers, open)));
            Assert.AreEqual("Click · put lettuce slices on the board", InteractionRules.PromptFor(lettuce, Target(3, open)));
            Assert.AreEqual(InteractionAction.Fold, InteractionRules.ActionFor(Held.Nothing, Target(3, open)));
            Assert.AreEqual("Click · fold falafel wrap", InteractionRules.PromptFor(Held.Nothing, Target(3, open)));
            Assert.AreEqual(InteractionAction.Take, InteractionRules.ActionFor(Held.Nothing, Target(1, open)), "a lone wrap comes back");
            Assert.AreEqual(InteractionAction.Take, InteractionRules.ActionFor(Held.Nothing, Target(3, folded)));
            Assert.AreEqual(InteractionAction.Take, InteractionRules.ActionFor(Held.Nothing, Target(3, loose)), "no bread: nothing to fold");
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Knife, Target(3, open)));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Cookware(VesselKind.Plate, 0, null), Target(3, open)));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Pending(lettuce), Target(3, open)));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Food("falafel wrap in paper", true), Target(3, open)));
        }

        [Test]
        public void TheTools_SqueezeOntoAnOpenBuild_AndPackOnlyWhatFits()
        {
            byte open = BuildRules.Mode(BuildRules.CloseOpen, BuildRules.PackNone, true);
            byte folded = BuildRules.Mode(BuildRules.CloseFolded, BuildRules.PackNone, true);
            AimTarget Tool(int tool, int layers, byte mode) => AimTarget.AssemblyTool(tool, 0, tool == 0 ? "garlic-herb sauce" : "x", 35, layers, mode);
            Assert.AreEqual(InteractionAction.Squeeze, InteractionRules.ActionFor(Held.Nothing, Tool(0, 3, open)));
            Assert.AreEqual("Hold · squeeze garlic-herb sauce", InteractionRules.PromptFor(Held.Nothing, Tool(0, 3, open)));
            Assert.AreEqual(InteractionAction.Squeeze, InteractionRules.ActionFor(Held.Food("lettuce", false), Tool(0, 3, open)), "any hands");
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Nothing, Tool(0, 0, 0)));
            Assert.AreEqual("Start with a wrap or flatbread on the board", InteractionRules.PromptFor(Held.Nothing, Tool(0, 0, 0)));
            Assert.AreEqual("It is closed", InteractionRules.PromptFor(Held.Nothing, Tool(1, 3, folded)));
            int paper = BuildRules.DispenserCount + BuildRules.Paper;
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Nothing, Tool(paper, 3, open)));
            Assert.AreEqual("Fold it first", InteractionRules.PromptFor(Held.Nothing, Tool(paper, 3, open)));
            Assert.AreEqual(InteractionAction.Pack, InteractionRules.ActionFor(Held.Nothing, Tool(paper, 3, folded)));
            Assert.AreEqual("Click · wrap in paper ($35)", InteractionRules.PromptFor(Held.Nothing, Tool(paper, 3, folded)));
            int box = BuildRules.DispenserCount + BuildRules.Box;
            Assert.AreEqual(InteractionAction.Pack, InteractionRules.ActionFor(Held.Nothing,
                Tool(box, 3, BuildRules.Mode(BuildRules.CloseOpen, BuildRules.PackNone, false))));
            Assert.AreEqual(InteractionAction.Pack, InteractionRules.ActionFor(Held.Nothing, Tool(box, 3, folded)));
            Assert.AreEqual("Paper is for a wrap or a flatbread", InteractionRules.PromptFor(Held.Nothing,
                Tool(paper, 3, BuildRules.Mode(BuildRules.CloseOpen, BuildRules.PackNone, false))));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Nothing, Tool(0, 3, open).TooFar()));
            // The board's body (a ray that meets no tool) does nothing and says nothing.
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Nothing, AimTarget.OnStation(StationKind.Assembly)));
            Assert.IsNull(InteractionRules.PromptFor(Held.Nothing, AimTarget.OnStation(StationKind.Assembly)));
        }

        [Test]
        public void TheModeByte_RoundTrips_AndTheBoardByteHasABitPerDispenser()
        {
            for (int close = 0; close <= 2; close++)
                for (int pack = 0; pack <= 2; pack++)
                    foreach (bool bread in new[] { false, true })
                    {
                        byte m = BuildRules.Mode(close, pack, bread);
                        Assert.AreEqual(close, BuildRules.CloseOf(m));
                        Assert.AreEqual(pack, BuildRules.PackOf(m));
                        Assert.AreEqual(bread, BuildRules.OnBread(m));
                    }
            byte flags = BuildRules.WithSqueezing(0, 1, true);
            Assert.IsTrue(BuildRules.IsSqueezing(flags, 1));
            Assert.IsFalse(BuildRules.IsSqueezing(flags, 0));
            Assert.AreEqual(0, BuildRules.WithSqueezing(flags, 1, false));
            Assert.AreEqual(flags, BuildRules.WithSqueezing(flags, 5, true));
            Assert.AreEqual(0, BuildRules.ModeOf(null));
            Assert.IsTrue(BuildRules.FoldsByHand(2, BuildRules.Mode(0, 0, true)));
            Assert.IsFalse(BuildRules.FoldsByHand(1, BuildRules.Mode(0, 0, true)), "a lone wrap has nothing to fold over");
            Assert.AreEqual(FoodLook.KindWrapped, BuildStackView.KindOf(BuildRules.Mode(BuildRules.CloseWrapped, 0, true)));
            Assert.AreEqual(FoodLook.KindBoxed, BuildStackView.KindOf(BuildRules.Mode(0, BuildRules.PackBox, false)));
            Assert.AreEqual(FoodLook.KindBagged, BuildStackView.KindOf(BuildRules.Mode(1, BuildRules.PackBag, true)));
            Assert.AreEqual(FoodLook.KindFolded, BuildStackView.KindOf(BuildRules.Mode(1, 0, true)));
            Assert.AreEqual(FoodLook.KindBuild, BuildStackView.KindOf(0));
        }

        [Test]
        public void TheHeldSqueeze_SendsAStartAndAStop_InOrder_NeverSwallowed()
        {
            var hold = new SqueezeHold();
            Assert.IsTrue(hold.Idle);
            Assert.IsTrue(hold.Press(Board, 0));
            Assert.AreEqual(SqueezeHold.Send.None, hold.Poll(false), "inside the request interval it waits");
            Assert.AreEqual(SqueezeHold.Send.Start, hold.Poll(true));
            Assert.AreEqual(SqueezeHold.Send.None, hold.Poll(true), "held: nothing more");
            hold.Release();
            Assert.AreEqual(SqueezeHold.Send.None, hold.Poll(false));
            Assert.AreEqual(SqueezeHold.Send.Stop, hold.Poll(true));
            Assert.IsTrue(hold.Idle);
            // A quick tap released before the start could go out still sends both, in order.
            hold.Press(Board, 1);
            hold.Release();
            Assert.AreEqual(SqueezeHold.Send.Start, hold.Poll(true));
            Assert.AreEqual(SqueezeHold.Send.Stop, hold.Poll(true));
            // Another dispenser waits until the first one's stop went out (031b: queued behind it, not dropped).
            hold.Press(Board, 0);
            hold.Poll(true);
            hold.Release();
            Assert.IsTrue(hold.Press(Board, 1));
            Assert.AreEqual(SqueezeHold.Send.Stop, hold.Poll(true));
            Assert.AreEqual(0, hold.SendDispenser, "the first dispenser's stop goes first");
            Assert.AreEqual(SqueezeHold.Send.Start, hold.Poll(true));
            Assert.AreEqual(1, hold.SendDispenser, "then the queued press");
            hold.Reset();
            Assert.IsTrue(hold.Idle);
        }

        [Test]
        public void TheStepPrediction_IsConfirmedByTheModeBits_AndDeniedByAnEmptyBoard()
        {
            var start = new ItemSnapshot(true, -1, Vector3.zero, BuildRules.Mode(0, 0, true) & 0x0F);
            var folded = new ItemSnapshot(true, -1, Vector3.zero, BuildRules.Mode(BuildRules.CloseFolded, 0, true) & 0x0F);
            Assert.AreEqual(PredictionPhase.Pending, FeelRules.Judge(PredictionKind.BuildStep, Owner, start, start));
            Assert.AreEqual(PredictionPhase.Confirmed, FeelRules.Judge(PredictionKind.BuildStep, Owner, start, folded));
            Assert.AreEqual(PredictionPhase.Denied, FeelRules.Judge(PredictionKind.BuildStep, Owner, start, ItemSnapshot.Gone));
            Assert.AreEqual(AnswerKind.Cook, PredictionBook<object>.AnswerFor(PredictionKind.BuildStep));
        }

        // ---- the looks ----

        [Test]
        public void BuildLooks_TellTheKind_AndAFinishedBuildGoesOnNothing()
        {
            KitchenState kitchen = FalafelWrap();
            FoodLook open = FoodLookRules.From(kitchen.BuildOn(Spot));
            Assert.AreEqual(FoodLook.KindBuild, open.Kind);
            Assert.AreEqual(FoodLookRules.IngredientIndex("wrap"), open.Ingredient);
            Assert.AreEqual(4, open.PartCount);
            Assert.AreEqual(4, FoodLookRules.PartsOf(kitchen.BuildOn(Spot)).Count);
            Assert.IsTrue(FoodLookRules.IsBuild(open));
            Assert.IsFalse(FoodLookRules.IsFinished(open));
            kitchen.TryFold(Spot);
            Assert.AreEqual(FoodLook.KindFolded, FoodLookRules.From(kitchen.BuildOn(Spot)).Kind);
            kitchen.TryPreviewPackaging(Spot, BuildRules.Paper, out Item wrapped);
            FoodLook paper = FoodLookRules.From(wrapped);
            Assert.AreEqual(FoodLook.KindWrapped, paper.Kind);
            Assert.IsTrue(FoodLookRules.IsFinished(paper));
            Assert.IsFalse(FoodLookRules.IsBuild(new FoodLook(0, 0, 0, 0, FoodLook.KindPlated, 1)));
        }

        [Test]
        public void TheStack_LayersStandOnEachOther_AndANewOneDropsOnAnArcWithAPlop()
        {
            var wrap = new FoodLook(FoodLookRules.IngredientIndex("wrap"), FoodLook.ShapeWhole, 0, 0, 0, 0);
            var sauce = new FoodLook(FoodLookRules.IngredientIndex("garlic_herb_sauce"), FoodLook.ShapeWhole, 0, 0, 0, 0);
            var falafel = new FoodLook(FoodLookRules.IngredientIndex("falafel_mix"), FoodLook.ShapeWhole, 2, FoodLook.HeatDeepFried, 0, 0);
            var lettuce = new FoodLook(FoodLookRules.IngredientIndex("lettuce"), FoodLook.ShapeSlices, 0, 0, 0, 0);
            Assert.IsTrue(AssemblyArtRules.IsFlat(wrap));
            Assert.IsTrue(AssemblyArtRules.IsFlat(sauce));
            Assert.IsFalse(AssemblyArtRules.IsFlat(falafel));
            Assert.AreEqual(AssemblyArtRules.BaseThickness, AssemblyArtRules.LayerHeight(wrap));
            Assert.AreEqual(AssemblyArtRules.SauceThickness, AssemblyArtRules.LayerHeight(sauce));
            Assert.Greater(AssemblyArtRules.LayerHeight(falafel), AssemblyArtRules.LayerHeight(lettuce));
            Vector3 top = AssemblyArtRules.DropOffset(0f);
            Assert.AreEqual(AssemblyArtRules.DropHeight, top.y, 1e-5f, "it starts above the stack");
            Assert.Less(top.z, 0f, "from the cook's side: an arc, not a straight drop");
            Assert.AreEqual(Vector3.zero, AssemblyArtRules.DropOffset(AssemblyArtRules.DropSeconds));
            Assert.Less(AssemblyArtRules.DropOffset(AssemblyArtRules.DropSeconds * 0.5f).y, AssemblyArtRules.DropHeight);
            Assert.AreEqual(AssemblyArtRules.Squash, AssemblyArtRules.SquashAfter(0f));
            Assert.AreEqual(1f, AssemblyArtRules.SquashAfter(AssemblyArtRules.SquashSeconds));
            Assert.AreEqual(0f, AssemblyArtRules.StepShare(0f));
            Assert.AreEqual(1f, AssemblyArtRules.StepShare(AssemblyArtRules.StepSeconds));
            Assert.LessOrEqual(AssemblyArtRules.StepSeconds, 0.35f, "folding and wrapping are short");
            Assert.AreEqual(1f, AssemblyArtRules.BottleShare(AssemblyArtRules.BottleOverSeconds, true));
            Assert.AreEqual(0f, AssemblyArtRules.BottleShare(0f, false));
            Assert.LessOrEqual(AssemblyArtRules.BottleOverSeconds, 0.1f + 0.05f, "the squeeze shows within 100 ms or so (feel.md 1)");
            Vector2 sway = AssemblyArtRules.RibbonLanding(0.3f);
            Assert.LessOrEqual(Mathf.Abs(sway.x), AssemblyArtRules.RibbonSway + 1e-5f);
            Assert.AreNotEqual(Vector2.zero, AssemblyArtRules.ToolPosition(0));
            Assert.AreEqual(Vector2.zero, AssemblyArtRules.ToolPosition(9));
        }

        [Test]
        public void TheNewSounds_ArePlopSqueezeFoldAndPaper_AndTheSqueezeLoops()
        {
            Assert.IsTrue(Worktown.Game.Audio.ProceduralSounds.IsLoop(Worktown.Game.Audio.SoundKey.Squeeze));
            Assert.IsFalse(Worktown.Game.Audio.ProceduralSounds.IsLoop(Worktown.Game.Audio.SoundKey.Plop));
            float[] squeeze = Worktown.Game.Audio.ProceduralSounds.Squeeze();
            Assert.AreEqual(0f, squeeze[0], 0.01f, "the loop starts quiet");
            Assert.AreEqual(0f, squeeze[squeeze.Length - 1], 0.01f, "and ends quiet: no click");
        }
    }
}
