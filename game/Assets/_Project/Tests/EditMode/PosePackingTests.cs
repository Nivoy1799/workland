using NUnit.Framework;
using Worktown.Game.Layout;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 062a: a piece's pose in one long (x and z 24 bits signed, turns 2, length 14).</summary>
    public class PosePackingTests
    {
        private static void RoundTrip(int x, int z, int turns, int length)
        {
            PosePacking.Unpack(PosePacking.Pack(x, z, turns, length), out int x2, out int z2, out int t2, out int l2);
            Assert.AreEqual(x, x2, "x");
            Assert.AreEqual(z, z2, "z");
            Assert.AreEqual(turns, t2, "turns");
            Assert.AreEqual(length, l2, "length");
        }

        [Test]
        public void Pack_RoundTrips_TodaysPoses_AndTheLimits()
        {
            RoundTrip(11640, 11850, 2, 0);
            RoundTrip(-2000, 13400, 3, 0); // the bin, west of the origin
            RoundTrip(4420, 11850, 2, 6760);
            RoundTrip(0, 0, 0, 0);
            RoundTrip(PosePacking.MaxCoordinate, PosePacking.MinCoordinate, 1, PosePacking.MaxLength);
            RoundTrip(PosePacking.MinCoordinate, PosePacking.MaxCoordinate, 3, 1);
        }

        [Test]
        public void OutOfRange_Clamps_AndTurnsWrap()
        {
            PosePacking.Unpack(PosePacking.Pack(int.MaxValue, int.MinValue, 6, 99999), out int x, out int z, out int t, out int l);
            Assert.AreEqual(PosePacking.MaxCoordinate, x);
            Assert.AreEqual(PosePacking.MinCoordinate, z);
            Assert.AreEqual(2, t);
            Assert.AreEqual(PosePacking.MaxLength, l);
            PosePacking.Unpack(PosePacking.Pack(0, 0, -1, -5), out _, out _, out t, out l);
            Assert.AreEqual(3, t);
            Assert.AreEqual(0, l);
            Assert.AreEqual(16383, PosePacking.MaxLength);
            Assert.AreEqual(8388607, PosePacking.MaxCoordinate);
        }

        [Test]
        public void Yaw_IsAQuarterTurnEach()
        {
            Assert.AreEqual(0f, PosePacking.Yaw(0));
            Assert.AreEqual(90f, PosePacking.Yaw(1));
            Assert.AreEqual(180f, PosePacking.Yaw(2));
            Assert.AreEqual(270f, PosePacking.Yaw(3));
            Assert.AreEqual(270f, PosePacking.Yaw(-1));
        }
    }
}
