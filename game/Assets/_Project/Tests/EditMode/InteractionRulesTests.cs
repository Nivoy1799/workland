using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 020 AC2, spec 021 AC6: the one rule for first person. Every row of the spec 021 table: held item + target
    /// gives the click's action and the prompt under the dot (or a grey reason).
    /// </summary>
    public class InteractionRulesTests
    {
        private static readonly Held Nothing = Held.Nothing;
        private static readonly Held Potato = Held.Food("potato", plated: false);
        private static readonly Held Plated = Held.Food("potato plate", plated: true);
        private static readonly Held Knife = Held.Knife;
        private static readonly Held Box = Held.Item("box");

        private static AimTarget Empty(VesselKind kind) => AimTarget.Cookware(kind, 0, null);
        private static AimTarget With(VesselKind kind, string food, int count = 1) => AimTarget.Cookware(kind, count, food);
        private static Held Holding(VesselKind kind, string food = null, int count = 0) => Held.Cookware(kind, count, food);

        private static InteractionAction Act(Held held, AimTarget target) => InteractionRules.ActionFor(held, target);

        // ---- row 1: anything + knob / button = Press ----

        [Test]
        public void Button_BeatsEverything()
        {
            AimTarget button = AimTarget.Button(StationRules.StoveOption(1, StationRules.StoveSwitch), 0);
            foreach (Held held in new[] { Nothing, Potato, Plated, Knife, Box, Holding(VesselKind.Pan, "potato", 1) })
                Assert.AreEqual(InteractionAction.Press, Act(held, button), held.Name);
            Assert.AreEqual("Click · burner on", InteractionRules.PromptFor(Nothing, button));
            Assert.AreEqual("Click · fry → sauté", InteractionRules.PromptFor(Nothing, AimTarget.Button(StationRules.StoveOption(0, StationRules.PanMode), 0)));
        }

        // ---- row 2: knife + board with food = Cut ----

        [Test]
        public void KnifeAndBoardWithFood_IsCut()
        {
            Assert.AreEqual(InteractionAction.Cut, Act(Knife, With(VesselKind.Board, "potato")));
            Assert.AreEqual("Drag down · cut   Right click · turn", InteractionRules.PromptFor(Knife, With(VesselKind.Board, "potato")));
        }

        [Test]
        public void KnifeAndEmptyBoard_IsNone_WithAReason_KnifeInAPan_Silent()
        {
            Assert.AreEqual(InteractionAction.None, Act(Knife, Empty(VesselKind.Board)));
            Assert.AreEqual("Put food on the board to cut", InteractionRules.Reason(Knife, Empty(VesselKind.Board)));
            Assert.AreEqual(InteractionAction.None, Act(Knife, With(VesselKind.Pan, "potato")));
            Assert.IsNull(InteractionRules.PromptFor(Knife, Empty(VesselKind.Pan)), "a knife in the pan");
        }

        // ---- row 3: food that fits + cookware with room = PutInto ----

        [Test]
        public void PotatoAndPotWithRoom_IsPutInto()
        {
            Assert.AreEqual(InteractionAction.PutInto, Act(Potato, Empty(VesselKind.Pot)));
            Assert.AreEqual("Click · put potato into the pot", InteractionRules.PromptFor(Potato, Empty(VesselKind.Pot)));
            Assert.AreEqual(InteractionAction.PutInto, Act(Potato, With(VesselKind.Pot, "onion", VesselRules.PotCapacity - 1)),
                "several ingredients in a pot");
        }

        [Test]
        public void FoodThatFits_IntoEveryKind()
        {
            Assert.AreEqual("Click · put potato on the board", InteractionRules.PromptFor(Potato, Empty(VesselKind.Board)));
            Assert.AreEqual("Click · put potato into the pan", InteractionRules.PromptFor(Potato, Empty(VesselKind.Pan)));
            Assert.AreEqual("Click · put potato on the plate", InteractionRules.PromptFor(Potato, With(VesselKind.Plate, "x")));
            Assert.AreEqual("Click · put potato into the bowl", InteractionRules.PromptFor(Potato, With(VesselKind.Bowl, "apple")));
        }

        [Test]
        public void EveryKind_TakesUpToTheServersCapacity()
        {
            foreach (VesselKind kind in new[] { VesselKind.Board, VesselKind.Pan, VesselKind.Pot, VesselKind.Plate, VesselKind.Bowl })
            {
                int max = VesselRules.Capacity(kind);
                Assert.AreEqual(InteractionAction.PutInto, Act(Potato, With(kind, "x", max - 1)), kind.ToString());
                Assert.AreEqual(InteractionAction.None, Act(Potato, With(kind, "x", max)), kind + " full");
            }
        }

        // ---- row 4: food that does not fit + cookware = None (grey) ----

        [Test]
        public void FullPot_IsNone_PotIsFull()
        {
            AimTarget full = With(VesselKind.Pot, "x", VesselRules.PotCapacity);
            Assert.AreEqual(InteractionAction.None, Act(Potato, full));
            Assert.AreEqual("Pot is full", InteractionRules.PromptFor(Potato, full));
            Assert.AreEqual("Board is full", InteractionRules.PromptFor(Potato, With(VesselKind.Board, "apple")));
        }

        [Test]
        public void PlatedFood_GoesIntoNoCookware()
        {
            foreach (VesselKind kind in new[] { VesselKind.Board, VesselKind.Pan, VesselKind.Pot, VesselKind.Plate, VesselKind.Bowl })
            {
                Assert.AreEqual(InteractionAction.None, Act(Plated, Empty(kind)), kind.ToString());
                Assert.AreEqual("potato plate can't go in there", InteractionRules.Reason(Plated, Empty(kind)), kind.ToString());
                Assert.IsFalse(InteractionRules.Fits(Plated, Empty(kind)));
            }
        }

        // ---- row 5: pan / pot / bowl with food + plate / bowl with room = Pour ----

        [Test]
        public void PanWithFood_AndPlate_IsPour()
        {
            Held pan = Holding(VesselKind.Pan, "crispy fried potato sticks", 1);
            Assert.AreEqual(InteractionAction.Pour, Act(pan, Empty(VesselKind.Plate)));
            Assert.AreEqual("Click · pour into the plate", InteractionRules.PromptFor(pan, Empty(VesselKind.Plate)));
            Assert.AreEqual(InteractionAction.Pour, Act(pan, With(VesselKind.Bowl, "x")));
        }

        [Test]
        public void PotIntoBowl_AndBowlOntoPlate_ArePour_ButNotTheOtherWay()
        {
            Assert.AreEqual(InteractionAction.Pour, Act(Holding(VesselKind.Pot, "mix", 2), Empty(VesselKind.Bowl)));
            Assert.AreEqual(InteractionAction.Pour, Act(Holding(VesselKind.Bowl, "mix", 2), Empty(VesselKind.Plate)));
            Assert.AreEqual(InteractionAction.None, Act(Holding(VesselKind.Bowl, "mix", 2), Empty(VesselKind.Bowl)));
            Assert.AreEqual(InteractionAction.None, Act(Holding(VesselKind.Plate, "plate", 1), Empty(VesselKind.Pan)));
            Assert.AreEqual(InteractionAction.None, Act(Holding(VesselKind.Pan, "x", 1), Empty(VesselKind.Pot)));
        }

        [Test]
        public void PourIntoAFullPlate_IsNone_PlateIsFull_AndAnEmptyPanPoursNothing()
        {
            Held pan = Holding(VesselKind.Pan, "fries", 1);
            AimTarget full = With(VesselKind.Plate, "x", VesselRules.Capacity(VesselKind.Plate));
            Assert.AreEqual(InteractionAction.None, Act(pan, full));
            Assert.AreEqual("Plate is full", InteractionRules.PromptFor(pan, full));
            Assert.AreEqual(InteractionAction.None, Act(Holding(VesselKind.Pan), Empty(VesselKind.Plate)));
            Assert.IsNull(InteractionRules.PromptFor(Holding(VesselKind.Pan), Empty(VesselKind.Plate)));
        }

        // ---- row 6: plate with food + counter = Sell (loose food still sells) ----

        [Test]
        public void PlateWithFood_AndCounter_IsSell()
        {
            Held plate = Holding(VesselKind.Plate, "plate of crispy fried potato sticks", 2);
            AimTarget counter = AimTarget.OnStation(StationKind.Counter);
            Assert.AreEqual(InteractionAction.Sell, Act(plate, counter));
            Assert.AreEqual("Click · sell plate of crispy fried potato sticks", InteractionRules.PromptFor(plate, counter));
            Assert.AreEqual(InteractionAction.Sell, Act(Potato, counter), "the counter plates loose food (spec 009)");
            Assert.AreEqual("Click · sell potato", InteractionRules.PromptFor(Potato, counter));
        }

        [Test]
        public void EmptyPlateOrFullPan_AtTheCounter_IsNone_WithAReason()
        {
            AimTarget counter = AimTarget.OnStation(StationKind.Counter);
            Assert.AreEqual(InteractionAction.None, Act(Holding(VesselKind.Plate), counter));
            Assert.AreEqual("Put food on the plate first", InteractionRules.PromptFor(Holding(VesselKind.Plate), counter));
            Assert.AreEqual("Pour it onto a plate first", InteractionRules.PromptFor(Holding(VesselKind.Pan, "fries", 1), counter));
            Assert.IsNull(InteractionRules.PromptFor(Nothing, counter), "empty hands at the counter");
            Assert.AreEqual(InteractionAction.None, Act(Box, counter));
        }

        // ---- row 7: anything + free spot or free burner = Place ----

        [Test]
        public void PotatoAndFreeStoveTopSpot_IsPlace()
        {
            Assert.AreEqual(InteractionAction.Place, Act(Potato, AimTarget.Spot(3)));
            Assert.AreEqual("Click · put potato here", InteractionRules.PromptFor(Potato, AimTarget.Spot(3)));
        }

        [Test]
        public void PanAndFreeBurner_IsPlace_OnTheBurner()
        {
            Held pan = Holding(VesselKind.Pan);
            Assert.AreEqual(InteractionAction.Place, Act(pan, AimTarget.Spot(0, burner: true)));
            Assert.AreEqual("Click · put pan on the burner", InteractionRules.PromptFor(pan, AimTarget.Spot(0, burner: true)));
        }

        [Test]
        public void AnythingHeld_AndFreeSpot_IsPlace()
        {
            foreach (Held held in new[] { Potato, Plated, Knife, Box, Holding(VesselKind.Pot, "x", 2), Holding(VesselKind.Board) })
                Assert.AreEqual(InteractionAction.Place, Act(held, AimTarget.Spot(0)), held.Name);
            Assert.AreEqual("Click · put knife here", InteractionRules.PromptFor(Knife, AimTarget.Spot(0)));
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

        // ---- row 8: nothing + board / pan / bowl with food = Take ----

        [Test]
        public void EmptyHands_AndBoardPanOrBowlWithFood_IsTake()
        {
            Assert.AreEqual(InteractionAction.Take, Act(Nothing, With(VesselKind.Board, "potato sticks")));
            Assert.AreEqual("Click · take potato sticks", InteractionRules.PromptFor(Nothing, With(VesselKind.Board, "potato sticks")));
            Assert.AreEqual(InteractionAction.Take, Act(Nothing, With(VesselKind.Pan, "crispy fried potato sticks")));
            Assert.AreEqual("Click · take the mix", InteractionRules.PromptFor(Nothing, With(VesselKind.Bowl, "potato", 2)));
            Assert.AreEqual("Click · take potato", InteractionRules.PromptFor(Nothing, With(VesselKind.Bowl, "potato", 1)));
        }

        // ---- row 9: nothing + pot with food = PickUp ----

        [Test]
        public void EmptyHands_AndPotWithFood_IsPickUp()
        {
            AimTarget pot = With(VesselKind.Pot, "mix", 2);
            Assert.AreEqual(InteractionAction.PickUp, Act(Nothing, pot));
            Assert.AreEqual("Click · pick up the pot", InteractionRules.PromptFor(Nothing, pot));
            Assert.AreEqual(InteractionAction.PickUp, Act(Nothing, With(VesselKind.Plate, "fries")), "a plate is carried to the counter");
        }

        // ---- row 10: nothing + any free item or empty cookware = PickUp ----

        [Test]
        public void EmptyHands_AndEmptyPan_IsPickUp()
        {
            Assert.AreEqual(InteractionAction.PickUp, Act(Nothing, Empty(VesselKind.Pan)));
            Assert.AreEqual("Click · pick up the pan", InteractionRules.PromptFor(Nothing, Empty(VesselKind.Pan)));
            foreach (VesselKind kind in new[] { VesselKind.Board, VesselKind.Pot, VesselKind.Plate, VesselKind.Bowl })
                Assert.AreEqual(InteractionAction.PickUp, Act(Nothing, Empty(kind)), kind.ToString());
        }

        [Test]
        public void EmptyHands_AndFreeItem_IsPickUp_FullHandsNone()
        {
            Assert.AreEqual(InteractionAction.PickUp, Act(Nothing, AimTarget.Item("knife")));
            Assert.AreEqual("Click · pick up knife", InteractionRules.PromptFor(Nothing, AimTarget.Item("knife")));
            Assert.AreEqual(InteractionAction.None, Act(Potato, AimTarget.Item("knife")));
            Assert.AreEqual(InteractionAction.None, Act(Knife, AimTarget.Item("potato")));
            Assert.AreEqual(InteractionAction.None, Act(Box, Empty(VesselKind.Pot)), "a box goes into nothing");
        }

        // ---- row 11: nothing + crate / plate shelf = Buy / Take ----

        [Test]
        public void EmptyHands_AndCrate_IsBuy_WithThePrice()
        {
            Assert.AreEqual(InteractionAction.Buy, Act(Nothing, AimTarget.Crate("potato", 60)));
            Assert.AreEqual("Click · take potato ($60)", InteractionRules.PromptFor(Nothing, AimTarget.Crate("potato", 60)));
            Assert.AreEqual("Click · take potato", InteractionRules.PromptFor(Nothing, AimTarget.Crate("potato", -1)));
            Assert.AreEqual(InteractionAction.None, Act(Potato, AimTarget.Crate("potato", 60)), "hands full");
        }

        [Test]
        public void EmptyHands_AndPlateShelf_IsTakeAPlate()
        {
            AimTarget shelf = AimTarget.OnStation(StationKind.PlateShelf);
            Assert.AreEqual(InteractionAction.Take, Act(Nothing, shelf));
            Assert.AreEqual("Click · take a plate", InteractionRules.PromptFor(Nothing, shelf));
            Assert.AreEqual(InteractionAction.None, Act(Potato, shelf));
            Assert.AreEqual(InteractionAction.None, Act(Nothing, AimTarget.OnStation(StationKind.Stove)), "the stove body does nothing");
        }

        // ---- spec 020 review: beyond reach ----

        [Test]
        public void TooFar_IsNone_WithTheReasonTooFar()
        {
            AimTarget farPot = Empty(VesselKind.Pot).TooFar();
            Assert.IsTrue(farPot.IsTooFar);
            Assert.AreEqual(InteractionAction.None, Act(Potato, farPot));
            Assert.AreEqual("Too far", InteractionRules.PromptFor(Potato, farPot));
            Assert.AreEqual("Too far", InteractionRules.PromptFor(Nothing, farPot), "empty hands would pick up the pot");
            Assert.AreEqual("Too far", InteractionRules.PromptFor(Nothing, AimTarget.Button(1, 0).TooFar()));
            Assert.AreEqual("Too far", InteractionRules.PromptFor(Knife, AimTarget.Spot(2).TooFar()));
            Assert.AreEqual("Too far", InteractionRules.PromptFor(Plated, Empty(VesselKind.Pan).TooFar()), "would be grey anyway");
        }

        [Test]
        public void TooFar_WhereNothingWouldHappen_StaysSilent()
        {
            Assert.IsNull(InteractionRules.PromptFor(Nothing, AimTarget.Spot(2).TooFar()), "empty hands, worktop");
            Assert.IsNull(InteractionRules.PromptFor(Knife, Empty(VesselKind.Pan).TooFar()), "a knife at a far pan");
            Assert.IsFalse(Empty(VesselKind.Pot).TooFar().InReach().IsTooFar);
            Assert.AreEqual(InteractionAction.PutInto, Act(Potato, Empty(VesselKind.Pot).TooFar().InReach()));
        }

        // ---- anything else = None, no prompt ----

        [Test]
        public void AnythingElse_IsNone_WithoutPrompt()
        {
            Assert.AreEqual(InteractionAction.None, Act(Nothing, AimTarget.Nothing));
            Assert.IsNull(InteractionRules.PromptFor(Nothing, AimTarget.Nothing));
            Assert.IsNull(InteractionRules.PromptFor(Potato, AimTarget.Nothing));
            Assert.IsNull(InteractionRules.PromptFor(Box, Empty(VesselKind.Pot)), "a box in the pot");
        }

        [Test]
        public void EveryActionHasAPrompt_AndOnlyNoneIsGrey()
        {
            var cases = new (Held, AimTarget)[]
            {
                (Nothing, AimTarget.Button(1, 0)), (Knife, With(VesselKind.Board, "potato")), (Potato, Empty(VesselKind.Board)),
                (Potato, Empty(VesselKind.Pot)), (Holding(VesselKind.Pan, "fries", 1), Empty(VesselKind.Plate)),
                (Holding(VesselKind.Plate, "plate of fries", 1), AimTarget.OnStation(StationKind.Counter)),
                (Knife, AimTarget.Spot(1)), (Holding(VesselKind.Pan), AimTarget.Spot(0, true)), (Nothing, AimTarget.Item("knife")),
                (Nothing, AimTarget.Crate("potato", 60)), (Nothing, With(VesselKind.Pan, "potato")), (Nothing, Empty(VesselKind.Pan)),
                (Nothing, AimTarget.OnStation(StationKind.PlateShelf))
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
