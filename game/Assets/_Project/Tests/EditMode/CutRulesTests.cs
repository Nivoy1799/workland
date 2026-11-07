using NUnit.Framework;
using Worktown.Game.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 019 AC2: every row of the stroke-to-shape table, and the lost-knife rule.</summary>
    public class CutRulesTests
    {
        private static CutProgress Strokes(CutProgress progress, int count)
        {
            for (int i = 0; i < count; i++) progress = CutRules.Stroke(progress);
            return progress;
        }

        private static CutProgress Sticks() => CutRules.Stroke(CutRules.Turn(Strokes(CutProgress.Whole, 2)));

        [Test]
        public void Start_IsWholeNotTurned()
        {
            Assert.AreEqual(FoodLook.ShapeWhole, CutProgress.Whole.Shape);
            Assert.AreEqual(0, CutProgress.Whole.Turned);
        }

        [Test]
        public void OneStroke_Halves()
        {
            Assert.AreEqual(new CutProgress(FoodLook.ShapeHalves, 0), Strokes(CutProgress.Whole, 1));
        }

        [TestCase(2)]
        [TestCase(5)]
        public void TwoOrMoreStrokes_WithoutTurning_Slices(int strokes)
        {
            Assert.AreEqual(new CutProgress(FoodLook.ShapeSlices, 0), Strokes(CutProgress.Whole, strokes));
        }

        [Test]
        public void TwoStrokesTurnStroke_Sticks_AndTheTurnIsUsedUp()
        {
            CutProgress slices = Strokes(CutProgress.Whole, 2);
            CutProgress turned = CutRules.Turn(slices);
            Assert.AreEqual(new CutProgress(FoodLook.ShapeSlices, 1), turned);
            Assert.AreEqual(new CutProgress(FoodLook.ShapeSticks, 0), CutRules.Stroke(turned));
        }

        [Test]
        public void SticksTurnStroke_Dice()
        {
            CutProgress turned = CutRules.Turn(Sticks());
            Assert.AreEqual(new CutProgress(FoodLook.ShapeSticks, 1), turned);
            Assert.AreEqual(new CutProgress(FoodLook.ShapeCubes, 0), CutRules.Stroke(turned));
        }

        [Test]
        public void TurningWholeOrHalves_ChangesNoShape()
        {
            Assert.AreEqual(CutProgress.Whole, CutRules.Turn(CutProgress.Whole));
            CutProgress halves = Strokes(CutProgress.Whole, 1);
            Assert.AreEqual(halves, CutRules.Turn(halves));
            // ...so turning first does not skip a step: whole, turn, 2 strokes is still slices.
            Assert.AreEqual(FoodLook.ShapeSlices, Strokes(CutRules.Turn(CutProgress.Whole), 2).Shape);
        }

        [Test]
        public void StrokeOnSticks_WithoutTurn_StaysSticks()
        {
            Assert.AreEqual(Sticks(), CutRules.Stroke(Sticks()));
            Assert.AreEqual(Sticks(), Strokes(Sticks(), 4));
        }

        [Test]
        public void TurningTwice_StaysTurned()
        {
            CutProgress turned = CutRules.Turn(Strokes(CutProgress.Whole, 2));
            Assert.AreEqual(turned, CutRules.Turn(turned));
        }

        [Test]
        public void Dice_StayDice()
        {
            CutProgress dice = CutRules.Stroke(CutRules.Turn(Sticks()));
            Assert.AreEqual(dice, CutRules.Stroke(dice));
            Assert.AreEqual(dice, CutRules.Turn(dice));
            Assert.AreEqual(dice, Strokes(CutRules.Turn(dice), 3));
        }

        [Test]
        public void ShapeIds_AreTheSimCutChoices()
        {
            Assert.IsNull(CutRules.ShapeId(FoodLook.ShapeWhole));
            Assert.AreEqual("halves", CutRules.ShapeId(FoodLook.ShapeHalves));
            Assert.AreEqual("slices", CutRules.ShapeId(FoodLook.ShapeSlices));
            Assert.AreEqual("sticks", CutRules.ShapeId(FoodLook.ShapeSticks));
            Assert.AreEqual("dice", CutRules.ShapeId(FoodLook.ShapeCubes));
            Assert.IsNull(CutRules.ShapeId(200));
        }

        [Test]
        public void ShapeIds_RoundTripThroughTheLookRules()
        {
            for (byte shape = FoodLook.ShapeHalves; shape <= FoodLook.ShapeCubes; shape++)
                Assert.AreEqual(shape, FoodLookRules.ShapeOf(CutRules.ShapeId(shape)));
        }

        [Test]
        public void Knife_IsLostWhenGoneOrBelowTheWorld()
        {
            Assert.IsFalse(CutRules.IsKnifeLost(true, 1f));
            Assert.IsFalse(CutRules.IsKnifeLost(true, -9.9f));
            Assert.IsTrue(CutRules.IsKnifeLost(true, -10.1f));
            Assert.IsTrue(CutRules.IsKnifeLost(true, float.NaN));
            Assert.IsTrue(CutRules.IsKnifeLost(false, 1f));
        }

        [Test]
        public void LostKnife_RespawnsAfterFiveSeconds()
        {
            Assert.IsFalse(CutRules.ShouldRespawnKnife(10f, 14.9f));
            Assert.IsTrue(CutRules.ShouldRespawnKnife(10f, 15f));
            Assert.IsTrue(CutRules.ShouldRespawnKnife(10f, 60f));
        }
    }
}
