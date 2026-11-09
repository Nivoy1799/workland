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
    /// Spec 030 AC3: the griddle's server rules through KitchenState (the flip needs the spatula and reach, spot capacity, the
    /// zones per side, the take), plus the pure rules clients use (the spot's bytes, prompts, the two-half ring, the flip arc), the
    /// two griddle dishes, and the 029b review additions (the hands rule, the fryer prompt's seen state).
    /// </summary>
    public class GriddleStationTests
    {
        private const int Griddle = 7;
        private const int Spot0 = 80;
        private const int Spot1 = 81;
        private const int Food = 42;
        private const uint Start = HeatTestKit.StartTick;
        private const double Rate = HeatTestKit.TickRate;

        private static uint Real(double seconds) => Start + (uint)System.Math.Round(seconds * Rate);

        private static KitchenState Kitchen()
        {
            var kitchen = KitchenState.CreateDemo();
            kitchen.RegisterGriddleSpot(Spot0, Griddle, 0);
            kitchen.RegisterGriddleSpot(Spot1, Griddle, 1);
            return kitchen;
        }

        /// <summary>A raw ingredient under <paramref name="id"/>, cut with the board option (1 halves, 2 slices, 3 sticks) or not (0).</summary>
        private static void Ingredient(KitchenState kitchen, string baseId, int cut, int id = Food)
        {
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateIngredient(baseId, out Item item));
            kitchen.Register(id, item);
            if (cut > 0) HeatTestKit.Cut(kitchen, id, cut);
        }

        /// <summary>Seitan strips on spot 0, the griddle switched on at <see cref="Start"/>.</summary>
        private static KitchenState Grilling(string baseId = "seitan", int cut = 3)
        {
            KitchenState kitchen = Kitchen();
            Ingredient(kitchen, baseId, cut);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(Food, Spot0, VesselKind.Griddle, Start));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryToggleGriddle(Griddle, Start));
            Assert.IsTrue(kitchen.GriddleIsOn(Griddle));
            return kitchen;
        }

        // ---- AC3: the flip needs the spatula and reach ----

        [Test]
        public void Flip_NeedsTheSpatula_AndIsOnlyForAGriddleSpot()
        {
            Assert.AreEqual(CookOutcome.Ok, VesselRules.CheckFlip(VesselKind.Griddle, true));
            Assert.AreEqual(CookOutcome.NoSpatula, VesselRules.CheckFlip(VesselKind.Griddle, false), "by hand: Use the spatula");
            Assert.AreEqual(CookOutcome.InvalidOption, VesselRules.CheckFlip(VesselKind.Pan, true));
            Assert.AreEqual(CookOutcome.InvalidOption, VesselRules.CheckFlip(VesselKind.Basket, true));
            Assert.AreEqual("Use the spatula", InteractionRules.UseTheSpatula);
            Assert.AreEqual(4, VesselRules.ActionFlip, "a new RequestUse action number, no new RPC");
        }

        [Test]
        public void Flip_NeedsReach_ToTheSpotsPivot_AndAGriddleSpotHasNoPickup()
        {
            Vector3 spot = new Vector3(5.5f, 0.9f, 14.03f);
            Assert.AreEqual(CookOutcome.Ok, VesselRules.CheckUseTarget(VesselKind.Griddle, false, false, spot + Vector3.back * 1.2f, spot));
            Assert.AreEqual(CookOutcome.TooFar, VesselRules.CheckUseTarget(VesselKind.Griddle, false, false, spot + Vector3.back * 2.6f, spot));
            Assert.IsTrue(VesselRules.IsFixed(VesselKind.Griddle));
            Assert.IsFalse(VesselRules.RespawnsAtHome(VesselKind.Griddle));
            Assert.IsTrue(VesselRules.TakesOutByHand(VesselKind.Griddle));
        }

        [Test]
        public void TheFlip_TurnsTheItem_AndOnlyTheDownSideCooks()
        {
            KitchenState kitchen = Grilling();
            GriddleSides s = kitchen.GriddleSidesOf(Spot0, Real(10), Rate);
            Assert.AreEqual(0, s.DownSide);
            Assert.AreEqual(60f, s.SideA, 0.01f, "10 s on side 1");
            Assert.AreEqual(0f, s.SideB);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryFlip(Spot0, Real(10)));
            Assert.AreEqual(1, kitchen.FlipsOf(Spot0));
            s = kitchen.GriddleSidesOf(Spot0, Real(16), Rate);
            Assert.AreEqual(1, s.DownSide);
            Assert.AreEqual(60f, s.SideA, 0.01f, "side 1 stopped at the flip");
            Assert.AreEqual(36f, s.SideB, 0.01f, "side 2 cooks from the flip");
            // Flipping a raw side is allowed; an empty spot has nothing to flip.
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryFlip(Spot0, Real(16)));
            Assert.AreEqual(CookOutcome.StationEmpty, kitchen.TryFlip(Spot1, Real(16)));
            Assert.AreEqual(CookOutcome.NotAVessel, kitchen.TryFlip(999, Real(16)));
        }

        [Test]
        public void SwitchingTheGriddleOff_PausesTheDownSide()
        {
            KitchenState kitchen = Grilling();
            kitchen.TryToggleGriddle(Griddle, Real(5));
            Assert.AreEqual(30f, kitchen.GriddleSidesOf(Spot0, Real(20), Rate).SideA, 0.01f);
            kitchen.TryFlip(Spot0, Real(20)); // flipped while off: nothing cooks
            Assert.AreEqual(0f, kitchen.GriddleSidesOf(Spot0, Real(25), Rate).SideB);
            kitchen.TryToggleGriddle(Griddle, Real(25));
            Assert.AreEqual(12f, kitchen.GriddleSidesOf(Spot0, Real(27), Rate).SideB, 0.01f);
        }

        // ---- AC3: spot capacity ----

        [Test]
        public void ASpot_HoldsOneItem_PlatedFoodNever_AndFoodOffTheGriddleIsRefusedNowhere()
        {
            KitchenState kitchen = Grilling();
            Ingredient(kitchen, "paprika", 3, 43);
            Assert.AreEqual(CookOutcome.VesselFull, kitchen.TryPutInto(43, Spot0, VesselKind.Griddle, Start));
            Assert.IsTrue(kitchen.TryGet(43, out _), "refused: still in the hands");
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(43, Spot1, VesselKind.Griddle, Start), "the next spot is free");
            Assert.AreEqual(1, kitchen.ContentCount(Spot0, VesselKind.Griddle));
            Assert.AreEqual(1, VesselRules.Capacity(VesselKind.Griddle));
            Assert.AreEqual(6, GriddleRules.SpotsPerGriddle);
        }

        // ---- AC3: zones per side ----

        [Test]
        public void Zones_ArePerSide_TheDownSideInTheHeatBytes_TheUpSideInTheMode()
        {
            KitchenState kitchen = Grilling();
            kitchen.GriddleReadings(Spot0, Real(13), Rate, out HeatReading down, out HeatReading up);
            Assert.AreEqual(HeatZone.Good, down.Zone, "seitan strips side 1 good at 13 s");
            Assert.AreEqual(HeatZone.Cooking, up.Zone, "side 2 never went down");
            Assert.AreEqual(0, up.Progress);
            Assert.AreEqual(down.Zone, kitchen.HeatReadingOf(Spot0, Real(13), Rate).Zone, "the vessel's heat bytes are the down side");

            kitchen.TryFlip(Spot0, Real(13));
            kitchen.GriddleReadings(Spot0, Real(15), Rate, out down, out up);
            Assert.AreEqual(HeatZone.Good, up.Zone, "side 1 keeps its zone while it is up");
            Assert.AreEqual(HeatZone.Cooking, down.Zone, "side 2 cooks from raw");
            byte mode = kitchen.GriddleMode(Spot0, Real(15), Rate);
            Assert.AreEqual(1, GriddleRules.DownSideOf(mode));
            Assert.AreEqual(1, GriddleRules.FlipsOf(mode));
            Assert.AreEqual(HeatZone.Good, GriddleRules.UpZoneOf(mode));
            Assert.That(GriddleRules.UpProgressOf(mode), Is.InRange(HeatZoneRules.GoodShare, HeatZoneRules.PerfectShare));

            kitchen.GriddleReadings(Spot0, Real(13 + 23), Rate, out down, out _);
            Assert.AreEqual(HeatZone.Burnt, down.Zone, "side 2 burns at 22 s down");
        }

        [Test]
        public void MeasuredZoneTimings_PerSide_AreTheSpecs()
        {
            KitchenState kitchen = KitchenState.CreateDemo();
            void Measure(string baseId, int cut, double good, double perfect, double burnt)
            {
                Ingredient(kitchen, baseId, cut);
                kitchen.TryGet(Food, out Item food);
                kitchen.TryRemove(Food, out _);
                HeatTimeline t = kitchen.FreshSideTimeline(food);
                double scale = CookingTime.TimeScale;
                TestContext.WriteLine(baseId + " per side: good " + (t.GoodFrom / scale).ToString("0.0") + " s, perfect "
                    + (t.PerfectFrom / scale).ToString("0.0") + " s, warning " + ((t.OverdoneFrom - HeatZoneRules.WarnSimSeconds) / scale).ToString("0.0")
                    + " s, overdone " + (t.OverdoneFrom / scale).ToString("0.0") + " s, burnt " + (t.BurntFrom / scale).ToString("0.0") + " s");
                Assert.AreEqual(good, t.GoodFrom / scale, 0.1, baseId + " good");
                Assert.AreEqual(perfect, t.PerfectFrom / scale, 0.1, baseId + " perfect");
                Assert.AreEqual(burnt, t.BurntFrom / scale, 0.1, baseId + " burnt");
            }
            Measure("seitan", 3, 12, 14, 22);
            Measure("paprika", 3, 10, 12, 20);
            Measure("eggplant", 2, 10, 12, 20);
            Measure("mushroom", 1, 10, 12, 20);
            Measure("flatbread", 0, 4, 5, 9);
            Assert.AreEqual(DeepFry.KitchenTimeScale, CookingTime.TimeScale, "the timings are in kitchen seconds");
        }

        [Test]
        public void TheSideTimeline_IsCached_UnderAKeyWithTheSideState()
        {
            KitchenState kitchen = KitchenState.CreateDemo();
            Ingredient(kitchen, "seitan", 3);
            kitchen.TryGet(Food, out Item strips);
            string side = KitchenState.TimelineKeyOf(strips, Worktown.Sim.Cooking.Griddle.OperationId, null, KitchenState.SideTimelineKey);
            string whole = KitchenState.TimelineKeyOf(strips, Worktown.Sim.Cooking.Griddle.OperationId, null);
            StringAssert.Contains("|sides:" + KitchenState.SideTimelineKey, side);
            Assert.AreNotEqual(whole, side, "a side timeline never mixes with a whole-item one");
            Assert.AreEqual(whole, KitchenState.TimelineKeyOf(strips, Worktown.Sim.Cooking.Griddle.OperationId, null, null), "027c keys unchanged");
            kitchen.RegisterGriddleSpot(Spot0, Griddle, 0);
            int before = kitchen.CachedTimelines;
            kitchen.TryPutInto(Food, Spot0, VesselKind.Griddle, Start);
            Assert.AreEqual(before + 1, kitchen.CachedTimelines);
        }

        // ---- the take ----

        [Test]
        public void TakingOff_GivesTheGriddleOpWithBothSides_OrTheFoodUnchangedWhenTooShort()
        {
            KitchenState kitchen = Grilling();
            Assert.AreEqual(CookOutcome.TooShort, kitchen.TryPreviewGriddleTake(Spot0, Start + 10, Rate, out Item raw));
            Assert.AreEqual("seitan sticks", KitchenState.NameOf(raw));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewGriddleTake(Spot0, Real(14), Rate, out Item half));
            Assert.AreEqual("half-grilled seitan strips", KitchenState.NameOf(half));
            kitchen.TryFlip(Spot0, Real(14));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewGriddleTake(Spot0, Real(28), Rate, out Item both));
            Assert.AreEqual("grilled seitan strips", KitchenState.NameOf(both));
            Assert.Greater(KitchenState.QualityOf(both), KitchenState.QualityOf(half) + 0.2f, "both sides good beats one side");
            // The label is the preview name with the side that is down (spec 030, the orchestrator's label rule).
            Assert.AreEqual("grilled seitan strips · side 2 down", kitchen.DescribeHeat(Spot0, Real(28), Rate));
            Assert.AreEqual(KitchenState.NameOf(both), kitchen.ResultName(Spot0, VesselKind.Griddle, Real(28), Rate));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewTakeOut(Spot0, VesselKind.Griddle, Real(28), Rate, out Item taken));
            Assert.AreEqual(both.Hash, taken.Hash);
            kitchen.ClearContents(Spot0, VesselKind.Griddle);
            Assert.AreEqual(0, kitchen.ContentCount(Spot0, VesselKind.Griddle));
            Assert.AreEqual(HeatZone.None, kitchen.HeatReadingOf(Spot0, Real(30), Rate).Zone);
        }

        [Test]
        public void BreadToasts_OnTheGriddle()
        {
            KitchenState kitchen = Grilling("flatbread", 0);
            kitchen.TryFlip(Spot0, Real(5));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewGriddleTake(Spot0, Real(10), Rate, out Item toast));
            Assert.AreEqual("toasted flatbread", KitchenState.NameOf(toast));
            Assert.AreEqual(Worktown.Sim.Cooking.Griddle.ToastOperationId, toast.OperationId);
        }

        [Test]
        public void ASlideOntoAHeldPlate_EmptiesTheSpot()
        {
            KitchenState kitchen = Grilling();
            kitchen.TryFlip(Spot0, Real(14));
            const int plate = 60;
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPour(Spot0, VesselKind.Griddle, plate, VesselKind.Plate, Real(28), Rate, out Item slid));
            Assert.AreEqual("grilled seitan strips", KitchenState.NameOf(slid));
            Assert.AreEqual(0, kitchen.ContentCount(Spot0, VesselKind.Griddle));
            Assert.AreEqual(1, kitchen.CountIn(plate));
            Assert.IsTrue(VesselRules.TipsFromFixed(VesselKind.Griddle, VesselKind.Plate));
            Assert.IsFalse(VesselRules.TipsFromFixed(VesselKind.Pan, VesselKind.Plate));
        }

        // ---- the spot's bytes ----

        [Test]
        public void TheModeByte_CarriesTheDownSide_TheFlipCounter_AndTheUpSide()
        {
            foreach (HeatZone zone in new[] { HeatZone.None, HeatZone.Cooking, HeatZone.Good, HeatZone.Perfect, HeatZone.Overdone, HeatZone.Burnt })
                for (int flips = 0; flips < 6; flips++)
                {
                    byte progress = zone == HeatZone.Good ? (byte)170 : zone == HeatZone.Perfect ? (byte)230 : zone == HeatZone.Cooking ? (byte)80 : (byte)255;
                    byte mode = GriddleRules.Mode(flips % 2, flips, new HeatReading(zone, progress, false));
                    Assert.AreEqual(flips % 2, GriddleRules.DownSideOf(mode));
                    Assert.AreEqual(flips % 4, GriddleRules.FlipsOf(mode));
                    Assert.AreEqual(zone, GriddleRules.UpZoneOf(mode));
                    if (zone == HeatZone.Cooking || zone == HeatZone.Good || zone == HeatZone.Perfect)
                        Assert.AreEqual(progress / 255f, GriddleRules.UpProgressOf(mode), 0.1f, zone.ToString());
                }
            Assert.AreEqual(1, GriddleRules.FlipsBetween(3, 0), "the counter wraps");
            Assert.AreEqual(0, GriddleRules.FlipsBetween(2, 2));
            Assert.AreEqual("side 1", GriddleRules.SideName(0));
            Assert.AreEqual("side 2", GriddleRules.SideName(1));
        }

        // ---- the client's rules ----

        [Test]
        public void Interaction_OnAGriddleSpot()
        {
            AimTarget withFood = AimTarget.Cookware(VesselKind.Griddle, 1, "half-grilled seitan strips");
            AimTarget empty = AimTarget.Cookware(VesselKind.Griddle, 0, null);
            Assert.AreEqual(InteractionAction.Flip, InteractionRules.ActionFor(Held.Spatula, withFood));
            Assert.AreEqual("Click · flip half-grilled seitan strips", InteractionRules.PromptFor(Held.Spatula, withFood));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Spatula, empty));
            Assert.AreEqual("Put food on the griddle first", InteractionRules.PromptFor(Held.Spatula, empty));
            Assert.AreEqual(InteractionAction.Take, InteractionRules.ActionFor(Held.Nothing, withFood));
            Assert.AreEqual("Click · take half-grilled seitan strips   Flip: use the spatula", InteractionRules.PromptFor(Held.Nothing, withFood));
            Held plate = Held.Cookware(VesselKind.Plate, 0, null);
            Assert.AreEqual(InteractionAction.Take, InteractionRules.ActionFor(plate, withFood));
            Assert.AreEqual("Click · slide half-grilled seitan strips on the plate", InteractionRules.PromptFor(plate, withFood));
            Held strips = Held.Food("seitan sticks", false);
            Assert.AreEqual(InteractionAction.PutInto, InteractionRules.ActionFor(strips, empty));
            Assert.AreEqual("Click · put seitan sticks on the griddle", InteractionRules.PromptFor(strips, empty));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(strips, withFood), "one item per spot");
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Knife, withFood));
            // The right click flips (by hand the server says "Use the spatula"); only the spatula predicts the arc.
            Assert.IsTrue(InteractionRules.RightClickFlips(withFood));
            Assert.IsFalse(InteractionRules.RightClickFlips(empty));
            Assert.IsFalse(InteractionRules.RightClickFlips(withFood.TooFar()));
            Assert.IsTrue(InteractionRules.PredictsFlip(Held.Spatula, withFood));
            Assert.IsFalse(InteractionRules.PredictsFlip(Held.Nothing, withFood));
            Assert.AreEqual("Click · griddle on", InteractionRules.PromptFor(Held.Nothing, AimTarget.Button(StationKind.Griddle, GriddleRules.PowerOption, 0)));
            Assert.AreEqual("Click · griddle off",
                InteractionRules.PromptFor(Held.Nothing, AimTarget.Button(StationKind.Griddle, GriddleRules.PowerOption, GriddleRules.Flags(true))));
        }

        [Test]
        public void TheFlipPrediction_IsConfirmedByTheCounter_DeniedWhenTheSpotEmpties()
        {
            var start = new ItemSnapshot(true, -1, Vector3.zero, 1);
            Assert.AreEqual(PredictionPhase.Pending, FeelRules.Judge(PredictionKind.Flip, 3, start, start));
            Assert.AreEqual(PredictionPhase.Confirmed, FeelRules.Judge(PredictionKind.Flip, 3, start, new ItemSnapshot(true, -1, Vector3.zero, 2)));
            Assert.AreEqual(PredictionPhase.Denied, FeelRules.Judge(PredictionKind.Flip, 3, start, new ItemSnapshot(false, -1, Vector3.zero, 1)));
            Assert.AreEqual(AnswerKind.Cook, PredictionBook<object>.AnswerFor(PredictionKind.Flip));
        }

        [Test]
        public void TheFlipArc_IsAQuarterSecond_UpAndDown_WithAHalfTurn()
        {
            Assert.AreEqual(0.25f, GriddleArtRules.FlipSeconds);
            Assert.AreEqual(0f, GriddleArtRules.Lift(0f));
            Assert.AreEqual(GriddleArtRules.FlipHeight, GriddleArtRules.Lift(0.125f), 1e-4f);
            Assert.AreEqual(0f, GriddleArtRules.Lift(0.25f));
            Assert.AreEqual(0f, GriddleArtRules.Turn(0f));
            Assert.AreEqual(90f, GriddleArtRules.Turn(0.125f), 1e-3f);
            Assert.AreEqual(180f, GriddleArtRules.Turn(0.3f));
            Assert.IsTrue(GriddleArtRules.IsFlipping(0.1f));
            Assert.IsFalse(GriddleArtRules.IsFlipping(0.25f));
            Assert.IsFalse(GriddleArtRules.MarksShown(HeatZone.Cooking));
            Assert.IsTrue(GriddleArtRules.MarksShown(HeatZone.Good));
            Assert.AreEqual(Palette.Charcoal, GriddleArtRules.MarkColor(HeatZone.Burnt));
        }

        [Test]
        public void TheTwoHalfRing_HasAHalfPerSide_AndMarksTheDownSide()
        {
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            HeatRingRules.BuildHalves(0f, HeatZone.Cooking, 0f, HeatZone.None, 0, v, c, t);
            int empty = t.Count;
            HeatRingRules.BuildHalves(0.7f, HeatZone.Good, 0.3f, HeatZone.Cooking, 0, v, c, t);
            Assert.Greater(t.Count, empty, "both halves fill");
            Assert.AreEqual(v.Count, c.Count);
            Assert.AreEqual(0, t.Count % 3);
            Assert.IsTrue(c.Contains(HeatRingRules.FillColor(HeatZone.Good)), "side 1's half in its zone colour");
            // Side 1 fills down the left (x < 0), side 2 down the right.
            HeatRingRules.BuildHalves(1f, HeatZone.Perfect, 0f, HeatZone.None, 0, v, c, t);
            bool leftFill = false, rightFill = false;
            for (int i = 0; i < v.Count; i++)
            {
                if (c[i] != HeatRingRules.FillColor(HeatZone.Perfect) || Mathf.Abs(v[i].magnitude - HeatRingRules.FillOuter) > 0.01f) continue;
                if (v[i].x < -0.05f) leftFill = true;
                if (v[i].x > 0.05f) rightFill = true;
            }
            Assert.IsTrue(leftFill, "side 1 on the left");
            Assert.IsFalse(rightFill, "side 2 is still empty");
        }

        // ---- the menu ----

        [Test]
        public void GrilledVeggies_AndToastedFlatbread_AreOnTheMenu_CookedOnTheGriddle()
        {
            KitchenState kitchen = Kitchen();
            Ingredient(kitchen, "paprika", 3, 43);
            Ingredient(kitchen, "eggplant", 2, 44);
            kitchen.TryPutInto(43, Spot0, VesselKind.Griddle, Start);
            kitchen.TryPutInto(44, Spot1, VesselKind.Griddle, Start);
            kitchen.TryToggleGriddle(Griddle, Start);
            kitchen.TryFlip(Spot0, Real(11));
            kitchen.TryFlip(Spot1, Real(11));
            const int plate = 60;
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPour(Spot0, VesselKind.Griddle, plate, VesselKind.Plate, Real(22), Rate, out _));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPour(Spot1, VesselKind.Griddle, plate, VesselKind.Plate, Real(22), Rate, out _));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewTake(plate, VesselKind.Plate, out Item veggies));
            MenuDish dish = Menu.ById(Menu.GrilledVeggies);
            Assert.AreEqual("Grilled veggies", dish.Name);
            Assert.AreEqual(OrderMatch.Exact, OrderRules.Match(veggies, dish), KitchenState.NameOf(veggies));
            TownEconomy economy = TownEconomy.CreateDemo();
            Assert.IsTrue(economy.TryOpenAccount("p0", 500));
            SaleResult sale = economy.TrySell("p0", veggies);
            Assert.IsTrue(sale.Ok, "Grilled veggies: " + sale.Error);
            Assert.AreEqual(dish.Market, sale.MarketId);

            // Close: one side never went down (no flip): one step wrong.
            KitchenState again = Kitchen();
            Ingredient(again, "paprika", 3, 43);
            Ingredient(again, "eggplant", 2, 44);
            again.TryPutInto(43, Spot0, VesselKind.Griddle, Start);
            again.TryPutInto(44, Spot1, VesselKind.Griddle, Start);
            again.TryToggleGriddle(Griddle, Start);
            again.TryFlip(Spot1, Real(11));
            again.TryPour(Spot0, VesselKind.Griddle, plate, VesselKind.Plate, Real(22), Rate, out _);
            again.TryPour(Spot1, VesselKind.Griddle, plate, VesselKind.Plate, Real(22), Rate, out _);
            again.TryPreviewTake(plate, VesselKind.Plate, out Item unflipped);
            Assert.AreEqual(OrderMatch.Close, OrderRules.Match(unflipped, dish), KitchenState.NameOf(unflipped));
            Assert.Less(KitchenState.QualityOf(unflipped), KitchenState.QualityOf(veggies));

            KitchenState bread = Grilling("flatbread", 0);
            bread.TryFlip(Spot0, Real(5));
            bread.TryPreviewGriddleTake(Spot0, Real(10), Rate, out Item toast);
            Assert.AreEqual(OrderMatch.Exact, OrderRules.Match(toast, Menu.ById(Menu.ToastedFlatbread)));
            Assert.IsTrue(Menu.CanMake(dish, Menu.CrateIngredients), "the vegetables are in stock");
        }

        // ---- the 029b review additions ----

        [Test]
        public void EffectiveHands_RealHandsWin_ATakeCountsAsItsFood_APickUpInFlightOnlyRefuses()
        {
            Held knife = Held.Knife;
            Assert.AreEqual(knife.Name, InteractionRules.EffectiveHands(knife, "potato", Held.Spatula).Name, "what is really held wins");
            Held take = InteractionRules.EffectiveHands(Held.Nothing, "potato", null);
            Assert.IsTrue(take.IsFood);
            Assert.AreEqual("potato", take.Name);
            Held flying = InteractionRules.EffectiveHands(Held.Nothing, null, Held.Knife);
            Assert.IsTrue(flying.IsHolding && flying.IsPending, "a pick-up in flight counts as held");
            Assert.IsFalse(InteractionRules.EffectiveHands(Held.Nothing, null, null).IsHolding);
            // Held for refusals: no stock take, no plate shelf, no second pick-up while it flies.
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(flying, AimTarget.Crate("potato", 1)));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(flying, AimTarget.OnStation(StationKind.PlateShelf)));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(flying, AimTarget.Item("knife")));
        }

        [Test]
        public void APlateInFlight_NeverTipsABasket_OrSlidesFromTheGriddle()
        {
            Held plateInFlight = InteractionRules.EffectiveHands(Held.Nothing, null, Held.Cookware(VesselKind.Plate, 0, null));
            AimTarget basket = AimTarget.Cookware(VesselKind.Basket, 1, "golden fries", FryerRules.Mode(BasketState.Raised, 0));
            AimTarget spot = AimTarget.Cookware(VesselKind.Griddle, 1, "grilled seitan strips");
            Assert.AreEqual(InteractionAction.Take, InteractionRules.ActionFor(Held.Cookware(VesselKind.Plate, 0, null), basket), "a real plate tips");
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(plateInFlight, basket), "no tip: it would be a bare-hand take");
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(plateInFlight, spot));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(plateInFlight, AimTarget.Cookware(VesselKind.Plate, 1, "fries")), "no pour");
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(plateInFlight, AimTarget.Spot(0)), "no place");
        }

        [Test]
        public void TheFryerButtonPrompt_UsesTheSameSeenStateAsTheClick()
        {
            byte synced = FryerRules.FryerFlags(true, false, false); // the server still says basket 0 is up
            byte seen = FryerRules.WithLowered(synced, 0, true);     // our own lower is on its way
            Assert.IsTrue(FryerRules.IsLowered(seen, 0));
            Assert.IsTrue(FryerRules.IsOn(seen));
            Assert.AreEqual("Click · raise the basket", InteractionRules.FryerButtonPrompt(FryerRules.BasketOption(0), seen));
            Assert.AreEqual(FryerRules.RaiseOption(0), FryerRules.IntentOption(0, FryerRules.IsLowered(seen, 0)), "the click asks for the same");
            Assert.AreEqual(synced, FryerRules.WithLowered(seen, 0, false));
            Assert.AreEqual(seen, FryerRules.WithLowered(seen, 5, false), "no such basket: unchanged");
        }
    }
}
