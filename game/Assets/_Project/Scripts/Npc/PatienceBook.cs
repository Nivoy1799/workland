using System.Collections.Generic;
using Worktown.Sim.Economy;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// Spec 032, server, pure: every waiting guest's patience. A guest's patience drains only while it is in the queue and stands
    /// at its slot (<see cref="Tick"/> asks); a guest that left the queue (handed its food, waiting at the register) is not ticked,
    /// so its patience never drains there. Out of patience, a guest is reported once and forgotten. The ring's byte is reported
    /// when due (at most every <see cref="SyncSeconds"/> per guest). Time comes in from the caller.
    /// </summary>
    public sealed class PatienceBook
    {
        public const float SyncSeconds = 0.25f;

        private sealed class Entry
        {
            public float Total;
            public float Waited;
            public float NextSync;
            public int LastByte = -1;
        }

        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();

        public int Count => _entries.Count;

        public void Add(int guest, float totalSeconds) => _entries[guest] = new Entry { Total = totalSeconds };

        public bool Remove(int guest) => _entries.Remove(guest);

        public void Clear() => _entries.Clear();

        public bool Contains(int guest) => _entries.ContainsKey(guest);

        /// <summary>The seconds waited and the whole patience of <paramref name="guest"/> (0, 0 when unknown).</summary>
        public void Get(int guest, out float waited, out float total)
        {
            waited = total = 0f;
            if (!_entries.TryGetValue(guest, out Entry e)) return;
            waited = e.Waited;
            total = e.Total;
        }

        /// <summary>
        /// One server frame of <paramref name="dt"/> s at <paramref name="now"/>: each guest of <paramref name="queue"/> standing at its
        /// slot (<paramref name="atSlot"/>) waits <paramref name="dt"/> longer. The guests whose patience ran out go into
        /// <paramref name="ranOut"/> (and are forgotten); the ring bytes due go into <paramref name="bytes"/> (guest, byte). Both lists
        /// are cleared first.
        /// </summary>
        public void Tick(float dt, float now, IReadOnlyList<QueuedCustomer> queue, System.Func<int, bool> atSlot, List<int> ranOut,
            List<KeyValuePair<int, byte>> bytes)
        {
            ranOut.Clear();
            bytes.Clear();
            if (queue == null) return;
            for (int i = 0; i < queue.Count; i++) // by index: no enumerator per frame
            {
                QueuedCustomer q = queue[i];
                if (!_entries.TryGetValue(q.Id, out Entry e)) continue;
                if (dt > 0f && (atSlot == null || atSlot(q.Id))) e.Waited += dt;
                if (GuestPatience.RunsOut(e.Total, e.Waited))
                {
                    ranOut.Add(q.Id);
                    continue;
                }
                if (now < e.NextSync) continue;
                byte b = GuestPatience.ToByte(GuestPatience.ShareLeft(e.Total, e.Waited));
                if (b == e.LastByte) continue;
                e.NextSync = now + SyncSeconds;
                e.LastByte = b;
                bytes.Add(new KeyValuePair<int, byte>(q.Id, b));
            }
            foreach (int id in ranOut) _entries.Remove(id);
        }
    }
}
