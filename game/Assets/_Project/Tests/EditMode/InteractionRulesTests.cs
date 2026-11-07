using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 020 AC2: the one rule for first person. Every row of the spec's table: held item + target gives the
    /// click's action and the prompt under the dot (or a grey reason).
    /// </summary>
    public class InteractionRulesTests
    {
        private static readonly Held Nothing = Held.Nothing;
        private static readonly Held Potato = Held.Food("potato", plated: false);
        private static readonly Held Plate = Held.Food("potato plate", plated: true);
        private static readonly Held Knife = Held.Knife;
        private static readonly Held Box = Held.Item("box");

        private static AimTarget Empty(StationKind kind) => AimTarget.OnStation(kind, 0, null);
        private static AimTarget With(StationKind kind, string food, int count = 1) => AimTarget.OnStation(kind, count, food);

        private static InteractionAction Act(Held held, AimTarget target) => InteractionRules.ActionFor(held, target);

        // ---- row 1: anything + button = Press ----

        [Test]
        public void Button_BeatsEverything()
        {
            AimTarget button = AimTarget.Button(StationRules.StoveSwitch, 0);
            foreach (Held held in new[] { Nothing, Potato, Plate, Knife, Box })
                Assert.AreEqual(InteractionAction.Press, Act(held, button), held.Name);
        }

        [Test]
        public void ButtonPrompts_SayWhatPressingDoes()
        {
            byte off = StationRules.StoveFlags(false, false);
            byte onSaute = StationRules.StoveFlags(true, true);
            Assert.AreEqual("Click · switch on", InteractionRules.PromptFor(Nothing, AimTarget.Button(StationRules.StoveSwitch, off)));
            Assert.AreEqual("Click · switch off", InteractionRules.PromptFor(Potato, AimTarget.Button(StationRules.StoveSwitch, onSaute)));
            Assert.AreEqual("Click · fry → sauté", InteractionRules.PromptFor(Nothing, AimTarget.Button(StationRules.PanMode, off)));
            Assert.AreEqual("Click · sauté → fry", InteractionRules.PromptFor(Nothing, AimTarget.Button(StationRules.PanMode, onSaute)));
        }

        // ---- row 2: knife + board with food = Cut ----

        [Test]
        public void KnifeAndBoardWithFood_IsCut()
        {
            Assert.AreEqual(InteractionAction.Cut, Act(Knife, With(StationKind.Board, "potato")));
            Assert.AreEqual("Drag down · cut   Right click · turn", InteractionRules.PromptFor(Knife, With(StationKind.Board, "potato")));
        }

        [Test]
        public void KnifeAndEmptyBoard_IsNone_WithAReason()
        {
            Assert.AreEqual(InteractionAction.None, Act(Knife, Empty(StationKind.Board)));
            Assert.AreEqual("Put food on the board to cut", InteractionRules.Reason(Knife, Empty(StationKind.Board)));
        }

        // ---- row 3: food + empty board = PutOnBoard ----

        [Test]
        public void FoodAndEmptyBoard_IsPutOnBoard()
        {
            Assert.AreEqual(InteractionAction.PutOnBoard, Act(Potato, Empty(StationKind.Board)));
            Assert.AreEqual("Click · put potato on the board", InteractionRules.PromptFor(Potato, Empty(StationKind.Board)));
        }

        [Test]
        public void FoodAndFullBoard_IsNone_WithAReason()
        {
            Assert.AreEqual(InteractionAction.None, Act(Potato, With(StationKind.Board, "apple")));
            Assert.AreEqual("potato can't go in there", InteractionRules.PromptFor(Potato, With(StationKind.Board, "apple")));
        }

        // ---- rows 4 and 5: food that fits + pan / pot / plating table / bowl = PutInto ----

        [Test]
        public void FoodThatFits_AndEmptyPot_IsPutInto()
        {
            Assert.AreEqual(InteractionAction.PutInto, Act(Potato, Empty(StationKind.Pot)));
            Assert.AreEqual("Click · put potato into the pot", InteractionRules.PromptFor(Potato, Empty(StationKind.Pot)));
            Assert.AreEqual(InteractionAction.PutInto, Act(Potato, Empty(StationKind.Pan)));
            Assert.AreEqual("Click · put potato into the pan", InteractionRules.PromptFor(Potato, Empty(StationKind.Pan)));
        }

        [Test]
        public void FoodThatFits_AndPlateOrBowl_IsPutInto()
        {
            Assert.AreEqual(InteractionAction.PutInto, Act(Potato, Empty(StationKind.PlatingTable)));
            Assert.AreEqual("Click · put potato on the plate", InteractionRules.PromptFor(Potato, Empty(StationKind.PlatingTable)));
            Assert.AreEqual(InteractionAction.PutInto, Act(Potato, With(StationKind.Bowl, "apple")));
            Assert.AreEqual("Click · put potato into the bowl", InteractionRules.PromptFor(Potato, With(StationKind.Bowl, "apple")));
        }

        [Test]
        public void PlateAndBowl_TakeUpToTheServersLimit()
        {
            int plateMax = AssemblyRules.MaxItems(StationKind.PlatingTable);
            Assert.AreEqual(InteractionAction.PutInto, Act(Potato, With(StationKind.PlatingTable, "x", plateMax - 1)));
            Assert.AreEqual(InteractionAction.None, Act(Potato, With(StationKind.PlatingTable, "x", plateMax)));
            int bowlMax = AssemblyRules.MaxItems(StationKind.Bowl);
            Assert.AreEqual(InteractionAction.None, Act(Potato, With(StationKind.Bowl, "x", bowlMax)));
        }

        // ---- row 6: food + counter = Sell ----

        [Test]
        public void FoodAndCounter_IsSell()
        {
            Assert.AreEqual(InteractionAction.Sell, Act(Plate, Empty(StationKind.Counter)));
            Assert.AreEqual("Click · sell potato plate", InteractionRules.PromptFor(Plate, Empty(StationKind.Counter)));
            // The counter plates unplated food itself (spec 009), so the server sells it too.
            Assert.AreEqual(InteractionAction.Sell, Act(Potato, Empty(StationKind.Counter)));
        }

        // ---- row 7: anything + free spot = Place ----

        [Test]
        public void KnifeAndFreeSpot_IsPlace()
        {
            Assert.AreEqual(InteractionAction.Place, Act(Knife, AimTarget.Spot(3)));
            Assert.AreEqual("Click · put knife here", InteractionRules.PromptFor(Knife, AimTarget.Spot(3)));
        }

        [Test]
        public void AnythingHeld_AndFreeSpot_IsPlace()
        {
            foreach (Held held in new[] { Potato, Plate, Knife, Box })
                Assert.AreEqual(InteractionAction.Place, Act(held, AimTarget.Spot(0)), held.Name);
            Assert.AreEqual("Click · put potato here", InteractionRules.PromptFor(Potato, AimTarget.Spot(0)));
        }

        [Test]
        public void FullSurface_IsNone_WithAReason_EmptyHandsNoPrompt()
        {
            AimTarget full = AimTarget.Spot(PlaceSpots.None);
            Assert.AreEqual(InteractionAction.None, Act(Potato, full));
            Assert.AreEqual("No room here", InteractionRules.PromptFor(Potato, full));
            Assert.AreEqual(InteractionAction.None, Act(Nothing, AimTarget.Spot(2)));
            Assert.IsNull(InteractionRules.PromptFor(Nothing, AimTarget.Spot(2)));
        }

        // ---- row 8: nothing + free item = PickUp ----

        [Test]
        public void EmptyHands_AndFreeItem_IsPickUp()
        {
            Assert.AreEqual(InteractionAction.PickUp, Act(Nothing, AimTarget.Item("knife")));
            Assert.AreEqual("Click · pick up knife", InteractionRules.PromptFor(Nothing, AimTarget.Item("knife")));
            Assert.AreEqual("Click · pick up potato", InteractionRules.PromptFor(Nothing, AimTarget.Item("potato")));
        }

        [Test]
        public void FullHands_AndItem_IsNone()
        {
            Assert.AreEqual(InteractionAction.None, Act(Potato, AimTarget.Item("knife")));
            Assert.AreEqual(InteractionAction.None, Act(Knife, AimTarget.Item("potato")));
        }

        // ---- row 9: nothing + crate = Buy ----

        [Test]
        public void EmptyHands_AndCrate_IsBuy_WithThePrice()
        {
            Assert.AreEqual(InteractionAction.Buy, Act(Nothing, AimTarget.Crate("potato", 60)));
            Assert.AreEqual("Click · take potato ($60)", InteractionRules.PromptFor(Nothing, AimTarget.Crate("potato", 60)));
            Assert.AreEqual("Click · take potato", InteractionRules.PromptFor(Nothing, AimTarget.Crate("potato", -1)));
            Assert.AreEqual(InteractionAction.None, Act(Potato, AimTarget.Crate("potato", 60)), "hands full");
        }

        // ---- row 10: nothing + station with food = Take ----

        [Test]
        public void EmptyHands_AndPanWithFood_IsTake()
        {
            AimTarget pan = With(StationKind.Pan, "potato sticks · medium");
            Assert.AreEqual(InteractionAction.Take, Act(Nothing, pan));
            Assert.AreEqual("Click · take potato sticks · medium", InteractionRules.PromptFor(Nothing, pan));
        }

        [Test]
        public void EmptyHands_TakeFromEveryFoodStation()
        {
            foreach (StationKind kind in new[] { StationKind.Pan, StationKind.Pot, StationKind.Board, StationKind.PlatingTable, StationKind.Bowl })
            {
                Assert.AreEqual(InteractionAction.Take, Act(Nothing, With(kind, "potato")), kind.ToString());
                Assert.AreEqual(InteractionAction.None, Act(Nothing, Empty(kind)), kind + " empty");
                Assert.IsNull(InteractionRules.PromptFor(Nothing, Empty(kind)), kind + " empty");
            }
            Assert.AreEqual("Click · take the plate", InteractionRules.PromptFor(Nothing, With(StationKind.PlatingTable, "potato")));
            Assert.AreEqual("Click · take the mix", InteractionRules.PromptFor(Nothing, With(StationKind.Bowl, "potato", 2)));
            Assert.AreEqual("Click · take potato", InteractionRules.PromptFor(Nothing, With(StationKind.Bowl, "potato", 1)));
        }

        // ---- row 11: food that does not fit + pan / pot / plate / bowl = None with a reason ----

        [Test]
        public void PlatedFoodAndPan_IsNone_WithAReason()
        {
            Assert.AreEqual(InteractionAction.None, Act(Plate, Empty(StationKind.Pan)));
            Assert.AreEqual("potato plate can't go in there", InteractionRules.PromptFor(Plate, Empty(StationKind.Pan)));
            Assert.IsFalse(InteractionRules.Fits(Plate, Empty(StationKind.Pan)));
        }

        [Test]
        public void FoodThatDoesNotFit_IsNone_WithAReason()
        {
            Assert.AreEqual(InteractionAction.None, Act(Potato, With(StationKind.Pot, "apple")), "a full pot takes nothing more");
            Assert.IsNotNull(InteractionRules.Reason(Potato, With(StationKind.Pot, "apple")));
            foreach (StationKind kind in new[] { StationKind.Pot, StationKind.PlatingTable, StationKind.Bowl, StationKind.Board })
            {
                Assert.AreEqual(InteractionAction.None, Act(Plate, Empty(kind)), kind.ToString());
                Assert.AreEqual("potato plate can't go in there", InteractionRules.Reason(Plate, Empty(kind)), kind.ToString());
            }
        }

        // ---- spec 020 review: beyond reach ----

        [Test]
        public void TooFar_IsNone_WithTheReasonTooFar()
        {
            AimTarget farPot = Empty(StationKind.Pot).TooFar();
            Assert.IsTrue(farPot.IsTooFar);
            Assert.AreEqual(InteractionAction.None, Act(Potato, farPot));
            Assert.AreEqual("Too far", InteractionRules.PromptFor(Potato, farPot));
            Assert.AreEqual("Too far", InteractionRules.PromptFor(Nothing, AimTarget.Button(StationRules.StoveSwitch, 0).TooFar()));
            Assert.AreEqual("Too far", InteractionRules.PromptFor(Nothing, AimTarget.Item("knife").TooFar()));
            Assert.AreEqual("Too far", InteractionRules.PromptFor(Knife, AimTarget.Spot(2).TooFar()));
            Assert.AreEqual("Too far", InteractionRules.PromptFor(Plate, Empty(StationKind.Pan).TooFar()), "would be grey anyway");
        }

        [Test]
        public void TooFar_WhereNothingWouldHappen_StaysSilent()
        {
            Assert.IsNull(InteractionRules.PromptFor(Nothing, Empty(StationKind.Pan).TooFar()), "empty hands, empty pan");
            Assert.IsNull(InteractionRules.PromptFor(Nothing, AimTarget.Spot(2).TooFar()), "empty hands, worktop");
            Assert.IsFalse(Empty(StationKind.Pot).TooFar().InReach().IsTooFar);
            Assert.AreEqual(InteractionAction.PutInto, Act(Potato, Empty(StationKind.Pot).TooFar().InReach()));
        }

        // ---- row 12: anything else = None, no prompt ----

        [Test]
        public void AnythingElse_IsNone_WithoutPrompt()
        {
            Assert.AreEqual(InteractionAction.None, Act(Nothing, AimTarget.Nothing));
            Assert.IsNull(InteractionRules.PromptFor(Nothing, AimTarget.Nothing));
            Assert.IsNull(InteractionRules.PromptFor(Potato, AimTarget.Nothing));
            Assert.IsNull(InteractionRules.PromptFor(Nothing, Empty(StationKind.Counter)), "empty hands at the counter");
            Assert.IsNull(InteractionRules.PromptFor(Knife, Empty(StationKind.Pan)), "a knife in the pan");
            Assert.IsNull(InteractionRules.PromptFor(Box, Empty(StationKind.Pot)), "a box in the pot");
            Assert.AreEqual(InteractionAction.None, Act(Box, Empty(StationKind.Counter)));
        }

        [Test]
        public void EveryActionHasAPrompt_AndOnlyNoneIsGrey()
        {
            var cases = new (Held, AimTarget)[]
            {
                (Nothing, AimTarget.Button(StationRules.StoveSwitch, 0)), (Knife, With(StationKind.Board, "potato")),
                (Potato, Empty(StationKind.Board)), (Potato, Empty(StationKind.Pot)), (Plate, Empty(StationKind.Counter)),
                (Knife, AimTarget.Spot(1)), (Nothing, AimTarget.Item("knife")), (Nothing, AimTarget.Crate("potato", 60)),
                (Nothing, With(StationKind.Pan, "potato"))
            };
            foreach ((Held held, AimTarget target) in cases)
            {
                Assert.AreNotEqual(InteractionAction.None, Act(held, target));
                Assert.IsNull(InteractionRules.Reason(held, target), "an action has no reason");
                string prompt = InteractionRules.PromptFor(held, target);
                Assert.IsNotEmpty(prompt);
                StringAssert.DoesNotContain("E ·", prompt, "first person has no E");
                if (!prompt.Contains("$"))
                    foreach (char c in prompt) Assert.IsFalse(char.IsDigit(c), prompt);
            }
        }
    }
}
