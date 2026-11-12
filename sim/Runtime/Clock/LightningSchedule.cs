using System.Collections.Generic;
using Worktown.Sim.Core;

namespace Worktown.Sim.Clock
{
    /// <summary>Strikes of lightning on the glass of a storm day, as seconds of the day.</summary>
    public static class LightningSchedule
    {
        /// <summary>Smallest gap between two strikes, in seconds.</summary>
        public const int GapFromSeconds = 20;

        /// <summary>One past the largest gap, in seconds.</summary>
        public const int GapToSecondsExclusive = 91;

        public static IReadOnlyList<int> Strikes(DayWeather w, ClockCatalog c)
        {
            var result = new List<int>();
            if (w.Kind != DayKind.Storm) return result;
            int from = TownClock.DaySecondOf(c, w.StormFromMinute);
            int to = TownClock.DaySecondOf(c, w.StormToMinute);
            var rng = new SeededRandom((ulong)w.LightningSeed);
            int t = from + rng.NextInt(GapFromSeconds, GapToSecondsExclusive);
            while (t < to)
            {
                result.Add(t);
                t += rng.NextInt(GapFromSeconds, GapToSecondsExclusive);
            }
            return result;
        }
    }
}
