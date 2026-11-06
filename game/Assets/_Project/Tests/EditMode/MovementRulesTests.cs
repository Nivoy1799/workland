using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Player;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 010 AC1.</summary>
    public class MovementRulesTests
    {
        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.Less((expected - actual).magnitude, 0.001f, "expected " + expected + " got " + actual);
        }

        [TestCase(0f, 1f, 0f, 1f)]       // W
        [TestCase(0f, -1f, 0f, -1f)]     // S
        [TestCase(1f, 0f, 1f, 0f)]       // D
        [TestCase(-1f, 0f, -1f, 0f)]     // A
        public void CameraFacingNorth_KeysMapToWorldAxes(float x, float y, float wx, float wz)
        {
            AssertVector(new Vector3(wx, 0f, wz), MovementRules.WorldDirection(new Vector2(x, y), 0f));
        }

        [Test]
        public void Diagonal_IsNotFaster()
        {
            Assert.AreEqual(1f, MovementRules.WorldDirection(new Vector2(1f, 1f), 0f).magnitude, 0.001f);
        }

        [Test]
        public void ForwardFollowsTheCamera()
        {
            // Camera looking east (yaw 90): W walks east, D walks south.
            AssertVector(Vector3.right, MovementRules.WorldDirection(new Vector2(0f, 1f), 90f));
            AssertVector(Vector3.back, MovementRules.WorldDirection(new Vector2(1f, 0f), 90f));
        }

        [Test]
        public void BadInput_IsClampedOrZeroed()
        {
            Assert.AreEqual(1f, MovementRules.SanitizeMove(new Vector2(50f, 0f)).magnitude, 0.001f);
            Assert.AreEqual(Vector2.zero, MovementRules.SanitizeMove(new Vector2(float.NaN, 1f)));
            Assert.AreEqual(Vector2.zero, MovementRules.SanitizeMove(new Vector2(0f, float.PositiveInfinity)));
            Assert.AreEqual(0f, MovementRules.SanitizeYaw(float.NaN));
            Assert.AreEqual(10f, MovementRules.SanitizeYaw(370f), 0.001f);
        }

        [Test]
        public void Speeds_ByStance()
        {
            Assert.AreEqual(MovementRules.WalkSpeed, MovementRules.MaxSpeed(false, false));
            Assert.AreEqual(MovementRules.SprintSpeed, MovementRules.MaxSpeed(true, false));
            Assert.AreEqual(MovementRules.CrouchSpeed, MovementRules.MaxSpeed(true, true), "no sprint while crouched");
            Assert.Greater(MovementRules.SprintSpeed, MovementRules.WalkSpeed);
            Assert.Less(MovementRules.CrouchSpeed, MovementRules.WalkSpeed);
        }

        [Test]
        public void Jump_ReachesConfiguredHeight()
        {
            float g = 9.81f;
            float v = MovementRules.JumpSpeed(g);
            Assert.AreEqual(MovementRules.JumpHeight, v * v / (2f * g), 0.001f);
            Assert.AreEqual(v, MovementRules.JumpSpeed(-g), 0.001f, "sign of gravity does not matter");
        }

        [TestCase(true, true, false, true)]
        [TestCase(true, false, false, false)]   // in the air
        [TestCase(true, true, true, false)]     // crouched
        [TestCase(false, true, false, false)]   // no input
        public void CanJump_OnlyFromGroundStanding(bool wants, bool grounded, bool crouched, bool expected)
        {
            Assert.AreEqual(expected, MovementRules.CanJump(wants, grounded, crouched));
        }

        [Test]
        public void Acceleration_IsLimitedPerTick()
        {
            Vector3 v = MovementRules.Accelerate(Vector3.zero, Vector3.forward * 4f, 1f / 30f);
            Assert.AreEqual(MovementRules.Acceleration / 30f, v.magnitude, 0.001f);
            Vector3 stopped = MovementRules.Accelerate(Vector3.forward * 0.5f, Vector3.zero, 1f / 30f);
            Assert.AreEqual(0f, stopped.magnitude, 0.001f);
        }

        [Test]
        public void TurnRate_LimitsYawChangePerTick()
        {
            float delta = 1f / 30f;
            float turned = MovementRules.TurnTowards(0f, Vector3.back, delta); // wants 180
            Assert.AreEqual(MovementRules.TurnRate * delta, Mathf.Abs(Mathf.DeltaAngle(0f, turned)), 0.01f);
        }

        [Test]
        public void NoDirection_KeepsYaw()
        {
            Assert.AreEqual(45f, MovementRules.TurnTowards(45f, Vector3.zero, 1f), 0.001f);
        }
    }
}
