using NUnit.Framework;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Review of spec 027b: the server's rate limit per player is a token bucket (RequestBucket): a burst of two, one token
    /// per PickupRules.ServerRequestInterval. Bunched arrivals of well-spaced requests pass; the average stays capped.
    /// </summary>
    public class RequestBucketTests
    {
        [Test]
        public void Bucket_LetsABurstOfTwoThrough_ThenOneEveryInterval()
        {
            var bucket = new RequestBucket(0.2f);
            Assert.IsTrue(bucket.TryTake(10f));
            Assert.IsTrue(bucket.TryTake(10.01f), "two bunched requests both pass");
            Assert.IsFalse(bucket.TryTake(10.02f), "a third is refused");
            Assert.IsFalse(bucket.TryTake(10.1f));
            Assert.IsTrue(bucket.TryTake(10.21f), "one token back after the interval");
        }

        [Test]
        public void Bucket_PassesRequestsSentApartButArrivingBunched()
        {
            // Sent every 0.243 s (the client spacing); jitter makes some arrive 0.15 s apart, one pair together.
            var bucket = new RequestBucket(0.2f);
            float[] arrivals = { 0f, 0.243f, 0.39f, 0.729f, 0.972f, 0.972f, 1.458f, 1.6f };
            foreach (float t in arrivals) Assert.IsTrue(bucket.TryTake(t), "arrival at " + t);
        }

        [Test]
        public void Bucket_CapsTheAverageRate_AndTheSavings()
        {
            var bucket = new RequestBucket(0.2f);
            int passed = 0;
            for (int i = 0; i < 1000; i++)
                if (bucket.TryTake(i * 0.01f)) passed++; // spam at 100 per second for 10 s
            Assert.LessOrEqual(passed, 2 + 50 + 1, "the burst plus one per 0.2 s");
            Assert.GreaterOrEqual(passed, 50);
            var idle = new RequestBucket(0.2f);
            idle.TryTake(0f);
            idle.TryTake(100f); // a long pause saves at most the burst
            Assert.LessOrEqual(idle.Tokens, RequestBucket.Burst);
            Assert.IsTrue(idle.TryTake(100f));
            Assert.IsFalse(idle.TryTake(100f));
        }

        [Test]
        public void Bucket_KeepsItsPrecisionAfterDaysOfUptime()
        {
            // Second review: double time. Three days in, 0.2 s apart still refills exactly one token.
            var bucket = new RequestBucket(PickupRules.ServerRequestInterval);
            double t = 3 * 24 * 3600.0;
            Assert.IsTrue(bucket.TryTake(t));
            Assert.IsTrue(bucket.TryTake(t));
            Assert.IsFalse(bucket.TryTake(t + 0.1));
            Assert.IsTrue(bucket.TryTake(t + 0.2), "one token after exactly one interval");
            Assert.IsFalse(bucket.TryTake(double.NaN));
        }
    }
}
