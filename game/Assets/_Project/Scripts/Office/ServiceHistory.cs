using System;
using System.Collections.Generic;

namespace Worktown.Game.Office
{
    /// <summary>
    /// Spec 059, pure: the services the office terminal remembers: the newest <see cref="Kept"/> (newest last), the last <see cref="Shown"/>
    /// on Results page 2, and "usual" is the mean of up to <see cref="Usual"/> services before the last one (open question 5's default).
    /// </summary>
    public static class ServiceHistory
    {
        public const int Kept = 8;
        public const int Shown = 5;
        public const int Usual = 4;

        /// <summary><paramref name="history"/> with <paramref name="record"/> appended, the oldest dropped past <see cref="Kept"/>.</summary>
        public static ServiceRecord[] Push(IReadOnlyList<ServiceRecord> history, ServiceRecord record)
        {
            var list = new List<ServiceRecord>();
            if (history != null) foreach (ServiceRecord r in history) list.Add(r);
            list.Add(record);
            while (list.Count > Kept) list.RemoveAt(0);
            return list.ToArray();
        }

        /// <summary>The newest <see cref="Kept"/> of <paramref name="history"/> (a loaded save's list).</summary>
        public static ServiceRecord[] Newest(IReadOnlyList<ServiceRecord> history, int count = Kept)
        {
            if (history == null || history.Count == 0 || count <= 0) return Array.Empty<ServiceRecord>();
            int n = Math.Min(count, history.Count);
            var list = new ServiceRecord[n];
            for (int i = 0; i < n; i++) list[i] = history[history.Count - n + i];
            return list;
        }

        /// <summary>The last service, when there is one.</summary>
        public static bool TryLast(IReadOnlyList<ServiceRecord> history, out ServiceRecord last)
        {
            last = default;
            if (history == null || history.Count == 0) return false;
            last = history[history.Count - 1];
            return true;
        }

        /// <summary>
        /// The mean (rounded half up) of <paramref name="value"/> over up to <see cref="Usual"/> services before the last one; values below 0
        /// (unknown) are left out. False when there is none to compare with.
        /// </summary>
        public static bool TryUsual(IReadOnlyList<ServiceRecord> history, Func<ServiceRecord, int> value, out int mean)
        {
            mean = 0;
            if (history == null || value == null || history.Count < 2) return false;
            long sum = 0;
            int n = 0;
            for (int i = history.Count - 2; i >= 0 && i >= history.Count - 1 - Usual; i--)
            {
                int v = value(history[i]);
                if (v < 0) continue;
                sum += v;
                n++;
            }
            if (n == 0) return false;
            mean = (int)((sum * 2 + n) / (2 * n));
            return true;
        }
    }
}
