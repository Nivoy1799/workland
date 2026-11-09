using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Art;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 027d: the sign hangs on the front (local -z) face, low and clear of the worktop and the knobs.</summary>
    public class StationSignTests
    {
        [Test]
        public void Sign_SitsJustInFrontOfTheFrontFace_BelowTheTop()
        {
            Vector3 p = StationSign.LocalPosition(0.9f, -0.6f);
            Assert.Less(p.z, -0.6f, "in front of the front face (z -0.6)");
            Assert.Greater(p.z, -0.7f, "close to it");
            Assert.AreEqual(0f, p.x);
            Assert.Less(p.y, 0.45f - StationSign.BelowTop + 0.001f, "below the top");
            Assert.Greater(p.y, -0.45f, "above the floor");
        }
    }
}
