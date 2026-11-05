using System;
using Worktown.Sim.Core;
using Xunit;

namespace Worktown.Sim.Tests.Core
{
    public class SeededRandomTests
    {
        [Fact]
        public void SameSeed_SameSequence()
        {
            var a = new SeededRandom(42);
            var b = new SeededRandom(42);
            for (int i = 0; i < 20; i++) Assert.Equal(a.NextUInt64(), b.NextUInt64());
        }

        [Fact]
        public void SameSeed_SameNextIntAndNextFloatSequences()
        {
            var a = new SeededRandom(99);
            var b = new SeededRandom(99);
            for (int i = 0; i < 50; i++) Assert.Equal(a.NextInt(-10, 10), b.NextInt(-10, 10));
            for (int i = 0; i < 50; i++) Assert.Equal(a.NextFloat(), b.NextFloat());
        }

        [Fact]
        public void Seed0_PinnedOutputs()
        {
            // Reference SplitMix64 values for seed 0.
            var r = new SeededRandom(0);
            Assert.Equal(0xE220A8397B1DCDAFUL, r.NextUInt64());
            Assert.Equal(0x6E789E6AA1B965F4UL, r.NextUInt64());
            Assert.Equal(0x06C45D188009454FUL, r.NextUInt64());
        }

        [Fact]
        public void NextFloat_InRange()
        {
            var r = new SeededRandom(7);
            for (int i = 0; i < 1000; i++) Assert.InRange(r.NextFloat(), 0f, 0.99999994f);
        }

        [Fact]
        public void NextInt_InRangeAndValidates()
        {
            var r = new SeededRandom(7);
            for (int i = 0; i < 1000; i++) Assert.InRange(r.NextInt(-3, 4), -3, 3);
            Assert.Throws<ArgumentOutOfRangeException>(() => r.NextInt(5, 5));
        }
    }
}
