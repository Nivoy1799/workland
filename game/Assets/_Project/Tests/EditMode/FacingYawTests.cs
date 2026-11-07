using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Player;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 018 AC3: in first person the body faces the camera, in third person the move direction.</summary>
    public class FacingYawTests
    {
        private const float Delta = 1f / 30f;

        [TestCase(0f, 1f)]    // forward
        [TestCase(0f, -1f)]   // backwards
        [TestCase(1f, 0f)]    // strafe right
        [TestCase(-1f, 0f)]   // strafe left
        [TestCase(0f, 0f)]    // standing still
        public void FirstPerson_FacesTheCamera(float x, float y)
        {
            const float cameraYaw = 135f;
            Vector3 direction = MovementRules.WorldDirection(new Vector2(x, y), cameraYaw);
            Assert.AreEqual(cameraYaw, MovementRules.FacingYaw(true, 10f, cameraYaw, direction, Delta), 0.0001f);
        }

        [Test]
        public void FirstPerson_WrapsTheCameraYaw()
        {
            Assert.AreEqual(270f, MovementRules.FacingYaw(true, 0f, -90f, Vector3.zero, Delta), 0.0001f);
            Assert.AreEqual(30f, MovementRules.FacingYaw(true, 0f, 750f, Vector3.zero, Delta), 0.0001f);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void FirstPerson_NotFiniteCameraYaw_IsSanitized(float cameraYaw)
        {
            Assert.AreEqual(0f, MovementRules.FacingYaw(true, 45f, cameraYaw, Vector3.forward, Delta));
        }

        [Test]
        public void FirstPerson_HugeCameraYaw_StaysInRange()
        {
            float yaw = MovementRules.FacingYaw(true, 0f, 1e9f, Vector3.zero, Delta);
            Assert.GreaterOrEqual(yaw, 0f);
            Assert.Less(yaw, 360f);
        }

        [TestCase(0f, 0f, 1f)]
        [TestCase(0f, 1f, 0f)]
        [TestCase(90f, -1f, 0f)]
        [TestCase(350f, 0f, -1f)]
        [TestCase(30f, 0f, 0f)]
        public void ThirdPerson_MatchesTurnTowards(float currentYaw, float dx, float dz)
        {
            var direction = new Vector3(dx, 0f, dz);
            Assert.AreEqual(MovementRules.TurnTowards(currentYaw, direction, Delta),
                MovementRules.FacingYaw(false, currentYaw, 200f, direction, Delta));
        }
    }
}
