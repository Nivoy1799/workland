using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Net.Rules;
using Worktown.Game.Npc;
using Worktown.Game.Shop;
using Worktown.Game.Tutorial;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 038 AC2 and AC4: the steps' predicates over snapshots, order and adaptivity, skip and replay, calm guests on a host only.</summary>
    public class TutorialTests
    {
        private static TutorialSnapshot Empty => new TutorialSnapshot();

        private static TutorialSnapshot Holding(Held held) => new TutorialSnapshot { Held = held };

        private static TutorialStep Step(TutorialStepId id)
        {
            foreach (TutorialStep s in TutorialSteps.Default)
                if (s.Id == id) return s;
            Assert.Fail("no step " + id);
            return null;
        }

        private static readonly Held Potato = Held.Food("potato", false);
        private static readonly Held Sticks = Held.Food("potato sticks", false);
        private static readonly Held FriesPlate = Held.Cookware(VesselKind.Plate, 1, "fries");
        private static readonly Held Lemonade = Held.Cup("lemonade to go", 0, DrinkRules.LevelRight, true);
        private static readonly Held OpenLemonade = Held.Cup("brimming cup of lemonade", 0, DrinkRules.LevelRight, false);
        private static readonly Held EmptyCup = Held.Cup("empty cup", -1, DrinkRules.LevelEmpty, false);

        /// <summary>The board's synced name is the take name (one entry), here the dish it matches.</summary>
        private static IReadOnlyList<string> FullBuild => new[] { "Falafel wrap" };

        /// <summary>A snapshot on which every step up to and including the done card's predecessor holds (a player far ahead).</summary>
        private static TutorialSnapshot Everything => new TutorialSnapshot
        {
            Moved = 10f,
            Turned = 400f,
            Held = Lemonade,
            BasketTitle = "fries",
            BasketMode = FryerRules.Mode(BasketState.Raised, 1),
            BuildLayers = FullBuild, BuildCount = 4,
            BuildMode = BuildRules.Mode(BuildRules.CloseWrapped, BuildRules.PackNone, true),
            Events = TutorialEvents.Served | TutorialEvents.Paid | TutorialEvents.Scrubbed | TutorialEvents.Ordered | TutorialEvents.Shaved,
        };

        [Test]
        public void TheDefaultShift_HasFourteenSteps_InTheSpecsOrder_TheSpitOptional()
        {
            IReadOnlyList<TutorialStep> steps = TutorialSteps.Default;
            Assert.AreEqual(14, steps.Count);
            for (int i = 0; i < steps.Count; i++) Assert.AreEqual((TutorialStepId)i, steps[i].Id, "step " + (i + 1));
            foreach (TutorialStep s in steps)
            {
                Assert.IsFalse(string.IsNullOrEmpty(s.Text));
                Assert.LessOrEqual(s.Text.Length, 140, s.Id + ": a short instruction, not a text wall");
                Assert.AreEqual(s.Id == TutorialStepId.ShaveSpit, s.Optional, s.Id + ": only the spit is optional");
            }
            Assert.IsFalse(Step(TutorialStepId.Done).IsDone(Everything), "the done card has no predicate");
        }

        [Test]
        public void EachPredicate_FiresOnItsSnapshot_AndNotOnAnEmptyOne()
        {
            var cases = new (TutorialStepId Id, TutorialSnapshot Yes)[]
            {
                (TutorialStepId.MoveAndLook, new TutorialSnapshot { Moved = 3.5f, Turned = 120f }),
                (TutorialStepId.TakePotato, Holding(Potato)),
                (TutorialStepId.CutSticks, new TutorialSnapshot { BoardTitle = "potato sticks" }),
                (TutorialStepId.FrySticks, new TutorialSnapshot { BasketTitle = "fries", BasketMode = FryerRules.Mode(BasketState.Draining, 1) }),
                (TutorialStepId.PlateFries, Holding(FriesPlate)),
                (TutorialStepId.BuildWrap, new TutorialSnapshot { BuildLayers = FullBuild, BuildCount = 4, BuildMode = BuildRules.Mode(BuildRules.CloseOpen, BuildRules.PackNone, true) }),
                (TutorialStepId.FoldAndWrap, new TutorialSnapshot { BuildLayers = FullBuild, BuildCount = 4, BuildMode = BuildRules.Mode(BuildRules.CloseWrapped, BuildRules.PackNone, true) }),
                (TutorialStepId.ServeGuest, new TutorialSnapshot { Events = TutorialEvents.Served }),
                (TutorialStepId.TakePayment, new TutorialSnapshot { Events = TutorialEvents.Paid }),
                (TutorialStepId.FillLemonade, Holding(Lemonade)),
                (TutorialStepId.WashPlate, new TutorialSnapshot { Events = TutorialEvents.Scrubbed }),
                (TutorialStepId.OrderStock, new TutorialSnapshot { Events = TutorialEvents.Ordered }),
                (TutorialStepId.ShaveSpit, new TutorialSnapshot { Events = TutorialEvents.Shaved }),
            };
            foreach (var c in cases)
            {
                Assert.IsTrue(Step(c.Id).IsDone(c.Yes), c.Id + " fires on its snapshot");
                Assert.IsFalse(Step(c.Id).IsDone(Empty), c.Id + " does not fire on an empty snapshot");
            }
        }

        [Test]
        public void ThePredicates_RefuseTheNearMisses()
        {
            Assert.IsFalse(TutorialSteps.Moved(new TutorialSnapshot { Moved = 5f, Turned = 10f }), "walked but did not look around");
            Assert.IsFalse(TutorialSteps.Moved(new TutorialSnapshot { Moved = 1f, Turned = 180f }), "looked but did not walk");
            Assert.IsFalse(TutorialSteps.HasPotato(Holding(Held.Food("onion", false))));
            Assert.IsFalse(TutorialSteps.HasSticks(Holding(Potato)), "a whole potato is not cut");
            Assert.IsFalse(TutorialSteps.HasSticks(new TutorialSnapshot { BoardTitle = "potato slices" }), "slices are not sticks");
            Assert.IsFalse(TutorialSteps.HasFries(new TutorialSnapshot { BasketTitle = "fries", BasketMode = FryerRules.Mode(BasketState.Lowered, 0) }),
                "still in the oil");
            Assert.IsFalse(TutorialSteps.HasFries(new TutorialSnapshot { BasketTitle = "potato sticks" }), "not fried yet");
            Assert.IsFalse(TutorialSteps.HasPlatedFries(Holding(Held.Cookware(VesselKind.Plate, 0, null))), "an empty plate");
            Assert.IsFalse(TutorialSteps.HasPlatedFries(Holding(Held.Cookware(VesselKind.Bowl, 1, "fries"))), "a bowl is no plate");
            Assert.IsFalse(TutorialSteps.HasBuild(new TutorialSnapshot { BuildLayers = new[] { "wrap with falafel balls and lettuce slices" }, BuildCount = 3 }), "no sauce: three layers");
            Assert.IsFalse(TutorialSteps.HasBuild(new TutorialSnapshot { BuildLayers = new[] { "wrap with lettuce slices and tomato slices and garlic-herb sauce" }, BuildCount = 4 }), "no falafel");
            Assert.IsFalse(TutorialSteps.HasWrapped(new TutorialSnapshot { BuildLayers = FullBuild, BuildCount = 4, BuildMode = BuildRules.Mode(BuildRules.CloseFolded, BuildRules.PackNone, true) }),
                "folded is not wrapped");
            Assert.IsFalse(TutorialSteps.HasLemonade(Holding(OpenLemonade)), "no lid yet");
            Assert.IsFalse(TutorialSteps.HasLemonade(Holding(EmptyCup)));
            Assert.IsFalse(TutorialSteps.HasServed(new TutorialSnapshot { Events = TutorialEvents.Paid }), "events are told apart");
            Assert.IsFalse(TutorialSteps.HasPaid(new TutorialSnapshot { Events = TutorialEvents.Served }));
        }

        [Test]
        public void ThePredicates_HoldForLaterEvidence_SoAPlayerAheadIsNeverSentBack()
        {
            var sticksInBasket = new TutorialSnapshot { BasketTitle = "potato sticks" };
            Assert.IsTrue(TutorialSteps.HasPotato(sticksInBasket), "sticks in the basket: the potato was taken");
            Assert.IsTrue(TutorialSteps.HasSticks(sticksInBasket));
            Assert.IsTrue(TutorialSteps.HasSticks(Holding(FriesPlate)), "fries on a plate: it was cut and fried");
            Assert.IsTrue(TutorialSteps.HasFries(Holding(FriesPlate)));
            Assert.IsTrue(TutorialSteps.HasBuild(Holding(Held.Food("falafel wrap", true, build: true))), "a wrapped falafel wrap in the hands");
        }

        [Test]
        public void TheTargets_FollowWhatThePlayerHasNow()
        {
            Assert.AreEqual(TutorialTarget.Knife, TutorialSteps.CutTarget(new TutorialSnapshot { BoardTitle = "potato" }), "the potato is on the board: the knife");
            Assert.AreEqual(TutorialTarget.CuttingBoard, TutorialSteps.CutTarget(Holding(Potato)));
            Assert.AreEqual(TutorialTarget.CuttingBoard, TutorialSteps.CutTarget(Holding(Held.Knife)));
            Assert.AreEqual(TutorialTarget.FryerPower, TutorialSteps.FryTarget(Holding(Sticks)), "fryer off: its power knob");
            var on = Holding(Sticks);
            on.FryerOn = true;
            Assert.AreEqual(TutorialTarget.FryerBasket, TutorialSteps.FryTarget(on));
            Assert.AreEqual(TutorialTarget.FryerButton, TutorialSteps.FryTarget(new TutorialSnapshot { BasketTitle = "potato sticks", FryerOn = true }), "lower it");
            Assert.AreEqual(TutorialTarget.FryerBasket,
                TutorialSteps.FryTarget(new TutorialSnapshot { BasketTitle = "potato sticks", BasketMode = FryerRules.Mode(BasketState.Lowered, 0), BasketZone = HeatZone.Good }),
                "shake it while it fries");
            Assert.AreEqual(TutorialTarget.FryerButton,
                TutorialSteps.FryTarget(new TutorialSnapshot { BasketTitle = "fries", BasketMode = FryerRules.Mode(BasketState.Lowered, 1), BasketZone = HeatZone.Perfect }),
                "raise it at perfect");
            Assert.AreEqual(TutorialTarget.Plate, TutorialSteps.PlateTarget(Empty));
            Assert.AreEqual(TutorialTarget.FryerBasket, TutorialSteps.PlateTarget(Holding(Held.Cookware(VesselKind.Plate, 0, null))));
            Assert.AreEqual(TutorialTarget.WrapSlot, TutorialSteps.BuildTarget(Empty));
            Assert.AreEqual(TutorialTarget.AssemblyBoard, TutorialSteps.BuildTarget(Holding(Held.Food("wrap", false))));
            Assert.AreEqual(TutorialTarget.FalafelSlot, TutorialSteps.BuildTarget(new TutorialSnapshot { BuildLayers = new[] { "wrap" }, BuildCount = 1 }));
            Assert.AreEqual(TutorialTarget.LettuceSlot, TutorialSteps.BuildTarget(new TutorialSnapshot { BuildLayers = new[] { "wrap with falafel balls" }, BuildCount = 2 }));
            Assert.AreEqual(TutorialTarget.Sauce, TutorialSteps.BuildTarget(new TutorialSnapshot { BuildLayers = new[] { "wrap with falafel balls and lettuce slices" }, BuildCount = 3 }));
            Assert.AreEqual(TutorialTarget.AssemblyBoard, TutorialSteps.FoldTarget(new TutorialSnapshot { BuildLayers = FullBuild, BuildCount = 4, BuildMode = BuildRules.Mode(0, 0, true) }));
            Assert.AreEqual(TutorialTarget.WrapPaper, TutorialSteps.FoldTarget(new TutorialSnapshot { BuildLayers = FullBuild, BuildCount = 4, BuildMode = BuildRules.Mode(BuildRules.CloseFolded, 0, true) }));
            Assert.AreEqual(TutorialTarget.CupStack, TutorialSteps.LemonadeTarget(Empty));
            Assert.AreEqual(TutorialTarget.LemonadeTap, TutorialSteps.LemonadeTarget(Holding(EmptyCup)));
            Assert.AreEqual(TutorialTarget.LidStack, TutorialSteps.LemonadeTarget(Holding(OpenLemonade)));
            // Spec 042: the cup on the lemonade tap's tray: hold the tap until the line, the lids, then take the lidded cup.
            TutorialSnapshot OnTray(byte level, bool lid) => new TutorialSnapshot { LemonadeSpot = DrinkRules.SpotByte(true, lid, 0, level) };
            Assert.AreEqual(TutorialTarget.LemonadeTap, TutorialSteps.LemonadeTarget(OnTray(DrinkRules.LevelEmpty, false)));
            Assert.AreEqual(TutorialTarget.LemonadeTap, TutorialSteps.LemonadeTarget(OnTray(DrinkRules.LevelLow, false)), "not full yet");
            Assert.AreEqual(TutorialTarget.LidStack, TutorialSteps.LemonadeTarget(OnTray(DrinkRules.LevelRight, false)));
            Assert.AreEqual(TutorialTarget.LemonadeCup, TutorialSteps.LemonadeTarget(OnTray(DrinkRules.LevelRight, true)));
            Assert.IsFalse(TutorialSteps.HasLemonade(OnTray(DrinkRules.LevelRight, true)), "done once it is in the hands");
            StringAssert.Contains("tray", TutorialSteps.Default[(int)TutorialStepId.FillLemonade].Text);
            Assert.AreEqual(TutorialTarget.DishReturn, TutorialSteps.WashTarget(Empty));
            Assert.AreEqual(TutorialTarget.Sink, TutorialSteps.WashTarget(Holding(Held.DirtyDish)));
            Assert.AreEqual(TutorialTarget.SpitCone, TutorialSteps.SpitTarget(Empty), "a bare spit: load a cone");
            Assert.AreEqual(TutorialTarget.SpitKnob, TutorialSteps.SpitTarget(new TutorialSnapshot { SpitFlags = SpitRules.Flags(Spit.Off, false, true) }));
            Assert.AreEqual(TutorialTarget.Knife, TutorialSteps.SpitTarget(new TutorialSnapshot { SpitFlags = SpitRules.Flags(Spit.High, false, true) }));
            var knife = Holding(Held.Knife);
            knife.SpitFlags = SpitRules.Flags(Spit.High, false, true);
            Assert.AreEqual(TutorialTarget.SpitCone, TutorialSteps.SpitTarget(knife));
        }

        [Test]
        public void Progress_GoesInOrder_OneStepAtATime()
        {
            var p = new TutorialProgress(TutorialSteps.Default);
            Assert.IsFalse(p.Running);
            Assert.IsNull(p.CurrentStep, "no card before it starts");
            p.Start();
            Assert.AreEqual(0, p.Current);
            Assert.IsFalse(p.Observe(Empty));
            Assert.IsFalse(p.Observe(Holding(Potato)), "step 2's state does not pass step 1");
            Assert.AreEqual(TutorialStepId.MoveAndLook, p.CurrentStep.Id);
            Assert.IsTrue(p.Observe(new TutorialSnapshot { Moved = 4f, Turned = 100f }));
            Assert.AreEqual(TutorialStepId.CutSticks, p.CurrentStep.Id, "the potato held during step 1 was remembered: step 2 completes at once");
            Assert.IsFalse(p.Observe(new TutorialSnapshot { Moved = 4f, Turned = 100f }), "step 3 waits for the sticks");
        }

        [Test]
        public void Progress_Adapts_AStepDoneAheadCompletesAtOnce()
        {
            var p = new TutorialProgress(TutorialSteps.Default);
            p.Start();
            p.Observe(new TutorialSnapshot { Moved = 4f, Turned = 100f });
            // The player already holds fries on a plate: take, cut, fry and plate all complete at once.
            Assert.IsTrue(p.Observe(Holding(FriesPlate)));
            Assert.AreEqual(TutorialStepId.BuildWrap, p.CurrentStep.Id);
            // A sale and a payment happen before the wrap: remembered, so steps 8 and 9 complete when they come.
            p.Observe(new TutorialSnapshot { Events = TutorialEvents.Served | TutorialEvents.Paid });
            Assert.AreEqual(TutorialStepId.BuildWrap, p.CurrentStep.Id, "still the wrap");
            Assert.IsTrue(p.WasDone((int)TutorialStepId.ServeGuest) && p.WasDone((int)TutorialStepId.TakePayment));
            p.Observe(new TutorialSnapshot { BuildLayers = FullBuild, BuildCount = 4, BuildMode = BuildRules.Mode(BuildRules.CloseWrapped, 0, true) });
            Assert.AreEqual(TutorialStepId.FillLemonade, p.CurrentStep.Id, "wrapped, and served and paid before: straight to the lemonade");
            // Far ahead: everything at once lands on the done card.
            p.Observe(Everything);
            Assert.IsTrue(p.AtEnd);
            Assert.AreEqual(TutorialStepId.Done, p.CurrentStep.Id);
            Assert.IsTrue(p.Running, "the done card shows until it is closed");
            p.Finish();
            Assert.IsFalse(p.Running);
            Assert.IsFalse(p.Skipped);
        }

        [Test]
        public void Skip_EndsItAnyTime_SkipStepOnlyForTheOptional_ReplayStartsOver()
        {
            var p = new TutorialProgress(TutorialSteps.Default);
            p.Start();
            Assert.IsFalse(p.SkipStep(), "a required step is not skipped on its own");
            p.SkipAll();
            Assert.IsFalse(p.Running);
            Assert.IsTrue(p.Skipped);
            Assert.IsNull(p.CurrentStep, "the card is gone");
            Assert.IsFalse(p.Observe(Everything), "nothing moves once skipped");

            p.Start(); // replay
            Assert.IsTrue(p.Running);
            Assert.IsFalse(p.Skipped);
            Assert.AreEqual(0, p.Current, "from the first step");
            Assert.IsFalse(p.WasDone(1), "nothing is remembered from before");
            p.Observe(new TutorialSnapshot { Moved = 4f, Turned = 100f });
            p.Observe(Holding(FriesPlate));
            Assert.AreEqual(TutorialStepId.BuildWrap, p.CurrentStep.Id);
            p.Observe(Everything);
            Assert.IsTrue(p.AtEnd);

            var q = new TutorialProgress(TutorialSteps.Default);
            q.Start();
            q.Observe(new TutorialSnapshot { Moved = 4f, Turned = 100f });
            q.Observe(Holding(FriesPlate));
            var toSpit = Everything;
            toSpit.Events &= ~TutorialEvents.Shaved;
            toSpit.Held = Lemonade;
            q.Observe(toSpit);
            Assert.AreEqual(TutorialStepId.ShaveSpit, q.CurrentStep.Id);
            Assert.IsTrue(q.SkipStep(), "the spit may be skipped");
            Assert.IsTrue(q.AtEnd);
            Assert.IsFalse(q.SkipStep(), "the done card is closed, not skipped");
        }

        [Test]
        public void ThePanel_IsAbsent_WhenTheTutorialIsDone_OrSkipped()
        {
            Assert.IsFalse(TutorialRules.StartsOnLaunch(new string[0], true, false), "done once: it does not start by itself");
            Assert.IsTrue(TutorialRules.StartsOnLaunch(new string[0], false, false), "a first start shows it");
            Assert.IsTrue(TutorialRules.StartsOnLaunch(new[] { TutorialRules.Arg }, true, false), "-tutorial forces it");
            Assert.IsFalse(TutorialRules.StartsOnLaunch(new[] { TutorialRules.SkipArg }, false, false));
            Assert.IsFalse(TutorialRules.StartsOnLaunch(new[] { "-skip-whatsnew" }, false, true), "the scripted dev runs stay clean");
            Assert.IsFalse(TutorialRules.StartsOnLaunch(new[] { "-observe", "cook" }, false, true));
            Assert.IsTrue(TutorialRules.StartsOnLaunch(new[] { "-skip-whatsnew" }, false, false), "a release build has no scripted runs");
            var p = new TutorialProgress(TutorialSteps.Default);
            Assert.IsFalse(TutorialRules.ShowsCard(p.Running, false));
            p.Start();
            Assert.IsTrue(TutorialRules.ShowsCard(p.Running, false));
            Assert.IsFalse(TutorialRules.ShowsCard(p.Running, true), "hidden under a menu or the banner");
            p.Observe(Everything);
            p.Finish();
            Assert.IsFalse(TutorialRules.ShowsCard(p.Running, false), "absent when done");
            Assert.AreEqual("3 / 14", TutorialRules.ProgressText(2, 14));
        }

        [Test]
        public void TheWatch_TurnsServerAnswers_IntoEvents_ForTheActionSeen()
        {
            var w = new TutorialWatch();
            Assert.AreEqual(TutorialEvents.None, w.Observe(5, true, 0, 0, 0f), "the first look only learns the counters");
            w.SawAction(InteractionAction.Sell, 0, 1f);
            w.SawAction(InteractionAction.None, 0, 1.1f);
            Assert.AreEqual(TutorialEvents.Served, w.Observe(6, true, 0, 0, 1.3f), "an ok right after a sale");
            Assert.AreEqual(TutorialEvents.None, w.Observe(6, true, 0, 0, 1.4f), "the same answer counts once");
            w.SawAction(InteractionAction.Sell, 0, 2f);
            Assert.AreEqual(TutorialEvents.None, w.Observe(7, false, 0, 0, 2.1f), "a refusal is no sale");
            Assert.AreEqual(TutorialEvents.None, w.Observe(8, true, 0, 0, 9f), "too long after the aim");
            w.SawAction(InteractionAction.Pay, 3, 10f);
            Assert.AreEqual(TutorialEvents.None, w.Observe(9, true, 0, 0, 10.1f), "a coin into the change is not the payment");
            w.SawAction(InteractionAction.Pay, RegisterRules.GiveChangeOption, 11f);
            Assert.AreEqual(TutorialEvents.Paid, w.Observe(10, true, 0, 0, 11.1f));
            Assert.AreEqual(TutorialEvents.Paid, TutorialWatch.EventOf(InteractionAction.Pay, RegisterRules.CardOption));
            Assert.AreEqual(TutorialEvents.Ordered, TutorialWatch.EventOf(InteractionAction.Order, 2));
            // Holds: the sink's basin goes down after a scrub, the spit's tray up after a shave.
            w.Observe(10, true, 1, 0, 12f);
            w.SawAction(InteractionAction.Scrub, 0, 12.2f);
            Assert.AreEqual(TutorialEvents.Scrubbed, w.Observe(10, true, 0, 0, 13.8f));
            w.SawAction(InteractionAction.Shave, 0, 14f);
            Assert.AreEqual(TutorialEvents.Shaved, w.Observe(10, true, 0, 2, 16f));
            w.SawAction(InteractionAction.Take, 0, 17f);
            Assert.AreEqual(TutorialEvents.None, w.Observe(10, true, 0, 1, 17.1f), "a scoop from the tray is no shave");
        }

        [Test]
        public void CalmGuests_OnlyOnAHostWhoseTutorialRuns_NeverOnADedicatedServer()
        {
            Assert.IsTrue(TutorialRules.HostCalms(false, true, true, true), "a host with its tutorial running");
            Assert.IsFalse(TutorialRules.HostCalms(false, true, true, false), "the tutorial ended");
            Assert.IsFalse(TutorialRules.HostCalms(false, true, false, true), "a server without a local client (-server)");
            Assert.IsFalse(TutorialRules.HostCalms(true, true, true, true), "a server build never");
            Assert.IsFalse(TutorialRules.HostCalms(true, true, false, true), "the dedicated server, whatever a player does");
            Assert.AreEqual(3f, TutorialRules.PatienceScale(true, 1f));
            Assert.AreEqual(1f, TutorialRules.PatienceScale(false, 1f));
            Assert.AreEqual(24f, TutorialRules.PatienceScale(true, 8f), "the tutorial tour's host multiplies the tour's patience");
            Assert.IsTrue(TutorialRules.WaitsForLocalClient(false, true, false, 0.5f), "a host waits for its own client before the first guests");
            Assert.IsFalse(TutorialRules.WaitsForLocalClient(false, true, false, 5f), "but not for ever (server only from the menu)");
            Assert.IsFalse(TutorialRules.WaitsForLocalClient(true, true, false, 0f), "a server build never waits");
            Assert.IsFalse(TutorialRules.WaitsForLocalClient(false, false, false, 0f));
        }

        [Test]
        public void TheTutorialsGuests_OrderWhatItTeaches_FriesToEatIn_AndAWrapWithLemonade_BothCash()
        {
            var orders = new TutorialOrders();
            Assert.IsTrue(orders.TryNext("snack", out MenuDish fries, out Worktown.Sim.Economy.GuestOrder friesOrder, out string friesDrink));
            Assert.AreEqual(Menu.Fries, fries.Id);
            Assert.IsNull(friesDrink);
            Assert.IsFalse(friesOrder.PaysByCard, "cash: the change step");
            Assert.IsTrue(orders.LastEatIn, "eats in: its plate comes back for the wash step");
            Assert.IsTrue(orders.TryNext("meal", out MenuDish wrap, out Worktown.Sim.Economy.GuestOrder wrapOrder, out string drink));
            Assert.AreEqual(Menu.FalafelWrap, wrap.Id);
            Assert.AreEqual("lemonade", drink);
            Assert.IsFalse(wrapOrder.PaysByCard);
            Assert.AreEqual(0, wrapOrder.Modifiers.Count, "a plain wrap for a first shift");
            Assert.IsFalse(orders.LastEatIn);
            Assert.IsFalse(orders.TryNext("meal", out _, out _, out _), "later guests draw as usual");
            Assert.IsFalse(orders.LastScripted);
        }

        [Test]
        public void TheEdgeArrow_PointsTowardsTheTarget_FromTheBorder()
        {
            Assert.IsTrue(TutorialRules.IsOnScreen(new Vector3(0.5f, 0.5f, 3f)));
            Assert.IsFalse(TutorialRules.IsOnScreen(new Vector3(0.5f, 0.5f, -3f)), "behind the camera");
            Assert.IsFalse(TutorialRules.IsOnScreen(new Vector3(1.4f, 0.5f, 3f)));
            Vector2 right = TutorialRules.EdgePoint(new Vector3(2f, 0.5f, 3f), 0.06f, out float a);
            Assert.AreEqual(0.94f, right.x, 1e-4f);
            Assert.AreEqual(0.5f, right.y, 1e-4f);
            Assert.AreEqual(0f, a, 1e-3f, "points right");
            Vector2 behindLeft = TutorialRules.EdgePoint(new Vector3(0.8f, 0.5f, -2f), 0.06f, out float b);
            Assert.Less(behindLeft.x, 0.5f, "behind and to the right on the flipped image: turn left");
            Assert.AreEqual(180f, Mathf.Abs(b), 1e-3f);
            Vector2 below = TutorialRules.EdgePoint(new Vector3(0.5f, -1f, 2f), 0.06f, out float c);
            Assert.AreEqual(0.06f, below.y, 1e-4f);
            Assert.AreEqual(-90f, c, 1e-3f);
            for (float t = 0f; t < 2f; t += 0.05f) Assert.That(TutorialRules.Pulse01(t), Is.InRange(0f, 1f));
        }
    }
}
