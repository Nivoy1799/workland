using System;
using System.Collections.Generic;
using Worktown.Sim.Core;

namespace Worktown.Sim.Clock
{
    /// <summary>Strikes of lightning on the glass of a storm day, as seconds of the day.</summary>
    public static class LightningSchedule
    {
        public const int MinGapSeconds = 4;

        public static IReadOnlyList<int> Strikes(DayWeather w, ClockCatalog c)
        {
            var result = new List<int>();
            if (w.Kind != DayKind.Storm) return result;
            int from = TownClock.DaySecondOf(c, w.StormFromMinute);
            int to = TownClock.DaySecondOf(c, w.StormToMinute);
            var rng = new SeededRandom((ulong)w.LightningSeed);
            int t = from + Math.Max(MinGapSeconds, rng.NextInt(20, 91));
            while (t < to)
            {
                result.Add(t);
                t += Math.Max(MinGapSeconds, rng.NextInt(20, 91));
            }
            return result;
        }
    }
}
