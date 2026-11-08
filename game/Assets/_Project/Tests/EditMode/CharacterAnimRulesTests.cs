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
            // Spec 025 amendment: crouched and moving is the crouch walk, crouched and still the crouch idle.
            var all = new[] { CharacterAnimState.Idle, CharacterAnimState.Walk, CharacterAnimState.Run, CharacterAnimState.Crouch, CharacterAnimState.CrouchWalk };
            foreach (float speed in new[] { 1.25f, 2.5f, 5f })
                foreach (CharacterAnimState previous in all)
                    Assert.AreEqual(CharacterAnimState.CrouchWalk, CharacterAnimRules.StateFor(speed, true, previous), speed + " " + previous);
            foreach (CharacterAnimState previous in all)
                Assert.AreEqual(CharacterAnimState.Crouch, CharacterAnimRules.StateFor(0f, true, previous), "still, " + previous);
            Assert.AreEqual(CharacterAnimState.CrouchWalk, CharacterAnimRules.StateFor(0.3f, true, CharacterAnimState.CrouchWalk), "hysteresis");
            Assert.AreEqual(CharacterAnimState.Crouch, CharacterAnimRules.StateFor(0.3f, true, CharacterAnimState.Crouch));
            Assert.AreEqual(CharacterAnimState.Idle, CharacterAnimRules.StateFor(0f, false, CharacterAnimState.Crouch), "standing up again");
            Assert.AreEqual(CharacterAnimState.Walk, CharacterAnimRules.StateFor(2f, false, CharacterAnimState.Crouch));
        }

        // Spec 025 amendment: clips like the library's on the 1.75 m body (m/s at rate 1, loop seconds).
        private static readonly GaitClip Walk = new GaitClip(0.7f, 1.333f);
        private static readonly GaitClip Jog = new GaitClip(3.9f, 0.933f);

        [Test]
        public void TargetCadence_IsLinearThroughTheThreePoints()
        {
            Assert.AreEqual(1.9f, CharacterAnimRules.TargetCadence(1f), 1e-4f);
            Assert.AreEqual(2.7f, CharacterAnimRules.TargetCadence(2.5f), 1e-4f);
            Assert.AreEqual(3.0f, CharacterAnimRules.TargetCadence(5f), 1e-4f);
            Assert.AreEqual(2.3f, CharacterAnimRules.TargetCadence(1.75f), 1e-4f);
            Assert.AreEqual(2.85f, CharacterAnimRules.TargetCadence(3.75f), 1e-4f);
            Assert.AreEqual(3.0f, CharacterAnimRules.TargetCadence(9f), 1e-4f, "held above the last point");
            Assert.AreEqual(1.9f - 0.5f * 0.8f / 1.5f, CharacterAnimRules.TargetCadence(0.5f), 1e-4f, "first slope continues");
            Assert.AreEqual(1.9f - 0.8f / 1.5f, CharacterAnimRules.TargetCadence(0f), 1e-4f, "standing still");
            Assert.AreEqual(CharacterAnimRules.TargetCadence(0f), CharacterAnimRules.TargetCadence(float.NaN), 1e-4f, "bad speed counts as 0");
            Assert.AreEqual(CharacterAnimRules.TargetCadence(0f), CharacterAnimRules.TargetCadence(-3f), 1e-4f);
        }

        [TestCase(1.0f)]
        [TestCase(2.0f)]
        [TestCase(2.5f)]
        [TestCase(3.75f)]
        [TestCase(5.0f)]
        public void Locomotion_HitsTheTargetCadence_WithPlantedFeet(float speed)
        {
            LocomotionBlend blend = CharacterAnimRules.Locomotion(speed, Walk, Jog);
            Assert.That(blend.Weight, Is.InRange(0f, 1f));
            Assert.Greater(blend.Rate, CharacterAnimRules.LocomotionMinRate);
            Assert.Less(blend.Rate, CharacterAnimRules.LocomotionMaxRate);
            Assert.AreEqual(CharacterAnimRules.TargetCadence(speed), blend.Cadence, 1e-3f, "cadence");
            Assert.AreEqual(speed, CharacterAnimRules.GroundSpeed(blend, Walk, Jog), 1e-3f, "feet planted");
        }

        [Test]
        public void Locomotion_JogWeightGrowsWithSpeed()
        {
            float last = -1f;
            for (float speed = 0.25f; speed <= 5f; speed += 0.25f)
            {
                float weight = CharacterAnimRules.Locomotion(speed, Walk, Jog).Weight;
                Assert.GreaterOrEqual(weight, last, "at " + speed);
                last = weight;
            }
            Assert.AreEqual(0f, CharacterAnimRules.Locomotion(0.5f, Walk, Jog).Weight, "slow: the walk alone");
            Assert.Greater(CharacterAnimRules.Locomotion(5f, Walk, Jog).Weight, 0.5f, "fast: mostly the jog");
        }

        [Test]
        public void Locomotion_EdgesAndBadInput()
        {
            LocomotionBlend still = CharacterAnimRules.Locomotion(0f, Walk, Jog);
            Assert.AreEqual(0f, still.Weight);
            Assert.AreEqual(CharacterAnimRules.LocomotionMinRate, still.Rate, "rate clamped at the bottom");
            LocomotionBlend fast = CharacterAnimRules.Locomotion(50f, Walk, Jog);
            Assert.AreEqual(1f, fast.Weight, "weight clamped");
            Assert.AreEqual(CharacterAnimRules.LocomotionMaxRate, fast.Rate, "rate clamped at the top");
            Assert.AreEqual(0f, CharacterAnimRules.Locomotion(float.NaN, Walk, Jog).Weight);
            Assert.AreEqual(0f, CharacterAnimRules.Locomotion(-2f, Walk, Jog).Weight);

            LocomotionBlend noWalk = CharacterAnimRules.Locomotion(2.5f, new GaitClip(0f, 1f), Jog);
            Assert.AreEqual(0f, noWalk.Weight);
            Assert.AreEqual(1f, noWalk.Rate, "unknown walk clip plays at 1");
            LocomotionBlend noJog = CharacterAnimRules.Locomotion(1.2f, Walk, new GaitClip(float.NaN, 1f));
            Assert.AreEqual(0f, noJog.Weight, "without a jog the walk plays alone");
            Assert.AreEqual(1.2f / Walk.Speed, noJog.Rate, 1e-4f);
            LocomotionBlend shortJog = CharacterAnimRules.Locomotion(1.2f, Walk, new GaitClip(0.5f, 0.5f));
            Assert.AreEqual(0f, shortJog.Weight, "a jog that does not step further is not blended");
        }

        [Test]
        public void CrouchRate_FollowsSpeedWithinLimits()
        {
            Assert.AreEqual(2f, CharacterAnimRules.CrouchRate(1f, 0.5f), 1e-5f);
            Assert.AreEqual(CharacterAnimRules.CrouchMaxRate, CharacterAnimRules.CrouchRate(5f, 0.5f));
            Assert.AreEqual(CharacterAnimRules.CrouchMinRate, CharacterAnimRules.CrouchRate(0.1f, 0.5f));
            Assert.AreEqual(1f, CharacterAnimRules.CrouchRate(1f, 0f));
            Assert.AreEqual(CharacterAnimRules.CrouchMinRate, CharacterAnimRules.CrouchRate(float.NaN, 0.5f));
            Assert.Less(MovementRules.CrouchSpeed / 0.545f, CharacterAnimRules.CrouchMaxRate, "sneaking fits the library's crouch walk");
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
