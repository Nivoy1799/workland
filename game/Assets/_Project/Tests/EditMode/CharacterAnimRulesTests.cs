using NUnit.Framework;
using Worktown.Game.Player;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 024 AC3: speed and crouch to the character's animation state.</summary>
    public class CharacterAnimRulesTests
    {
        [Test]
        public void StateFor_SpeedPicksIdleWalkRun()
        {
            Assert.AreEqual(CharacterAnimState.Idle, CharacterAnimRules.StateFor(0f, false));
            Assert.AreEqual(CharacterAnimState.Idle, CharacterAnimRules.StateFor(0.4f, false));
            Assert.AreEqual(CharacterAnimState.Walk, CharacterAnimRules.StateFor(2f, false), "customer walk speed");
            Assert.AreEqual(CharacterAnimState.Walk, CharacterAnimRules.StateFor(4f, false), "player walk speed");
            Assert.AreEqual(CharacterAnimState.Run, CharacterAnimRules.StateFor(8f, false), "player sprint speed");
            Assert.AreEqual(CharacterAnimState.Run, CharacterAnimRules.StateFor(CharacterAnimRules.RunStartSpeed, false));
        }

        [Test]
        public void StateFor_CrouchFlagWinsOverEverySpeed()
        {
            foreach (float speed in new[] { 0f, 2f, 4f, 8f })
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
            Assert.AreEqual(CharacterAnimState.Run, CharacterAnimRules.StateFor(5.5f, false, CharacterAnimState.Run));
            Assert.AreEqual(CharacterAnimState.Walk, CharacterAnimRules.StateFor(5.5f, false, CharacterAnimState.Walk));
            Assert.AreEqual(CharacterAnimState.Walk, CharacterAnimRules.StateFor(4.9f, false, CharacterAnimState.Run));
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
        public void PlaybackRate_FollowsSpeedWithinLimits()
        {
            Assert.AreEqual(1f, CharacterAnimRules.PlaybackRate(CharacterAnimState.Idle, 3f));
            Assert.AreEqual(1f, CharacterAnimRules.PlaybackRate(CharacterAnimState.Crouch, 2f));
            Assert.AreEqual(1f, CharacterAnimRules.PlaybackRate(CharacterAnimState.Walk, CharacterAnimRules.WalkClipSpeed), 1e-5f);
            Assert.AreEqual(1f, CharacterAnimRules.PlaybackRate(CharacterAnimState.Run, CharacterAnimRules.RunClipSpeed), 1e-5f);
            Assert.AreEqual(CharacterAnimRules.MinRate, CharacterAnimRules.PlaybackRate(CharacterAnimState.Walk, 0.5f));
            Assert.AreEqual(CharacterAnimRules.MaxRate, CharacterAnimRules.PlaybackRate(CharacterAnimState.Run, 50f));
            Assert.AreEqual(CharacterAnimRules.MinRate, CharacterAnimRules.PlaybackRate(CharacterAnimState.Walk, float.NaN));
            Assert.Greater(CharacterAnimRules.PlaybackRate(CharacterAnimState.Walk, 4f), CharacterAnimRules.PlaybackRate(CharacterAnimState.Walk, 2f));
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
