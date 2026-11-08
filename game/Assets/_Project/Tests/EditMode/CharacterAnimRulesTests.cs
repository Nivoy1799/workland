using NUnit.Framework;
using Worktown.Game.Player;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 024 AC3, spec 025 AC5: speed and crouch to the character's animation state and playback rate.</summary>
    public class CharacterAnimRulesTests
    {
        [Test]
        public void StateFor_SpeedPicksIdleWalkRun()
        {
            Assert.AreEqual(CharacterAnimState.Idle, CharacterAnimRules.StateFor(0f, false));
            Assert.AreEqual(CharacterAnimState.Idle, CharacterAnimRules.StateFor(0.4f, false));
            Assert.AreEqual(CharacterAnimState.Walk, CharacterAnimRules.StateFor(2f, false), "customer walk speed");
            Assert.AreEqual(CharacterAnimState.Walk, CharacterAnimRules.StateFor(MovementRules.WalkSpeed, false), "player walk speed");
            Assert.AreEqual(CharacterAnimState.Walk, CharacterAnimRules.StateFor(MovementRules.CrouchSpeed, false));
            Assert.AreEqual(CharacterAnimState.Run, CharacterAnimRules.StateFor(MovementRules.SprintSpeed, false), "player sprint speed");
            Assert.AreEqual(CharacterAnimState.Run, CharacterAnimRules.StateFor(CharacterAnimRules.RunStartSpeed, false));
        }

        [Test]
        public void StateFor_CrouchFlagWinsOverEverySpeed()
        {
            foreach (float speed in new[] { 0f, 1.25f, 2.5f, 5f })
                foreach (CharacterAnimState previous in new[] { CharacterAnimState.Idle, CharacterAnimState.Walk, CharacterAnimState.Run, CharacterAnimState.Crouch })
                    Assert.AreEqual(CharacterAnimState.Crouch, CharacterAnimRules.StateFor(speed, true, previous), speed + " " + previous);
            Assert.AreEqual(CharacterAnimState.Idle, CharacterAnimRules.StateFor(0f, false, CharacterAnimState.Crouch), "standing up again");
            Assert.AreEqual(CharacterAnimState.Walk, CharacterAnimRules.StateFor(2f, false, CharacterAnimState.Crouch));
        }

        [Test]
        public void StateFor_HasHysteresis()
        {
            // Between stop and start thresholds the previous state stays.
            Assert.AreEqual(CharacterAnimState.Walk, CharacterAnimRules.StateFor(0.3f, false, CharacterAnimState.Walk));
            Assert.AreEqual(CharacterAnimState.Idle, CharacterAnimRules.StateFor(0.3f, false, CharacterAnimState.Idle));
            Assert.AreEqual(CharacterAnimState.Idle, CharacterAnimRules.StateFor(0.2f, false, CharacterAnimState.Walk));
            Assert.AreEqual(CharacterAnimState.Run, CharacterAnimRules.StateFor(3.5f, false, CharacterAnimState.Run));
            Assert.AreEqual(CharacterAnimState.Walk, CharacterAnimRules.StateFor(3.5f, false, CharacterAnimState.Walk));
            Assert.AreEqual(CharacterAnimState.Walk, CharacterAnimRules.StateFor(3.2f, false, CharacterAnimState.Run));
            Assert.AreEqual(CharacterAnimState.Idle, CharacterAnimRules.StateFor(0.1f, false, CharacterAnimState.Run));
        }

        [Test]
        public void StateFor_BadSpeedsStandStill()
        {
            Assert.AreEqual(CharacterAnimState.Idle, CharacterAnimRules.StateFor(float.NaN, false, CharacterAnimState.Run));
            Assert.AreEqual(CharacterAnimState.Idle, CharacterAnimRules.StateFor(float.PositiveInfinity, false));
            Assert.AreEqual(CharacterAnimState.Idle, CharacterAnimRules.StateFor(-3f, false, CharacterAnimState.Walk));
            Assert.AreEqual(CharacterAnimState.Crouch, CharacterAnimRules.StateFor(float.NaN, true));
        }

        [Test]
        public void RunThresholds_LieBetweenWalkAndSprint()
        {
            Assert.AreEqual(3.75f, CharacterAnimRules.RunStartSpeed);
            Assert.AreEqual(3.25f, CharacterAnimRules.RunStopSpeed);
            Assert.Greater(CharacterAnimRules.RunStopSpeed, MovementRules.WalkSpeed, "walking never shows the run clip");
            Assert.Less(CharacterAnimRules.RunStartSpeed, MovementRules.SprintSpeed, "sprinting always does");
            Assert.AreEqual(CharacterAnimState.Walk, CharacterAnimRules.StateFor(3.7f, false, CharacterAnimState.Walk));
            Assert.AreEqual(CharacterAnimState.Run, CharacterAnimRules.StateFor(3.3f, false, CharacterAnimState.Run));
        }

        [Test]
        public void PlaybackRate_IsSpeedOverClipSpeed_WithinLimits()
        {
            Assert.AreEqual(1f, CharacterAnimRules.PlaybackRate(CharacterAnimState.Idle, 3f, 2f));
            Assert.AreEqual(1f, CharacterAnimRules.PlaybackRate(CharacterAnimState.Crouch, 2f, 2f));
            Assert.AreEqual(1f, CharacterAnimRules.PlaybackRate(CharacterAnimState.Walk, 1.7f, 1.7f), 1e-5f);
            Assert.AreEqual(1.25f, CharacterAnimRules.PlaybackRate(CharacterAnimState.Walk, 2.5f, 2f), 1e-5f);
            Assert.AreEqual(0.8f, CharacterAnimRules.PlaybackRate(CharacterAnimState.Run, 4f, 5f), 1e-5f);
            Assert.AreEqual(CharacterAnimRules.MinRate, CharacterAnimRules.PlaybackRate(CharacterAnimState.Walk, 0.5f, 2f));
            Assert.AreEqual(CharacterAnimRules.MaxRate, CharacterAnimRules.PlaybackRate(CharacterAnimState.Run, 50f, 4f));
            Assert.AreEqual(CharacterAnimRules.MinRate, CharacterAnimRules.PlaybackRate(CharacterAnimState.Walk, float.NaN, 2f));
            Assert.Greater(CharacterAnimRules.PlaybackRate(CharacterAnimState.Walk, 2.5f, 2f), CharacterAnimRules.PlaybackRate(CharacterAnimState.Walk, 2f, 2f));
        }

        [Test]
        public void PlaybackRate_UnknownClipSpeed_PlaysAtOne()
        {
            Assert.AreEqual(1f, CharacterAnimRules.PlaybackRate(CharacterAnimState.Walk, 2.5f, 0f));
            Assert.AreEqual(1f, CharacterAnimRules.PlaybackRate(CharacterAnimState.Run, 5f, -1f));
            Assert.AreEqual(1f, CharacterAnimRules.PlaybackRate(CharacterAnimState.Run, 5f, float.NaN));
        }

        [Test]
        public void FitsWithoutClamp_IsStrictlyInsideTheLimits()
        {
            Assert.IsTrue(CharacterAnimRules.FitsWithoutClamp(2.5f, 2f));
            Assert.IsFalse(CharacterAnimRules.FitsWithoutClamp(2.5f, 1f), "rate 2.5");
            Assert.IsFalse(CharacterAnimRules.FitsWithoutClamp(1f, 2f), "rate 0.5");
            Assert.IsFalse(CharacterAnimRules.FitsWithoutClamp(CharacterAnimRules.MaxRate * 2f, 2f), "exactly at the limit clamps");
            Assert.IsFalse(CharacterAnimRules.FitsWithoutClamp(2f, 0f));
            Assert.IsFalse(CharacterAnimRules.FitsWithoutClamp(2f, float.NaN));
        }

        [Test]
        public void SmoothSpeed_ApproachesTheMeasuredSpeedAndIgnoresBadSamples()
        {
            float speed = 0f;
            for (int i = 0; i < 60; i++) speed = CharacterAnimRules.SmoothSpeed(speed, 4f, 1f / 60f);
            Assert.AreEqual(4f, speed, 0.01f, "one second at 60 fps");
            float one = CharacterAnimRules.SmoothSpeed(0f, 4f, 1f / 60f);
            Assert.Greater(one, 0f);
            Assert.Less(one, 4f);
            Assert.AreEqual(2f, CharacterAnimRules.SmoothSpeed(2f, float.NaN, 0.1f));
            Assert.AreEqual(2f, CharacterAnimRules.SmoothSpeed(2f, -1f, 0.1f));
            Assert.AreEqual(2f, CharacterAnimRules.SmoothSpeed(2f, 4f, 0f));
        }

        [Test]
        public void FadeWeight_ReachesTheTargetInFadeSeconds()
        {
            float half = CharacterAnimRules.FadeSeconds * 0.5f;
            Assert.AreEqual(0.5f, CharacterAnimRules.FadeWeight(0f, true, half), 1e-5f);
            Assert.AreEqual(1f, CharacterAnimRules.FadeWeight(0.5f, true, CharacterAnimRules.FadeSeconds));
            Assert.AreEqual(0f, CharacterAnimRules.FadeWeight(1f, false, CharacterAnimRules.FadeSeconds * 2f));
            Assert.AreEqual(0.3f, CharacterAnimRules.FadeWeight(0.3f, true, 0f));
        }

        [Test]
        public void LoopTime_WrapsIntoTheClip()
        {
            Assert.AreEqual(0.25f, CharacterAnimRules.LoopTime(0.25, 0.5f), 1e-5f);
            Assert.AreEqual(0.25f, CharacterAnimRules.LoopTime(1.25, 0.5f), 1e-5f);
            Assert.AreEqual(0.4f, CharacterAnimRules.LoopTime(-0.1, 0.5f), 1e-5f);
            Assert.AreEqual(0f, CharacterAnimRules.LoopTime(3.0, 0f));
            Assert.AreEqual(0f, CharacterAnimRules.LoopTime(double.NaN, 0.5f));
        }
    }
}
