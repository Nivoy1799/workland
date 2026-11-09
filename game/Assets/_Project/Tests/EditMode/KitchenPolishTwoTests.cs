using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Cooking;
using Worktown.Game.Net.Rules;
using Worktown.Game.Player;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 031b: kitchen polish 2. The squeeze restart fix (10.0), the calm view (1), the warning lead (2), portion prices and
    /// the board's charging steps (3, 10), the hold pose (4), short build names (6), the client caches (7), grill marks (8), and
    /// the other 031 review findings (the step prediction, the timing of the held squeeze, what the client offers).
    /// </summary>
    public class KitchenPolishTwoTests
    {
        private const int Board = 8;
        private const int Spot = 90;
        private const int Owner = 3;
        private const uint Start = HeatTestKit.StartTick;
        private const double Rate = HeatTestKit.TickRate;

        private static uint Real(double seconds) => Start + (uint)System.Math.Round(seconds * Rate);

        private static KitchenState BoardWithWrap()
        {
            var kitchen = KitchenState.CreateDemo();
            kitchen.RegisterAssemblySpot(Spot, Board);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateIngredient("wrap", out Item wrap));
            kitchen.Register(40, wrap);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(40, Spot, VesselKind.Assembly, Start));
            return kitchen;
        }

        /// <summary>A wrap with lettuce slices on it (open, on bread, two layers: it folds).</summary>
        private static KitchenState BoardWithFilledWrap()
        {
            KitchenState kitchen = BoardWithWrap();
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateIngredient("lettuce", out Item lettuce));
            kitchen.Register(41, lettuce);
            HeatTestKit.Cut(kitchen, 41, 2);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(41, Spot, VesselKind.Assembly, Start));
            return kitchen;
        }

        /// <summary>Counts the board's charges; refuses all of them with <see cref="Refuse"/>.</summary>
        private sealed class CountingWallet : IAssemblyWallet
        {
            public readonly List<(int Owner, string Id, int Percent)> Charges = new List<(int, string, int)>();
            public CookOutcome Refuse = CookOutcome.Ok;
            public int Calls;

            public CookOutcome Charge(int owner, string grocerId, int percent, out long cost)
            {
                Calls++;
                cost = 0;
                if (Refuse != CookOutcome.Ok) return Refuse;
                Charges.Add((owner, grocerId, percent));
                cost = PriceRules.PortionCost(80, percent);
                return CookOutcome.Ok;
            }
        }

        // ---- 10.0: a repeated press never restarts the squeeze ----

        [Test]
        public void ARepeatedPress_KeepsTheFirstStart_SoTheAutoStopStillComes()
        {
            KitchenState kitchen = BoardWithWrap();
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryStartSqueeze(Board, 0, Owner, Start));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryStartSqueeze(Board, 0, Owner, Real(2.9)), "a same-owner press is Ok");
            Assert.IsTrue(kitchen.IsSqueezing(Board, 0, out int owner, out uint startTick));
            Assert.AreEqual(Owner, owner);
            Assert.AreEqual(Start, startTick, "the original start is kept");
            Assert.IsFalse(kitchen.TryFindOverdueSqueeze(Real(2.95), Rate, out _, out _, out _));
            Assert.IsTrue(kitchen.TryFindOverdueSqueeze(Real(3.0), Rate, out int board, out int d, out int who), "overdue at t0 + 3 s");
            Assert.AreEqual((Board, 0, Owner), (board, d, who));
            Assert.AreEqual(CookOutcome.DispenserBusy, kitchen.TryStartSqueeze(Board, 0, Owner + 1, Real(2.95)), "still busy for others");
        }

        // ---- 1: the calm view ----

        [Test]
        public void TheCalmView_AFullGriddleInFirstPerson_ShowsTheAimedRingAndTheAlarmsOnly()
        {
            // Six spots cooking, the third aimed, the fifth warning: two rings, never six.
            var distances = new List<float> { 1.0f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f };
            var ranks = new int[6];
            LabelRules.NearRanks(distances, ranks);
            int shown = 0;
            for (int i = 0; i < 6; i++)
            {
                bool aimed = i == 2;
                bool alarm = LabelRules.IsAlarm(i == 4 ? HeatZone.Perfect : HeatZone.Good, i == 4);
                if (LabelRules.ShowsRing(aimed, alarm, true, ranks[i])) shown++;
            }
            Assert.AreEqual(2, shown);

            // Third person: the four nearest plus the alarms beyond them.
            shown = 0;
            for (int i = 0; i < 6; i++)
                if (LabelRules.ShowsRing(false, LabelRules.IsAlarm(i == 5 ? HeatZone.Burnt : HeatZone.Good, false), false, ranks[i])) shown++;
            Assert.AreEqual(LabelRules.ThirdPersonRings + 1, shown);
        }

        [Test]
        public void TheCalmView_Alarms_AreWarningOverdoneAndBurnt()
        {
            Assert.IsTrue(LabelRules.IsAlarm(HeatZone.Perfect, true));
            Assert.IsTrue(LabelRules.IsAlarm(HeatZone.Overdone, false));
            Assert.IsTrue(LabelRules.IsAlarm(HeatZone.Burnt, false));
            Assert.IsFalse(LabelRules.IsAlarm(HeatZone.Good, false));
            Assert.IsFalse(LabelRules.IsAlarm(HeatZone.Cooking, false));
            Assert.IsTrue(LabelRules.ShowsRing(true, false, true, -1), "the aimed one");
            Assert.IsFalse(LabelRules.ShowsRing(false, false, true, 0), "first person: nearness alone is not enough");
            Assert.IsFalse(LabelRules.ShowsRing(false, false, false, -1));
            Assert.IsFalse(LabelRules.ShowsRing(false, false, false, LabelRules.ThirdPersonRings));
            Assert.IsTrue(LabelRules.ShowsRing(false, false, false, LabelRules.ThirdPersonRings - 1));
            // The 027d priorities stay: the aimed label and the held item before the rings, the rings before every other label.
            Assert.Less(LabelRules.PriorityHeld, LabelRules.PriorityRing);
            Assert.Less(LabelRules.PriorityRing, LabelRules.PriorityQueueHead);
        }

        [Test]
        public void NearRanks_OrderByDistance_TiesByIndex_NaNLast()
        {
            var ranks = new int[5];
            LabelRules.NearRanks(new List<float> { 3f, 1f, float.NaN, 1f, 2f }, ranks);
            CollectionAssert.AreEqual(new[] { 3, 0, 4, 1, 2 }, ranks);
        }

        // ---- 2: the warning lead ----

        [Test]
        public void TheWarningLead_IsFortyPercentOfTheGoodWindow_ClampedToOneToThreeSeconds()
        {
            Assert.AreEqual(1.0, HeatZoneRules.WarnLeadRealSeconds(0.5), 1e-9);
            Assert.AreEqual(1.0, HeatZoneRules.WarnLeadRealSeconds(2.5), 1e-9);
            Assert.AreEqual(1.6, HeatZoneRules.WarnLeadRealSeconds(4.0), 1e-9);
            Assert.AreEqual(3.0, HeatZoneRules.WarnLeadRealSeconds(7.5), 1e-9);
            Assert.AreEqual(3.0, HeatZoneRules.WarnLeadRealSeconds(30.0), 1e-9);
            Assert.AreEqual(3.0, HeatZoneRules.WarnLeadRealSeconds(double.NaN), 1e-9);
            Assert.AreEqual(3.0, HeatZoneRules.WarnLeadRealSeconds(double.PositiveInfinity), 1e-9);
            double s = CookingTime.TimeScale;
            var toast = new HeatTimeline(0, 4 * s, 6 * s, 6 * s, 9 * s);
            Assert.AreEqual(1.0 * s, HeatZoneRules.WarnLeadSimSeconds(toast), 1e-9);
            Assert.IsFalse(HeatZoneRules.Warn(4.9 * s, toast));
            Assert.IsTrue(HeatZoneRules.Warn(5.0 * s, toast));
        }

        [Test]
        public void ShortWindows_NeverWarnBeforeGood_OnTheRealSim()
        {
            KitchenState kitchen = KitchenState.CreateDemo();
            foreach (string baseId in new[] { "flatbread", "paprika", "mushroom" })
            {
                Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateIngredient(baseId, out Item food));
                HeatTimeline t = kitchen.FreshSideTimeline(food);
                double warnFrom = t.OverdoneFrom - HeatZoneRules.WarnLeadSimSeconds(t);
                TestContext.WriteLine(baseId + ": good " + (t.GoodFrom / CookingTime.TimeScale).ToString("0.0") + " s, warning "
                    + (warnFrom / CookingTime.TimeScale).ToString("0.0") + " s, overdone " + (t.OverdoneFrom / CookingTime.TimeScale).ToString("0.0") + " s");
                Assert.GreaterOrEqual(warnFrom, t.GoodFrom, baseId + ": the warning comes after Good");
                Assert.IsTrue(HeatZoneRules.Warn(t.OverdoneFrom - 0.01, t), baseId);
                Assert.IsFalse(HeatZoneRules.Warn(t.GoodFrom - 0.01, t), baseId);
            }
            // A long window keeps the old 3 s.
            var pot = new HeatTimeline(10, 100, 200, 200, 250);
            Assert.AreEqual(HeatZoneRules.WarnSimSeconds, HeatZoneRules.WarnLeadSimSeconds(pot), 1e-9);
        }

        // ---- 3 and 10: portion prices and the board's charges (service level, through AssemblyDesk) ----

        [Test]
        public void ASqueeze_IsChargedOnce_AtItsPortion_OnRelease()
        {
            KitchenState kitchen = BoardWithWrap();
            var desk = new AssemblyDesk(kitchen);
            var wallet = new CountingWallet();
            Assert.AreEqual(CookOutcome.Ok, desk.Press(Board, 0, Owner, Start));
            Assert.AreEqual(0, wallet.Calls, "a press charges nothing");
            Assert.AreEqual(CookOutcome.Ok, desk.Release(Board, 0, Owner, Real(0.8), Rate, wallet, out AssemblyDesk.SqueezeResult r));
            Assert.AreEqual(1, wallet.Calls, "charged exactly once");
            Assert.AreEqual((Owner, "garlic_herb_sauce", 20), wallet.Charges[0], "0.8 s: two started halves, 20 %");
            Assert.AreEqual(0.8f, r.Seconds, 0.05f);
            Assert.AreEqual(2, kitchen.LayerCount(Spot), "the sauce is on");
            Assert.AreEqual(CookOutcome.Ok, desk.Release(Board, 0, Owner, Real(1.2), Rate, wallet, out _), "a second release is harmless");
            Assert.AreEqual(1, wallet.Calls, "and charges nothing");

            // Another squeeze of the same sauce adds to the layer and is charged for its own time only.
            Assert.AreEqual(CookOutcome.Ok, desk.Press(Board, 0, Owner, Real(2)));
            Assert.AreEqual(CookOutcome.Ok, desk.Release(Board, 0, Owner, Real(2.3), Rate, wallet, out r));
            Assert.AreEqual(10, wallet.Charges[1].Percent);
            Assert.AreEqual(1.1f, r.TopSeconds, 0.05f, "the layer holds both");
            Assert.AreEqual(2, kitchen.LayerCount(Spot));
        }

        [Test]
        public void ARefusedSqueeze_ChargesNothing_AndChangesNothing()
        {
            // The wallet says no: the build stays as it was.
            KitchenState kitchen = BoardWithWrap();
            var desk = new AssemblyDesk(kitchen);
            var poor = new CountingWallet { Refuse = CookOutcome.NotEnoughMoney };
            Item before = kitchen.BuildOn(Spot);
            Assert.AreEqual(CookOutcome.Ok, desk.Press(Board, 1, Owner, Start));
            Assert.AreEqual(CookOutcome.NotEnoughMoney, desk.Release(Board, 1, Owner, Real(1), Rate, poor, out _));
            Assert.AreEqual(0, poor.Charges.Count);
            Assert.AreSame(before, kitchen.BuildOn(Spot));
            Assert.IsFalse(kitchen.IsSqueezing(Board, 1, out _, out _), "the dispenser is free again");

            // The board was emptied while squeezing: the release is refused before any charge.
            KitchenState taken = BoardWithWrap();
            var desk2 = new AssemblyDesk(taken);
            var wallet = new CountingWallet();
            Assert.AreEqual(CookOutcome.Ok, desk2.Press(Board, 0, Owner, Start));
            Assert.AreEqual(CookOutcome.Ok, taken.TryPreviewBuildTake(Spot, out Item build));
            taken.CommitBuild(Spot, null);
            Assert.IsNotNull(build);
            Assert.AreEqual(CookOutcome.StationEmpty, desk2.Release(Board, 0, Owner, Real(1), Rate, wallet, out _));
            Assert.AreEqual(0, wallet.Calls);

            // A press that was refused (someone else squeezes) leaves no squeeze to charge.
            KitchenState busy = BoardWithWrap();
            var desk3 = new AssemblyDesk(busy);
            Assert.AreEqual(CookOutcome.Ok, desk3.Press(Board, 0, Owner, Start));
            Assert.AreEqual(CookOutcome.DispenserBusy, desk3.Press(Board, 0, Owner + 1, Real(0.5)));
            Assert.AreEqual(CookOutcome.Ok, desk3.Release(Board, 0, Owner + 1, Real(1), Rate, wallet, out _));
            Assert.AreEqual(0, wallet.Calls, "the other player's release charges nobody");
        }

        [Test]
        public void TheRelease_NeedsNoReach_ThePressAndPackagingDo()
        {
            Assert.IsFalse(AssemblyDesk.NeedsReach(StationKind.Assembly, BuildRules.SqueezeStopOption(0)));
            Assert.IsFalse(AssemblyDesk.NeedsReach(StationKind.Assembly, BuildRules.SqueezeStopOption(1)));
            Assert.IsTrue(AssemblyDesk.NeedsReach(StationKind.Assembly, BuildRules.SqueezeStartOption(0)));
            Assert.IsTrue(AssemblyDesk.NeedsReach(StationKind.Assembly, BuildRules.PackagingOption(BuildRules.Paper)));
            Assert.IsTrue(AssemblyDesk.NeedsReach(StationKind.Stove, BuildRules.SqueezeStopOption(0)), "only the board's release");

            // Released from anywhere (the desk has no reach): charged once.
            KitchenState kitchen = BoardWithWrap();
            var desk = new AssemblyDesk(kitchen);
            var wallet = new CountingWallet();
            desk.Press(Board, 0, Owner, Start);
            Assert.AreEqual(CookOutcome.Ok, desk.Release(Board, 0, Owner, Real(0.3), Rate, wallet, out _));
            Assert.AreEqual(1, wallet.Calls);
            Assert.AreEqual(10, wallet.Charges[0].Percent);
        }

        [Test]
        public void TheAutoStop_ChargesOnceAtMostThirtyPercent_AndALateReleaseNothing()
        {
            KitchenState kitchen = BoardWithWrap();
            var desk = new AssemblyDesk(kitchen);
            var wallet = new CountingWallet();
            desk.Press(Board, 1, Owner, Start);
            desk.Press(Board, 1, Owner, Real(2.9)); // 10.0: the second press does not restart it
            Assert.IsTrue(kitchen.TryFindOverdueSqueeze(Real(3.0), Rate, out int board, out int d, out int owner));
            Assert.AreEqual(CookOutcome.Ok, desk.End(board, d, owner, Real(3.0), Rate, wallet, out AssemblyDesk.SqueezeResult r));
            Assert.AreEqual(1, wallet.Calls);
            Assert.AreEqual(PriceRules.PortionMaxPercent, wallet.Charges[0].Percent);
            Assert.AreEqual(BuildRules.AutoStopSeconds, r.Seconds, 1e-4f);
            Assert.AreEqual(CookOutcome.Ok, desk.Release(Board, 1, Owner, Real(5), Rate, wallet, out _));
            Assert.AreEqual(1, wallet.Calls, "the late release after the auto-stop charges nothing");
        }

        [Test]
        public void APlayerWhoLeft_IsCancelled_WithoutACharge_AndTheDispenserIsFree()
        {
            KitchenState kitchen = BoardWithWrap();
            var desk = new AssemblyDesk(kitchen);
            var wallet = new CountingWallet();
            desk.Press(Board, 0, Owner, Start);
            Assert.IsTrue(BuildRules.IsSqueezing(kitchen.AssemblyFlags(Board), 0));
            desk.Cancel(Board, 0);
            Assert.AreEqual(0, wallet.Calls);
            Assert.AreEqual(0, kitchen.AssemblyFlags(Board), "no ribbon for anyone");
            Assert.AreEqual(1, kitchen.LayerCount(Spot), "nothing went on");
            Assert.AreEqual(CookOutcome.Ok, desk.Press(Board, 0, Owner + 1, Real(0.1)), "someone else can use it at once");
        }

        [Test]
        public void Packaging_IsChargedOnceAtItsFullPrice_ARefusalChargesNothing()
        {
            KitchenState kitchen = BoardWithFilledWrap();
            var desk = new AssemblyDesk(kitchen);
            var wallet = new CountingWallet();
            Assert.AreEqual(CookOutcome.FoldFirst, desk.Package(Spot, BuildRules.Paper, Owner, wallet, out Item none, out _));
            Assert.IsNull(none);
            Assert.AreEqual(0, wallet.Calls, "paper before folding charges nothing");
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryFold(Spot));
            var poor = new CountingWallet { Refuse = CookOutcome.NotEnoughMoney };
            Item folded = kitchen.BuildOn(Spot);
            Assert.AreEqual(CookOutcome.NotEnoughMoney, desk.Package(Spot, BuildRules.Paper, Owner, poor, out _, out _));
            Assert.AreSame(folded, kitchen.BuildOn(Spot));
            Assert.AreEqual(CookOutcome.Ok, desk.Package(Spot, BuildRules.Paper, Owner, wallet, out Item wrapped, out _));
            Assert.AreEqual(1, wallet.Calls);
            Assert.AreEqual((Owner, BuildRules.PackagingId(BuildRules.Paper), 100), wallet.Charges[0]);
            Assert.AreSame(wrapped, kitchen.BuildOn(Spot));
            Assert.AreEqual(CookOutcome.BuildClosed, desk.Package(Spot, BuildRules.Box, Owner, wallet, out _, out _));
            Assert.AreEqual(1, wallet.Calls, "a second packaging is refused before any charge");
        }

        // ---- 4: the hold pose ----

        [Test]
        public void TheHoldPose_AWrapFillsAQuarterOfTheView_InTheLowerRight_LargeThingsAreScaled()
        {
            const float fov = ViewRules.FieldOfView, aspect = 16f / 9f;
            Assert.AreEqual(HoldViewRules.ReferenceShare, HoldViewRules.Share(0.25f, fov), 1e-4f, "a wrap: about 25 % of the view height");
            Assert.AreEqual(1f, HoldViewRules.FitScale(0.25f, fov));
            Assert.AreEqual(1f, HoldViewRules.FitScale(0.135f, fov), "a potato keeps its size");
            float pan = HoldViewRules.FitScale(0.52f, fov);
            Assert.Less(pan, 1f, "the pan is drawn smaller");
            Assert.AreEqual(HoldViewRules.MaxShare, HoldViewRules.Share(0.52f, fov) * pan, 1e-4f);
            foreach (float size in new[] { 0.05f, 0.135f, 0.25f, 0.3f, 0.52f, 1f, 3f })
            {
                Rect r = HoldViewRules.ScreenBound(size, fov, aspect);
                Assert.GreaterOrEqual(r.xMin, 0.5f, size + " m: right of the centre");
                Assert.LessOrEqual(r.yMax, 0.5f, size + " m: below the centre");
                Assert.GreaterOrEqual(r.yMin, 0f, size + " m: whole on screen (bottom)");
                Assert.LessOrEqual(r.xMax, 1f, size + " m: whole on screen (right)");
            }
            Vector3 c = HoldViewRules.CentreInView(fov, aspect);
            Assert.Greater(c.x, 0f);
            Assert.Less(c.y, 0f);
            Assert.AreEqual(HoldViewRules.Depth(fov), c.z, 1e-5f);
        }

        // ---- 6: short build names in labels ----

        [Test]
        public void ABuildWithoutADishName_ShowsThreeWordsAndAnEllipsis_InLabels()
        {
            Assert.AreEqual("wrap with golden…", BuildRules.LabelName("wrap with golden falafel balls and lettuce slices", true));
            Assert.AreEqual("wrap with falafel…", BuildRules.LabelName("wrap with falafel balls", true));
            Assert.AreEqual("folded wrap with…", BuildRules.LabelName("folded wrap with lettuce slices", true));
            Assert.AreEqual("falafel wrap in paper", BuildRules.LabelName("falafel wrap in paper", true), "a dish keeps its name");
            Assert.AreEqual("veggie box", BuildRules.LabelName("veggie box", true));
            Assert.AreEqual("wrap with lettuce", BuildRules.LabelName("wrap with lettuce", true), "three words need no ellipsis");
            Assert.AreEqual("potato with salt and pepper", BuildRules.LabelName("potato with salt and pepper", false), "no build");
            Assert.AreEqual(string.Empty, BuildRules.LabelName(null, true));
        }

        [Test]
        public void TheShortName_KeepsTheFullNameInTheSim()
        {
            KitchenState kitchen = BoardWithFilledWrap();
            string full = KitchenState.NameOf(kitchen.BuildOn(Spot));
            StringAssert.Contains(" with ", full);
            Assert.AreNotEqual(full, BuildRules.LabelName(full, true));
            StringAssert.StartsWith(BuildRules.LabelName(full, true).TrimEnd('…'), full);
        }

        // ---- 7: the client caches ----

        [Test]
        public void TheClientCaches_RefillWhileEmpty_OrWhenAMemberIsGone()
        {
            Assert.IsTrue(CacheRules.ShouldRefill(true, false, 0, 0f, 0f), "never filled");
            Assert.IsTrue(CacheRules.ShouldRefill(false, true, 6, 0f, 99f), "a member was destroyed");
            Assert.IsTrue(CacheRules.ShouldRefill(false, false, 0, 5f, 5f), "empty: look again");
            Assert.IsFalse(CacheRules.ShouldRefill(false, false, 0, 4.9f, 5f), "empty, but at most once a second");
            Assert.IsFalse(CacheRules.ShouldRefill(false, false, 6, 99f, 5f), "full and whole: kept");
            Assert.AreEqual(1f, CacheRules.EmptyRetrySeconds);
        }

        // ---- 8: grill marks ----

        [Test]
        public void GrilledFood_KeepsItsMarksAnywhere_FromTheMediumBucket()
        {
            Assert.AreEqual(FoodLook.HeatGrilled, FoodLookRules.HeatOf(Griddle.OperationId));
            Assert.AreEqual(FoodLook.HeatGrilled, FoodLookRules.HeatOf(Griddle.ToastOperationId));
            Assert.AreEqual(FoodLook.HeatFried, FoodLookRules.HeatOf("saute"));
            Assert.IsTrue(FoodArtRules.IsFried(FoodLook.HeatGrilled), "it still shifts golden");
            var grilled = new FoodLook(0, FoodLook.ShapeSticks, FoodLook.DonenessMedium, FoodLook.HeatGrilled, 0, 0);
            Assert.IsTrue(FoodArtRules.HasGrillMarks(grilled));
            Assert.IsFalse(FoodArtRules.HasGrillMarks(new FoodLook(0, 0, FoodLook.DonenessShort, FoodLook.HeatGrilled, 0, 0)));
            Assert.IsFalse(FoodArtRules.HasGrillMarks(new FoodLook(0, 0, FoodLook.DonenessLong, FoodLook.HeatFried, 0, 0)));

            // The real griddle: strips grilled to Perfect on both sides, then on a plate: the part keeps the grilled look.
            KitchenState kitchen = KitchenState.CreateDemo();
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateIngredient("paprika", out Item paprika));
            HeatTimeline t = kitchen.FreshSideTimeline(paprika);
            float side = (float)((t.PerfectFrom + 0.5 * CookingTime.TimeScale));
            var factory = new ItemFactory(new ItemCatalog(StreetFoodItems.All.Concat(StreetFoodItems.GrillVegetables)), OperationCatalog.CreateDefault());
            OperationResult done = factory.Apply(Griddle.OperationId, new[] { paprika }, Griddle.Params(side, side));
            Assert.IsTrue(done.Ok);
            OperationResult plated = factory.Apply("plate", new[] { done.Item }, new Dictionary<string, ParamValue>());
            Assert.IsTrue(plated.Ok);
            IReadOnlyList<FoodLook> parts = FoodLookRules.PartsOf(plated.Item);
            Assert.AreEqual(1, parts.Count);
            Assert.IsTrue(FoodArtRules.HasGrillMarks(parts[0]), "on the plate too");
        }

        [Test]
        public void TheSpatula_MovesInTheHandOnAFlip()
        {
            Assert.AreEqual(0f, HeldItemView.FlipAmount(-0.1f));
            Assert.AreEqual(0f, HeldItemView.FlipAmount(HeldItemView.FlipSeconds));
            Assert.AreEqual(1f, HeldItemView.FlipAmount(HeldItemView.FlipSeconds * 0.5f), 1e-4f);
            Assert.Greater(HeldItemView.FlipAmount(0.05f), 0f);
        }

        // ---- 10: the step prediction (two quick steps) ----

        [Test]
        public void TwoQuickSteps_EachConfirmOnlyByTheirOwnBits_AndTheViewNeverFlickers()
        {
            int open = BuildRules.Mode(BuildRules.CloseOpen, BuildRules.PackNone, true) & 0x0F;
            int folded = BuildRules.Mode(BuildRules.CloseFolded, BuildRules.PackNone, true) & 0x0F;
            int wrapped = BuildRules.Mode(BuildRules.CloseWrapped, BuildRules.PackNone, true) & 0x0F;
            var atClick = new ItemSnapshot(true, -1, Vector3.zero, open);
            ItemSnapshot fold = FeelRules.BuildStepStart(atClick, folded);
            ItemSnapshot paper = FeelRules.BuildStepStart(atClick, wrapped); // clicked before the fold arrived
            var nowFolded = new ItemSnapshot(true, -1, Vector3.zero, folded);
            var nowWrapped = new ItemSnapshot(true, -1, Vector3.zero, wrapped);
            Assert.AreEqual(PredictionPhase.Confirmed, FeelRules.Judge(PredictionKind.BuildStep, Owner, fold, nowFolded));
            Assert.AreEqual(PredictionPhase.Pending, FeelRules.Judge(PredictionKind.BuildStep, Owner, paper, nowFolded),
                "the fold arriving never confirms the paper");
            Assert.AreEqual(PredictionPhase.Confirmed, FeelRules.Judge(PredictionKind.BuildStep, Owner, paper, nowWrapped));
            Assert.AreEqual(PredictionPhase.Denied, FeelRules.Judge(PredictionKind.BuildStep, Owner, paper, ItemSnapshot.Gone));

            // The view: each prediction its own token; the fold's end does not end the paper's look.
            var look = new BuildStepLook();
            byte synced = (byte)open;
            int foldToken = look.Predict((byte)folded, 10f);
            Assert.AreEqual((byte)folded, look.Seen(0f, synced));
            int paperToken = look.Predict((byte)wrapped, 10f);
            Assert.AreNotEqual(foldToken, paperToken);
            synced = (byte)folded; // the fold arrives
            look.End(foldToken);
            Assert.AreEqual((byte)wrapped, look.Seen(0.1f, synced), "still wrapped: no wrapped, folded, wrapped flicker");
            synced = (byte)wrapped;
            look.End(paperToken);
            Assert.IsFalse(look.Predicting);
            Assert.AreEqual((byte)wrapped, look.Seen(0.2f, synced));
            // A prediction past its time ends by itself.
            look.Predict((byte)folded, 1f);
            Assert.AreEqual(synced, look.Seen(1.5f, synced));
        }

        [Test]
        public void TheAnswerBook_TellsWhichRequestWasAnswered()
        {
            var book = new PredictionBook<object>();
            int first = book.Sent(0f);
            int second = book.Sent(0.3f);
            Assert.AreEqual(first, book.Answer(AnswerKind.Cook, true, 0.35f));
            Assert.AreEqual(second, book.Answer(AnswerKind.Cook, false, 0.4f));
            Assert.AreEqual(0, book.Answer(AnswerKind.Cook, false, 0.5f), "nothing in flight: ignored");
        }

        // ---- 10: the held squeeze's timing and queue ----

        [Test]
        public void TheHeldSqueeze_StopsAsLateAsItStarted_SoTheServerMeasuresTheHold()
        {
            var hold = new SqueezeHold();
            Assert.IsTrue(hold.Press(Board, 0, 10.0));
            Assert.AreEqual(SqueezeHold.Send.None, hold.Poll(false, 10.1));
            Assert.AreEqual(SqueezeHold.Send.Start, hold.Poll(true, 10.25), "the start went out 0.25 s late");
            Assert.AreEqual(0.25, hold.Lag, 1e-9);
            hold.Release(11.0);
            Assert.AreEqual(SqueezeHold.Send.None, hold.Poll(true, 11.1), "the stop waits as long");
            Assert.AreEqual(SqueezeHold.Send.Stop, hold.Poll(true, 11.25));
            Assert.IsTrue(hold.Idle);
            // Server side: start at 10.25, stop at 11.25: the 1.0 s that was held.
        }

        [Test]
        public void APressOnTheOtherDispenser_WhileTheFirstStopWaits_IsQueued()
        {
            var hold = new SqueezeHold();
            hold.Press(Board, 0, 0.0);
            Assert.AreEqual(SqueezeHold.Send.Start, hold.Poll(true, 0.0));
            hold.Release(0.6);
            Assert.IsTrue(hold.Press(Board, 1, 0.65), "queued, not dropped");
            Assert.IsTrue(hold.Queued);
            Assert.IsTrue(hold.Held, "the look follows the new press at once");
            Assert.IsTrue(hold.Involves(Board, 0));
            Assert.IsTrue(hold.Involves(Board, 1));
            Assert.AreEqual(SqueezeHold.Send.None, hold.Poll(false, 0.7), "inside the request interval");
            Assert.AreEqual(SqueezeHold.Send.Stop, hold.Poll(true, 0.8));
            Assert.AreEqual((Board, 0), (hold.SendBoard, hold.SendDispenser));
            Assert.AreEqual(SqueezeHold.Send.Start, hold.Poll(true, 1.05));
            Assert.AreEqual((Board, 1), (hold.SendBoard, hold.SendDispenser));
            Assert.AreEqual(0.4, hold.Lag, 1e-9, "its own lag from its own press");
            hold.Release(1.5);
            Assert.AreEqual(SqueezeHold.Send.None, hold.Poll(true, 1.85));
            Assert.AreEqual(SqueezeHold.Send.Stop, hold.Poll(true, 1.95));
            Assert.IsTrue(hold.Idle);
            Assert.IsFalse(hold.Involves(Board, 1));
        }

        // ---- 10: the client offers only what the server allows ----

        [Test]
        public void TheDispenser_IsNotOffered_WhileSomeoneElseSqueezesIt()
        {
            byte open = BuildRules.Mode(BuildRules.CloseOpen, BuildRules.PackNone, true);
            byte busy = BuildRules.WithSqueezing(0, 0, true);
            AimTarget Tool(int tool, byte flags) => AimTarget.AssemblyTool(tool, flags, "garlic-herb sauce", 80, 2, open);
            Assert.AreEqual(InteractionAction.Squeeze, InteractionRules.ActionFor(Held.Nothing, Tool(0, 0)));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Nothing, Tool(0, busy)));
            Assert.AreEqual("Someone is using that sauce", InteractionRules.PromptFor(Held.Nothing, Tool(0, busy)));
            Assert.AreEqual(InteractionAction.Squeeze, InteractionRules.ActionFor(Held.Nothing, Tool(1, busy)), "the other one is free");
        }

        [Test]
        public void TheBoard_TakesBackOnlyABuild_AndOnlyOnAnEmptyBoard()
        {
            byte open = BuildRules.Mode(BuildRules.CloseOpen, BuildRules.PackNone, true);
            AimTarget Target(int layers) => AimTarget.Cookware(VesselKind.Assembly, layers, layers > 0 ? "wrap" : null, layers > 0 ? open : (byte)0);
            Held friesPlate = Held.Food("plate of golden fries", true);
            Held finished = Held.Food("falafel wrap in paper", true, build: true);
            Held openBuild = Held.Food("wrap with lettuce slices", false, build: true);
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(friesPlate, Target(0)), "a plate of fries is no build");
            Assert.AreEqual(InteractionAction.PutInto, InteractionRules.ActionFor(finished, Target(0)));
            Assert.AreEqual(InteractionAction.PutInto, InteractionRules.ActionFor(openBuild, Target(0)));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(openBuild, Target(2)), "no put prompt on a taken board");
            Assert.AreEqual("Take the other build off first", InteractionRules.PromptFor(openBuild, Target(2)));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(finished, Target(2)));
            Assert.IsTrue(Held.Pending(openBuild).IsBuild);

            // The server agrees: a plate of fries is refused, a build goes back only on an empty spot.
            KitchenState kitchen = BoardWithWrap();
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewBuildTake(Spot, out Item build));
            kitchen.Register(50, build);
            Assert.AreEqual(CookOutcome.VesselFull, kitchen.TryPutOnBuild(50, Spot), "the board is not empty");
            kitchen.CommitBuild(Spot, null);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutOnBuild(50, Spot));
        }
    }
}
