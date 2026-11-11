using Worktown.Sim.Core;
using Worktown.Sim.Revival;
using Xunit;
using R = Worktown.Sim.Tests.Revival.TestRevival;

namespace Worktown.Sim.Tests.Revival
{
    public class RevivalPropertyTests
    {
        [Theory]
        [InlineData(1UL)]
        [InlineData(65UL)]
        public void NeverBackUnderTenThousandSeededCalls(ulong seed) // AC6
        {
            foreach (RevivalCatalog c in new[] { R.Spec(), RevivalCatalog.CreateDefault(R.Town) })
            {
                var rng = new SeededRandom(seed);
                RevivalState s = RevivalEngine.Start(c);
                for (int call = 0; call < 10_000; call++)
                {
                    RevivalState before = s;
                    int op = rng.NextInt(0, 10);
                    if (op == 0) s = RevivalEngine.EnterStage(s, c, rng.NextInt(0, 7)).State;
                    else
                    {
                        string district = c.Districts[rng.NextInt(0, c.Districts.Count)].DistrictId;
                        s = RevivalEngine.AddMeals(s, c, district, rng.NextInt(1, 6), rng.NextInt(0, 40), rng.NextInt(1, 17)).State; // crew changes every call
                    }
                    for (int i = 0; i < s.Districts.Count; i++)
                    {
                        DistrictRevival was = before.Districts[i], now = s.Districts[i];
                        Assert.True(now.Level >= was.Level, $"call {call}: {now.DistrictId} dropped a level");
                        if (now.Level == was.Level) Assert.True(now.ProgressPpm >= was.ProgressPpm, $"call {call}: {now.DistrictId} lost progress");
                        Assert.InRange(now.ProgressPpm, 0, RevivalCatalog.Ppm);
                        if (now.Level == RevivalLevel.Thriving) Assert.Equal(RevivalCatalog.Ppm, now.ProgressPpm);
                    }
                }
            }
        }
    }
}
