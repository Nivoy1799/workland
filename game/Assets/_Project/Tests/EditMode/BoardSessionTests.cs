using System.Collections.Generic;
using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 019 AC3: food on a cutting board through KitchenState. Strokes and turns only change the progress;
    /// taking applies the sim cut op once, so the result is the same item the digit keys make.
    /// </summary>
    public class BoardSessionTests
    {
        private const int FoodId = 42;
        private const int OtherId = 43;
        private const int Board = 4; // spec 021: the board vessel's object id

        private static ItemFactory NewFactory()
        {
            return new ItemFactory(new ItemCatalog(DemoIngredients.All), OperationCatalog.CreateDefault());
        }

        private static Item CutDirectly(Item food, string shape)
        {
            return NewFactory().Apply("cut", new[] { food },
                new Dictionary<string, ParamValue> { ["shape"] = ParamValue.Choice(shape) }).Item;
        }

        private static KitchenState KitchenWith(string baseId, int id, out Item food)
        {
            var kitchen = KitchenState.CreateDemo();
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateIngredient(baseId, out food));
            kitchen.Register(id, food);
            return kitchen;
        }

        private static Item CutAndTake(KitchenState kitchen, params int[] options)
        {
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutOnBoard(FoodId, Board));
            Assert.IsFalse(kitchen.TryGet(FoodId, out _), "the food lies on the board, not in the hands");
            foreach (int option in options)
            {
                CookOutcome outcome = option == VesselRules.ActionStroke ? kitchen.TryBoardStroke(Board) : kitchen.TryBoardTurn(Board);
                Assert.AreEqual(CookOutcome.Ok, outcome);
            }
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewBoardTake(Board, out Item taken));
            Assert.IsTrue(kitchen.TryGetBoard(Board, out _, out _), "preview does not empty the board");
            kitchen.ClearBoard(Board);
            Assert.IsFalse(kitchen.TryGetBoard(Board, out _, out _));
            return taken;
        }

        private const int S = VesselRules.ActionStroke;
        private const int T = VesselRules.ActionTurn;

        [Test]
        public void PutTwoStrokesTake_IsTheSimSlices()
        {
            KitchenState kitchen = KitchenWith("potato", FoodId, out Item potato);
            Item slices = CutAndTake(kitchen, S, S);
            Assert.AreEqual(CutDirectly(potato, "slices").Hash, slices.Hash);
            Assert.AreEqual("potato slices", KitchenState.NameOf(slices));
        }

        [Test]
        public void OneStroke_IsTheSimHalves()
        {
            KitchenState kitchen = KitchenWith("potato", FoodId, out Item potato);
            Assert.AreEqual(CutDirectly(potato, "halves").Hash, CutAndTake(kitchen, S).Hash);
        }

        [Test]
        public void SlicesTurnStroke_IsTheSimSticks_OnAnyBoard()
        {
            KitchenState kitchen = KitchenWith("potato", FoodId, out Item potato);
            Item sticks = CutAndTake(kitchen, S, S, T, S);
            Assert.AreEqual(CutDirectly(potato, "sticks").Hash, sticks.Hash);

            // Spec 021: keyed by the board vessel's id; another board makes the very same item.
            KitchenState other = KitchenWith("potato", FoodId, out _);
            Item byOther = HeatTestKit.Cut(other, FoodId, 3);
            Assert.AreEqual(byOther.Hash, sticks.Hash);
        }

        [Test]
        public void SticksTurnStroke_IsTheSimDice()
        {
            KitchenState kitchen = KitchenWith("tomato", FoodId, out Item tomato);
            Item dice = CutAndTake(kitchen, S, S, T, S, T, S);
            Assert.AreEqual(CutDirectly(tomato, "dice").Hash, dice.Hash);
        }

        [Test]
        public void TakeWithoutStrokes_ReturnsTheItemUnchanged()
        {
            KitchenState kitchen = KitchenWith("potato", FoodId, out Item potato);
            Assert.AreEqual(potato.Hash, CutAndTake(kitchen).Hash);
            KitchenState turnedOnly = KitchenWith("potato", FoodId, out _);
            Assert.AreEqual(potato.Hash, CutAndTake(turnedOnly, T).Hash, "turning whole food cuts nothing");
        }

        [Test]
        public void StrokeOrTurnOnAnEmptyBoard_NothingToCut()
        {
            var kitchen = KitchenState.CreateDemo();
            Assert.AreEqual(CookOutcome.NothingToCut, kitchen.TryBoardStroke(Board));
            Assert.AreEqual(CookOutcome.NothingToCut, kitchen.TryBoardTurn(Board));
            Assert.AreEqual(CookOutcome.StationEmpty, kitchen.TryPreviewBoardTake(Board, out Item none));
            Assert.IsNull(none);
            Assert.IsNull(kitchen.BoardLook(Board));
            Assert.IsNull(kitchen.BoardName(Board));
        }

        [Test]
        public void SecondFoodOnAFullBoard_StationFull_AndStaysInTheHands()
        {
            KitchenState kitchen = KitchenWith("potato", FoodId, out _);
            kitchen.TryCreateIngredient("onion", out Item onion);
            kitchen.Register(OtherId, onion);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutOnBoard(FoodId, Board));
            Assert.AreEqual(CookOutcome.VesselFull, kitchen.TryPutOnBoard(OtherId, Board));
            Assert.IsTrue(kitchen.TryGet(OtherId, out Item still));
            Assert.AreEqual(onion.Hash, still.Hash);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutOnBoard(OtherId, Board + 1), "another board is free");
        }

        [Test]
        public void PlatedFood_IsRefused_AndStaysInTheHands()
        {
            KitchenState kitchen = KitchenWith("potato", FoodId, out _);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryAddToVessel(FoodId, 9, VesselKind.Plate));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryTakeFromVessel(9, VesselKind.Plate, out Item plate));
            kitchen.Register(OtherId, plate);
            Assert.AreEqual(CookOutcome.AlreadyPlated, kitchen.TryPutOnBoard(OtherId, Board), "spec 021: plated food never fits");
            Assert.IsTrue(kitchen.TryGet(OtherId, out _));
            Assert.IsFalse(kitchen.TryGetBoard(Board, out _, out _));
        }

        [Test]
        public void UnknownFood_IsNotFood()
        {
            Assert.AreEqual(CookOutcome.NotFood, KitchenState.CreateDemo().TryPutOnBoard(FoodId, Board));
        }

        [Test]
        public void BoardLookAndName_FollowTheStrokes()
        {
            KitchenState kitchen = KitchenWith("potato", FoodId, out Item potato);
            kitchen.TryPutOnBoard(FoodId, Board);
            Assert.AreEqual(FoodLookRules.From(potato), kitchen.BoardLook(Board));
            Assert.AreEqual("potato", kitchen.BoardName(Board));

            kitchen.TryBoardStroke(Board);
            kitchen.TryBoardStroke(Board);
            FoodLook expected = FoodLookRules.From(CutDirectly(potato, "slices"));
            Assert.AreEqual(expected, kitchen.BoardLook(Board), "the preview look is the look of what you would take");
            Assert.AreEqual(FoodLook.ShapeSlices, kitchen.BoardLook(Board).Value.Shape);
            Assert.AreEqual("potato slices", kitchen.BoardName(Board));

            kitchen.TryBoardTurn(Board);
            Assert.IsTrue(kitchen.TryGetBoard(Board, out _, out CutProgress progress));
            Assert.AreEqual(new CutProgress(FoodLook.ShapeSlices, 1), progress);
        }

        [Test]
        public void AlreadyCutFood_KeepsItsLookUntilTheKnifeChangesIt()
        {
            KitchenState kitchen = KitchenWith("potato", FoodId, out _);
            Item sticks = HeatTestKit.Cut(kitchen, FoodId, 3); // cut on another board before
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutOnBoard(FoodId, Board));
            Assert.AreEqual(FoodLook.ShapeSticks, kitchen.BoardLook(Board).Value.Shape);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewBoardTake(Board, out Item back));
            Assert.AreEqual(sticks.Hash, back.Hash, "uncut on the board: handed back unchanged");
        }

        [Test]
        public void Clear_EmptiesTheBoards()
        {
            KitchenState kitchen = KitchenWith("potato", FoodId, out _);
            kitchen.TryPutOnBoard(FoodId, Board);
            kitchen.Clear();
            Assert.IsFalse(kitchen.TryGetBoard(Board, out _, out _));
        }

        [Test]
        public void WithShape_ChangesOnlyTheShape()
        {
            var look = new FoodLook(2, FoodLook.ShapeWhole, FoodLook.DonenessShort, FoodLook.HeatBoiled, FoodLook.KindSingle, 0);
            FoodLook cut = FoodLookRules.WithShape(look, FoodLook.ShapeSticks);
            Assert.AreEqual(new FoodLook(2, FoodLook.ShapeSticks, FoodLook.DonenessShort, FoodLook.HeatBoiled, FoodLook.KindSingle, 0), cut);
        }
    }
}
