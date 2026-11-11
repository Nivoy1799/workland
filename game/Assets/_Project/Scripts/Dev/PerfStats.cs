using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 049, pure: the numbers of one profiling-tour stop (frame times avg, p95, p99, max in ms; GC bytes allocated per frame) and
    /// its JSON line for scripts/perf-check.sh: <c>{"station":"Fryer","kind":"Fryer","index":3,"frames":412,"avg_ms":...}</c>
    /// (invariant culture, names without quotes or backslashes).
    /// </summary>
    public static class PerfStats
    {
        public readonly struct Summary
        {
            public Summary(int frames, float avgMs, float p95Ms, float p99Ms, float maxMs, double gcBytesPerFrame)
            {
                Frames = frames;
                AvgMs = avgMs;
                P95Ms = p95Ms;
                P99Ms = p99Ms;
                MaxMs = maxMs;
                GcBytesPerFrame = gcBytesPerFrame;
            }

            public int Frames { get; }
            public float AvgMs { get; }
            public float P95Ms { get; }
            public float P99Ms { get; }
            public float MaxMs { get; }
            public double GcBytesPerFrame { get; }
        }

        /// <summary>The summary of a stop's frame times (ms) and per-frame GC bytes. Copies the lists (the caller's stay in order).</summary>
        public static Summary Summarize(IReadOnlyList<float> frameMs, IReadOnlyList<long> gcBytes)
        {
            var frames = new List<float>(frameMs ?? Array.Empty<float>());
            double gc = 0d;
            int gcCount = gcBytes?.Count ?? 0;
            for (int i = 0; i < gcCount; i++) gc += gcBytes[i];
            float avg = LoadStats.Average(frames);
            float max = LoadStats.Max(frames);
            float p95 = LoadStats.Percentile(frames, 95f);
            float p99 = LoadStats.Percentile(frames, 99f);
            return new Summary(frames.Count, avg, p95, p99, max, gcCount > 0 ? gc / gcCount : 0d);
        }

        /// <summary>One JSON line for a stop (<paramref name="station"/> "ALL" for the whole tour).</summary>
        public static string Line(string station, string kind, int index, int players, Summary s)
        {
            var sb = new StringBuilder(200);
            sb.Append("{\"station\":\"").Append(Clean(station)).Append("\",\"kind\":\"").Append(Clean(kind)).Append('"');
            Num(sb, "index", index);
            Num(sb, "players", players);
            Num(sb, "frames", s.Frames);
            Num(sb, "avg_ms", s.AvgMs);
            Num(sb, "p95_ms", s.P95Ms);
            Num(sb, "p99_ms", s.P99Ms);
            Num(sb, "max_ms", s.MaxMs);
            Num(sb, "gc_bytes_per_frame", s.GcBytesPerFrame);
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>A name safe inside a JSON string: no quotes, backslashes or control characters.</summary>
        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
                sb.Append(c == '"' || c == '\\' || char.IsControl(c) ? '\'' : c);
            return sb.ToString();
        }

        private static void Num(StringBuilder sb, string name, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0d;
            sb.Append(",\"").Append(name).Append("\":").Append(Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture));
        }
    }
}
