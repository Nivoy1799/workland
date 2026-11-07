using NUnit.Framework;
using Worktown.Game.Player;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 018 AC2.</summary>
    public class ViewRulesTests
    {
        [Test]
        public void Toggle_FlipsTheMode()
        {
            Assert.AreEqual(ViewMode.ThirdPerson, ViewRules.Toggle(ViewMode.FirstPerson));
            Assert.AreEqual(ViewMode.FirstPerson, ViewRules.Toggle(ViewMode.ThirdPerson));
            Assert.AreEqual(ViewMode.FirstPerson, ViewRules.Toggle(ViewRules.Toggle(ViewMode.FirstPerson)));
        }

        [TestCase(0f, 0f)]
        [TestCase(45f, 45f)]
        [TestCase(-45f, -45f)]
        [TestCase(80f, 80f)]
        [TestCase(-80f, -80f)]
        [TestCase(95f, 80f)]
        [TestCase(-120f, -80f)]
        [TestCase(10000f, 80f)]
        public void Pitch_ClampsTo80(float pitch, float expected)
        {
            Assert.AreEqual(expected, ViewRules.ClampPitch(pitch), 0.0001f);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void Pitch_NotFinite_IsLevel(float pitch)
        {
            Assert.AreEqual(0f, ViewRules.ClampPitch(pitch));
        }

        [Test]
        public void StartMode_PrefersLaunchArg()
        {
            Assert.AreEqual(ViewMode.ThirdPerson, ViewRules.StartMode(ViewMode.ThirdPerson, ViewMode.FirstPerson));
            Assert.AreEqual(ViewMode.FirstPerson, ViewRules.StartMode(ViewMode.FirstPerson, ViewMode.ThirdPerson));
        }

        [Test]
        public void StartMode_ThenSaved()
        {
            Assert.AreEqual(ViewMode.ThirdPerson, ViewRules.StartMode(null, ViewMode.ThirdPerson));
            Assert.AreEqual(ViewMode.FirstPerson, ViewRules.StartMode(null, ViewMode.FirstPerson));
        }

        [Test]
        public void StartMode_DefaultsToFirstPerson()
        {
            Assert.AreEqual(ViewMode.FirstPerson, ViewRules.StartMode(null, null));
        }

        [Test]
        public void Saved_UnknownValueIsNothingSaved()
        {
            Assert.AreEqual(ViewMode.FirstPerson, ViewRules.FromSaved(0));
            Assert.AreEqual(ViewMode.ThirdPerson, ViewRules.FromSaved(1));
            Assert.IsNull(ViewRules.FromSaved(-1)); // PlayerPrefs default: never chosen
            Assert.IsNull(ViewRules.FromSaved(7));
        }

        [TestCase("first", ViewMode.FirstPerson)]
        [TestCase("third", ViewMode.ThirdPerson)]
        [TestCase("THIRD", ViewMode.ThirdPerson)]
        public void Parse_KnownValues(string value, ViewMode expected)
        {
            Assert.IsTrue(ViewRules.TryParse(value, out ViewMode mode));
            Assert.AreEqual(expected, mode);
        }

        [TestCase("")]
        [TestCase("fps")]
        [TestCase(null)]
        public void Parse_UnknownValues(string value)
        {
            Assert.IsFalse(ViewRules.TryParse(value, out _));
        }

        [Test]
        public void EyeHeight_DropsWhenCrouched()
        {
            Assert.AreEqual(ViewRules.EyeHeight, ViewRules.EyeHeightFor(false));
            Assert.AreEqual(ViewRules.CrouchEyeHeight, ViewRules.EyeHeightFor(true));
            Assert.Less(ViewRules.CrouchEyeHeight, MovementRules.CrouchHeight); // eyes stay inside the crouched capsule
            Assert.Less(ViewRules.EyeHeight, MovementRules.StandHeight);
        }
    }
}
