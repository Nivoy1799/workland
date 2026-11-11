using System;
using System.Collections.Generic;
using System.Text;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 054 (A1), pure, no UnityEngine types: the host's outbound traffic of one sample window, by FishNet packet type and by
    /// object, and the bytes that went to the socket. <see cref="Fields"/> turns it into flat metrics fields (bytes per second per
    /// connected client) for <see cref="LoadStats.Line"/>: <c>pid_&lt;PacketId&gt;_bps</c>, <c>pid_Unattributed_bps</c> (socket bytes
    /// no packet type claims: NetworkTransform, which makes no statistics call, the SyncType fan-out and packet headers; never
    /// negative), <c>obj_&lt;Name&gt;_bps</c> for the top objects, and <c>net_socket_bps</c>. Note: FishNet counts most types per
    /// observer (an ObserversRpc's bytes times its observers), but a SyncType once per write; the socket total is exact.
    /// </summary>
    public sealed class TrafficTable
    {
        /// <summary>The bucket of the socket bytes no packet type claims.</summary>
        public const string Unattributed = "Unattributed";
        /// <summary>IPv4 (20) and UDP (8) header bytes per datagram (A2).</summary>
        public const int DatagramOverhead = 28;

        private readonly Dictionary<string, ulong> _groups = new Dictionary<string, ulong>();
        private readonly Dictionary<string, ulong> _objects = new Dictionary<string, ulong>();
        private readonly List<KeyValuePair<string, ulong>> _sort = new List<KeyValuePair<string, ulong>>();
        private ulong _socket;

        public void AddGroup(string packetId, ulong bytes) => Add(_groups, packetId, bytes);

        public void AddObject(string objectName, ulong bytes) => Add(_objects, ObjectKey(objectName), bytes);

        public void AddSocket(ulong bytes) => _socket += bytes;

        /// <summary>The socket bytes of the window.</summary>
        public ulong SocketBytes => _socket;

        /// <summary>The bytes the packet types claim (their sum).</summary>
        public ulong GroupBytes
        {
            get
            {
                ulong sum = 0;
                foreach (KeyValuePair<string, ulong> g in _groups) sum += g.Value;
                return sum;
            }
        }

        /// <summary>Socket bytes minus what the packet types claim, never below 0.</summary>
        public ulong UnattributedBytes => _socket > GroupBytes ? _socket - GroupBytes : 0;

        /// <summary>As <see cref="Fields(int, int, double)"/> for a one-second window.</summary>
        public IReadOnlyList<KeyValuePair<string, double>> Fields(int clients, int topObjects) => Fields(clients, topObjects, 1d);

        /// <summary>
        /// The fields of the window of <paramref name="seconds"/> s, per second and divided by <paramref name="clients"/> (at least 1):
        /// every packet type (largest first, then by name), Unattributed, the <paramref name="topObjects"/> largest objects (then by
        /// name), and net_socket_bps.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, double>> Fields(int clients, int topObjects, double seconds)
        {
            double scale = 1d / Math.Max(1, clients) / (seconds > 0d ? seconds : 1d);
            var fields = new List<KeyValuePair<string, double>>(_groups.Count + topObjects + 2);
            foreach (KeyValuePair<string, ulong> g in Sorted(_groups, int.MaxValue))
                fields.Add(new KeyValuePair<string, double>("pid_" + FieldPart(g.Key) + "_bps", g.Value * scale));
            fields.Add(new KeyValuePair<string, double>("pid_" + Unattributed + "_bps", UnattributedBytes * scale));
            foreach (KeyValuePair<string, ulong> o in Sorted(_objects, topObjects))
                fields.Add(new KeyValuePair<string, double>("obj_" + o.Key + "_bps", o.Value * scale));
            fields.Add(new KeyValuePair<string, double>("net_socket_bps", _socket * scale));
            return fields;
        }

        public void Clear()
        {
            _groups.Clear();
            _objects.Clear();
            _socket = 0;
        }

        /// <summary>A2: bytes on the wire: the transport's bytes plus <see cref="DatagramOverhead"/> per datagram.</summary>
        public static long WireBytes(long bytes, long datagrams) => bytes + DatagramOverhead * datagrams;

        /// <summary>
        /// The object bucket of a GameObject name: without "(Clone)" and without a scene copy's number ("Pan 2" and "Pan (1)" are
        /// "Pan"), only letters, digits and '_' (the metrics line's field names). Empty or null: "Unnamed".
        /// </summary>
        public static string ObjectKey(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unnamed";
            string s = name.Replace("(Clone)", string.Empty).Trim();
            int cut = s.IndexOfAny(new[] { ' ', '(' });
            if (cut > 0) s = s.Substring(0, cut);
            string part = FieldPart(s);
            return part.Length > 0 ? part : "Unnamed";
        }

        /// <summary>Only letters, digits and '_' (anything else becomes '_').</summary>
        public static string FieldPart(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) && c < 128 ? c : '_');
            return sb.ToString();
        }

        private List<KeyValuePair<string, ulong>> Sorted(Dictionary<string, ulong> source, int top)
        {
            _sort.Clear();
            _sort.AddRange(source);
            _sort.Sort((a, b) => a.Value != b.Value ? b.Value.CompareTo(a.Value) : string.CompareOrdinal(a.Key, b.Key));
            if (top >= 0 && _sort.Count > top) _sort.RemoveRange(top, _sort.Count - top);
            return new List<KeyValuePair<string, ulong>>(_sort);
        }

        private static void Add(Dictionary<string, ulong> into, string key, ulong bytes)
        {
            if (bytes == 0 || key == null) return;
            into.TryGetValue(key, out ulong had);
            into[key] = had + bytes;
        }
    }
}
