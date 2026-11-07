using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 007 AC1 (station mapping) and AC4 (stars).</summary>
    public class CookingRulesTests
    {
        [TestCase(StationKind.Board, 1, "cut", "halves")]
        [TestCase(StationKind.Board, 2, "cut", "slices")]
        [TestCase(StationKind.Board, 3, "cut", "sticks")]
        [TestCase(StationKind.Board, 4, "cut", "dice")]
        public void Board_MapsToCutShape(StationKind kind, int option, string op, string shape)
        {
            Assert.IsTrue(StationRules.TryGetAction(kind, option, out StationAction action));
            Assert.AreEqual(op, action.OperationId);
            Assert.AreEqual(ParamValue.Choice(shape), action.Parameters["shape"]);
        }

        [TestCase(StationKind.Pan)]
        [TestCase(StationKind.Pot)]
        public void PanAndPot_HaveNoInstantPresets_Spec016(StationKind kind)
        {
            for (int option = 1; option <= 4; option++)
                Assert.IsFalse(StationRules.TryGetAction(kind, option, out _), "option " + option);
            Assert.IsTrue(StationRules.IsHeat(kind));
        }

        [TestCase(StationKind.Board, 0)]
        [TestCase(StationKind.Board, 5)]
        [TestCase(StationKind.Pan, 4)]
        [TestCase(StationKind.Pot, -1)]
        [TestCase(StationKind.Crate, 1)]
        [TestCase(StationKind.Counter, 1)]
        [TestCase((StationKind)99, 1)]
        public void UnknownStationOrOption_IsRejected(StationKind kind, int option)
        {
            Assert.IsFalse(StationRules.TryGetAction(kind, option, out _));
        }

        [Test]
        public void OptionCounts()
        {
            Assert.AreEqual(4, StationRules.OptionCount(StationKind.Board));
            Assert.AreEqual(3, StationRules.OptionCount(StationKind.Pan));
            Assert.AreEqual(0, StationRules.OptionCount(StationKind.Pot));
            Assert.AreEqual(0, StationRules.OptionCount(StationKind.Crate));
            Assert.AreEqual(0, StationRules.OptionCount(StationKind.Counter));
        }

        [Test]
        public void Prompt_ListsOptions()
        {
            Assert.AreEqual("Pan: E put food in / E (empty hands) take it out, 1 heat down, 2 heat up, 3 fry/saute",
                StationRules.Prompt(StationKind.Pan, "Pan"));
            Assert.AreEqual("E · 1-3", StationRules.KeysHint(StationKind.Pan));
            Assert.AreEqual("E", StationRules.KeysHint(StationKind.Pot));
            Assert.AreEqual("1-4", StationRules.KeysHint(StationKind.Board));
            Assert.AreEqual("Board: 1 halves, 2 slices, 3 sticks, 4 cubes", StationRules.Prompt(StationKind.Board, "Board"));
            Assert.AreEqual("Counter: E sell", StationRules.Prompt(StationKind.Counter, "Counter"));
        }

        [Test]
        public void Reach_IsTheSameAsPickups()
        {
            Assert.IsTrue(StationRules.IsInReach(Vector3.zero, new Vector3(2.4f, 0f, 0f)));
            Assert.IsFalse(StationRules.IsInReach(Vector3.zero, new Vector3(2.6f, 0f, 0f)));
        }

        [TestCase(-1f, 1)]
        [TestCase(0f, 1)]
        [TestCase(0.13f, 1)]
        [TestCase(0.2f, 2)]
        [TestCase(0.39f, 2)]
        [TestCase(0.4f, 3)]
        [TestCase(0.61f, 4)]
        [TestCase(0.70f, 4)]
        [TestCase(0.8f, 5)]
        [TestCase(1f, 5)]
        [TestCase(float.NaN, 1)]
        public void Stars_AtFixedBoundaries(float quality, int stars)
        {
            Assert.AreEqual(stars, StarRating.Stars(quality));
        }

        [Test]
        public void Stars_Format()
        {
            Assert.AreEqual("★★★★☆", StarRating.Format(4));
            Assert.AreEqual("☆☆☆☆☆", StarRating.Format(0));
            Assert.AreEqual("★★★★★", StarRating.Format(9));
        }
    }
}
