using NUnit.Framework;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 045: a client is disconnected after many TooFast replies in a row (SpamGuard behind the RequestBucket).</summary>
    public class SpamGuardTests
    {
        [Test]
        public void TheLimitInARow_TripsOnce_AndStaysTripped()
        {
            var guard = new SpamGuard();
            for (int i = 1; i < SpamGuard.MaxTooFastInARow; i++)
                Assert.IsFalse(guard.Note(true), "refusal " + i);
            Assert.IsTrue(guard.Note(true), "the limit: kick now");
            Assert.IsTrue(guard.Tripped);
            Assert.IsFalse(guard.Note(true), "only once");
            Assert.IsFalse(guard.Note(false), "a passing request does not undo it");
            Assert.IsTrue(guard.Tripped);
        }

        [Test]
        public void APassingRequest_EndsTheStreak()
        {
            var guard = new SpamGuard(5);
            for (int i = 0; i < 4; i++) guard.Note(true);
            Assert.AreEqual(4, guard.Streak);
            Assert.IsFalse(guard.Note(false));
            Assert.AreEqual(0, guard.Streak);
            for (int i = 0; i < 4; i++) Assert.IsFalse(guard.Note(true), "a new streak starts at 0");
            Assert.IsTrue(guard.Note(true));
        }

        [Test]
        public void Reset_StartsANewLife()
        {
            var guard = new SpamGuard(2);
            guard.Note(true);
            guard.Note(true);
            Assert.IsTrue(guard.Tripped);
            guard.Reset();
            Assert.IsFalse(guard.Tripped);
            Assert.AreEqual(0, guard.Streak);
            Assert.IsFalse(guard.Note(true));
        }

        [Test]
        public void ABadLimit_FallsBackToTheDefault()
        {
            var guard = new SpamGuard(0);
            for (int i = 1; i < SpamGuard.MaxTooFastInARow; i++) Assert.IsFalse(guard.Note(true));
            Assert.IsTrue(guard.Note(true));
        }

        /// <summary>The server's own pair: the bucket decides TooFast, the guard counts it. Returns whether the guard tripped.</summary>
        private static bool Serve(RequestBucket bucket, SpamGuard guard, double at)
        {
            bool limited = !bucket.TryTake(at);
            return guard.Note(limited);
        }

        [Test]
        public void AnHonestClient_IsNeverKicked_EvenWhenAFiveSecondStallBunchesItsRequests()
        {
            var bucket = new RequestBucket();
            var guard = new SpamGuard();
            double interval = PickupRules.ClientRequestInterval(1f / 30f);
            double t = 0d;
            // Clicking as fast as the client allows for a minute.
            for (int i = 0; i < 250; i++, t += interval) Assert.IsFalse(Serve(bucket, guard, t));
            // A 5 s network stall: everything sent meanwhile arrives within one tick.
            double stallEnd = t + 5d;
            for (double sent = t; sent < stallEnd; sent += interval) Assert.IsFalse(Serve(bucket, guard, stallEnd), "after the stall");
            Assert.Greater(guard.Streak, 10, "many were refused in a row");
            for (int i = 0; i < 50; i++) Assert.IsFalse(Serve(bucket, guard, stallEnd + (i + 1) * interval), "back to normal");
            Assert.IsFalse(guard.Tripped);
        }

        [Test]
        public void AFlood_IsKicked_WithinAFractionOfASecond_ButAClientTwiceTooFastIsOnlyThrottled()
        {
            var bucket = new RequestBucket();
            var guard = new SpamGuard();
            double t = 0d;
            while (!Serve(bucket, guard, t))
            {
                t += 1d / 1000d; // 1000 requests per second
                Assert.Less(t, 0.5d, "kicked in time");
            }
            Assert.IsTrue(guard.Tripped);

            var bucket2 = new RequestBucket();
            var guard2 = new SpamGuard();
            for (int i = 0; i < 3000; i++) Assert.IsFalse(Serve(bucket2, guard2, i * PickupRules.ServerRequestInterval * 0.5d), "10 per second");
            Assert.IsFalse(guard2.Tripped);
        }
    }
}
