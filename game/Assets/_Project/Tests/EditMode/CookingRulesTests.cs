using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 007 AC1 (station mapping) and AC4 (stars). Spec 021: the board, pan and pot are carried cookware now (see
    /// VesselRulesTests, BoardSessionTests, HeatSessionTests); the stations are crates, the counter, the stoves and the
    /// plate shelf, and a stove's options are its burners' knobs and buttons.
    /// </summary>
    public class CookingRulesTests
    {
        [TestCase(0, StationRules.StoveSwitch, 1)]
        [TestCase(0, StationRules.PanMode, 2)]
        [TestCase(1, StationRules.StoveSwitch, 3)]
        [TestCase(1, StationRules.PanMode, 4)]
        public void StoveOption_PerBurner_AndBack(int burner, int button, int option)
        {
            Assert.AreEqual(option, StationRules.StoveOption(burner, button));
            Assert.IsTrue(StationRules.TryParseStoveOption(option, out int b, out int k));
            Assert.AreEqual(burner, b);
            Assert.AreEqual(button, k);
        }

        [TestCase(0)]
        [TestCase(5)]
        [TestCase(-1)]
        [TestCase(99)]
        public void UnknownStoveOption_IsRejected(int option)
        {
            Assert.IsFalse(StationRules.TryParseStoveOption(option, out int burner, out _));
            Assert.AreEqual(-1, burner);
        }

        [Test]
        public void Prompt_ForEveryStation()
        {
            Assert.AreEqual("Counter: E sell", StationRules.Prompt(StationKind.Counter, "Counter"));
            Assert.AreEqual("Potato crate: E take one", StationRules.Prompt(StationKind.Crate, "Potato crate"));
            Assert.AreEqual("Plates: E take a plate", StationRules.Prompt(StationKind.PlateShelf, "Plates"));
            foreach (StationKind kind in System.Enum.GetValues(typeof(StationKind)))
                Assert.IsNotEmpty(StationRules.Prompt(kind, "X"), kind.ToString());
        }

        [Test]
        public void StationKinds_AreTheSpec021Furniture()
        {
            CollectionAssert.AreEquivalent(new[] { StationKind.Crate, StationKind.Counter, StationKind.Stove, StationKind.PlateShelf },
                System.Enum.GetValues(typeof(StationKind)));
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
