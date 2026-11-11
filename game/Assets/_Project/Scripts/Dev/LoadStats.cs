using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 045 (the spec 004 load test), pure: the per-second numbers of a metrics line (average, 95th percentile, maximum of a
    /// second's samples) and the line itself (one flat JSON object, invariant culture, so tools/bots/run-loadtest.sh can read it).
    /// </summary>
    public static class LoadStats
    {
        /// <summary>The <paramref name="percent"/> percentile (nearest rank) of <paramref name="samples"/>; 0 for none. Sorts the list.</summary>
        public static float Percentile(List<float> samples, float percent)
        {
            if (samples == null || samples.Count == 0) return 0f;
            samples.Sort();
            float p = Math.Max(0f, Math.Min(100f, percent));
            int rank = (int)Math.Ceiling(p / 100f * samples.Count);
            return samples[Math.Max(0, Math.Min(samples.Count - 1, rank - 1))];
        }

        public static float Average(List<float> samples)
        {
            if (samples == null || samples.Count == 0) return 0f;
            double sum = 0d;
            foreach (float s in samples) sum += s;
            return (float)(sum / samples.Count);
        }

        public static float Max(List<float> samples)
        {
            if (samples == null || samples.Count == 0) return 0f;
            float max = float.MinValue;
            foreach (float s in samples) if (s > max) max = s;
            return max;
        }

        /// <summary>
        /// One JSON line of name/number pairs, numbers with up to 3 decimals in invariant culture: {"t":1,"clients":16,...}.
        /// A NaN or infinite value is written as 0 (JSON has neither).
        /// </summary>
        public static string Line(IReadOnlyList<KeyValuePair<string, double>> fields)
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            for (int i = 0; fields != null && i < fields.Count; i++)
            {
                if (i > 0) sb.Append(',');
                double v = fields[i].Value;
                if (double.IsNaN(v) || double.IsInfinity(v)) v = 0d;
                sb.Append('"').Append(fields[i].Key).Append("\":").Append(Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture));
            }
            sb.Append('}');
            return sb.ToString();
        }
    }
}
