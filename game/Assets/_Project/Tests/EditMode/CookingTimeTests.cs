using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 016 AC2: tick to sim time, clamping and the "too short" rule.</summary>
    public class CookingTimeTests
    {
        private static ParamDef TimeOf(string opId)
        {
            Assert.IsTrue(OperationCatalog.CreateDefault().TryGet(opId, out OperationDef op));
            Assert.IsTrue(CookingTime.TryGetTimeParam(op, out ParamDef time));
            return time;
        }

        [Test]
        public void SevenHundredFiftyTicks_AtThirty_AreExactly150SimSeconds()
        {
            Assert.AreEqual(150d, CookingTime.SimSeconds(750, 30d));
            Assert.AreEqual(0d, CookingTime.SimSeconds(750, 0d), "no tick rate, no time");
        }

        [Test]
        public void LongFry_ClampsTo600()
        {
            ParamDef fry = TimeOf("fry");
            Assert.AreEqual(1800d, CookingTime.SimSeconds(9000, 30d));
            Assert.AreEqual(600f, CookingTime.Clamp(fry, CookingTime.SimSeconds(9000, 30d)));
            Assert.AreEqual(150f, CookingTime.Clamp(fry, 150d));
        }

        [Test]
        public void BelowTheMinimum_IsTooShort()
        {
            ParamDef fry = TimeOf("fry");
            Assert.IsTrue(CookingTime.IsTooShort(fry, 9.9d));
            Assert.IsFalse(CookingTime.IsTooShort(fry, 10d));
            Assert.IsTrue(CookingTime.IsTooShort(TimeOf("boil"), 29d));
        }

        [Test]
        public void ElapsedTicks_NeverNegative()
        {
            Assert.AreEqual(5u, CookingTime.ElapsedTicks(10, 15));
            Assert.AreEqual(0u, CookingTime.ElapsedTicks(15, 10));
        }

        [TestCase(5d, "")]
        [TestCase(10d, "short")]
        [TestCase(59d, "short")]
        [TestCase(60d, "medium")]
        [TestCase(180d, "long")]
        [TestCase(600d, "long")]
        public void PanBuckets_FollowTheSim(double seconds, string bucket)
        {
            Assert.AreEqual(bucket, CookingTime.BucketId(TimeOf("fry"), seconds));
        }

        [Test]
        public void CutHasNoTimeParam()
        {
            Assert.IsTrue(OperationCatalog.CreateDefault().TryGet("cut", out OperationDef cut));
            Assert.IsFalse(CookingTime.TryGetTimeParam(cut, out _));
            Assert.IsFalse(CookingTime.TryGetTimeParam(null, out _));
        }
    }
}
