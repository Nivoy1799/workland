using NUnit.Framework;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 045 (045 review: a sliding window): a client is disconnected when its TooFast replies within 10 s reach 300
    /// (SpamGuard behind the RequestBucket). All times are injected.
    /// </summary>
    public class SpamGuardTests
    {
        [Test]
        public void TheLimitInTheWindow_TripsOnce_AndStaysTripped()
        {
            var guard = new SpamGuard();
            for (int i = 1; i < SpamGuard.MaxTooFastInWindow; i++)
                Assert.IsFalse(guard.Note(true, i * 0.01d), "refusal " + i);
            Assert.IsTrue(guard.Note(true, 3d), "the limit: kick now");
            Assert.IsTrue(guard.Tripped);
            Assert.IsFalse(guard.Note(true, 3.1d), "only once");
            Assert.IsFalse(guard.Note(false, 3.2d), "a passing request does not undo it");
            Assert.IsTrue(guard.Tripped);
        }

        [Test]
        public void APassingRequest_DoesNotResetTheCount_ButOldSecondsLeaveTheWindow()
        {
            var guard = new SpamGuard(5);
            for (int i = 0; i < 4; i++) guard.Note(true, 0.1d * i);
            Assert.IsFalse(guard.Note(false, 0.5d));
            Assert.AreEqual(4, guard.InWindow, "passing requests do not reset the count");
            Assert.IsFalse(guard.Note(false, 10.5d));
            Assert.AreEqual(0, guard.InWindow, "10 s later the old second is out of the window");
            for (int i = 0; i < 4; i++) Assert.IsFalse(guard.Note(true, 11d + 0.1d * i));
            Assert.IsTrue(guard.Note(true, 11.5d));
        }

        [Test]
        public void Reset_StartsANewLife()
        {
            var guard = new SpamGuard(2);
            guard.Note(true, 0d);
            guard.Note(true, 0.1d);
            Assert.IsTrue(guard.Tripped);
            guard.Reset();
            Assert.IsFalse(guard.Tripped);
            Assert.AreEqual(0, guard.InWindow);
            Assert.IsFalse(guard.Note(true, 0.2d));
        }

        [Test]
        public void ABadLimit_FallsBackToTheDefault()
        {
            var guard = new SpamGuard(0);
            for (int i = 1; i < SpamGuard.MaxTooFastInWindow; i++) Assert.IsFalse(guard.Note(true, i * 0.001d));
            Assert.IsTrue(guard.Note(true, 1d));
        }

        [Test]
        public void TheHost_IsNeverKicked()
        {
            Assert.IsTrue(SpamGuard.ShouldKick(true, isLocal: false));
            Assert.IsFalse(SpamGuard.ShouldKick(true, isLocal: true), "the host's own player");
            Assert.IsFalse(SpamGuard.ShouldKick(false, isLocal: false));
            Assert.IsFalse(SpamGuard.ShouldKick(false, isLocal: true));
        }

        /// <summary>The server's own pair: the bucket decides TooFast, the guard counts it. Returns whether the guard tripped.</summary>
        private static bool Serve(RequestBucket bucket, SpamGuard guard, double at)
        {
            bool limited = !bucket.TryTake(at);
            return guard.Note(limited, at);
        }

        /// <summary>Requests at <paramref name="perSecond"/> from t = 0; the time of the kick, or -1 if none within <paramref name="seconds"/>.</summary>
        private static double KickTime(double perSecond, double seconds)
        {
            var bucket = new RequestBucket();
            var guard = new SpamGuard();
            int count = (int)(perSecond * seconds);
            for (int i = 0; i < count; i++)
            {
                double t = i / perSecond;
                if (Serve(bucket, guard, t)) return t;
            }
            return -1d;
        }

        [Test]
        public void FiftyPerSecond_IsKickedWithinTenSeconds()
        {
            double t = KickTime(50d, 20d);
            Assert.GreaterOrEqual(t, 0d, "kicked");
            Assert.Less(t, 10d);
        }

        [Test]
        public void AHundredPerSecond_IsKickedWithinTenSeconds()
        {
            double t = KickTime(100d, 20d);
            Assert.GreaterOrEqual(t, 0d, "kicked");
            Assert.Less(t, 10d);
        }

        [Test]
        public void AThousandPerSecond_IsStillKicked_WithinASecond()
        {
            double t = KickTime(1000d, 5d);
            Assert.GreaterOrEqual(t, 0d, "kicked");
            Assert.Less(t, 1d);
        }

        [Test]
        public void AnHonestMasherAtTenPerSecond_IsNeverKicked_OverAMinute()
        {
            Assert.Less(KickTime(10d, 60d), 0d);
        }

        [Test]
        public void ATenSecondStall_FollowedByItsQueuedBurst_IsNeverKicked()
        {
            var bucket = new RequestBucket();
            var guard = new SpamGuard();
            double t = 0d;
            for (int i = 0; i < 100; i++, t += 0.1d) Assert.IsFalse(Serve(bucket, guard, t), "mashing at 10/s");
            // A 10 s network stall: the ~100 requests sent meanwhile arrive within one tick.
            double stallEnd = t + 10d;
            for (int i = 0; i < 100; i++) Assert.IsFalse(Serve(bucket, guard, stallEnd), "after the stall");
            Assert.Greater(guard.InWindow, 90, "most were refused");
            for (int i = 0; i < 300; i++) Assert.IsFalse(Serve(bucket, guard, stallEnd + (i + 1) * 0.1d), "back to mashing");
            Assert.IsFalse(guard.Tripped);
        }

        [Test]
        public void AClientAtTheBucketsOwnPace_IsOnlyThrottled()
        {
            var bucket = new RequestBucket();
            var guard = new SpamGuard();
            double interval = PickupRules.ClientRequestInterval(1f / 30f);
            for (int i = 0; i < 1000; i++) Assert.IsFalse(Serve(bucket, guard, i * interval));
            Assert.AreEqual(0, guard.InWindow);
        }
    }
}
