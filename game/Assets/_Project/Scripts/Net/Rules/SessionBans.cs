using System;
using System.Collections.Generic;

namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Follow-up 047, server side, pure: the session's ban list. A player the host kicked cannot join this shop again; the list is
    /// cleared when the shop closes (the server stops). The key is what the server's transport knows of the connection, never what
    /// the client says: the SteamID64 for a player who came over Steam (FishySteamworks' address), else the IP address (Tugboat's,
    /// without the port, which changes on every connect). Players behind one public IP who join by address share a key. The host's
    /// own player is never banned.
    /// </summary>
    public sealed class SessionBans
    {
        public const string SteamPrefix = "steam:";
        public const string AddressPrefix = "address:";
        private const string MappedIpv4 = "::ffff:";

        private readonly HashSet<string> _keys = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Banned keys in this session.</summary>
        public int Count => _keys.Count;

        /// <summary>
        /// The key of a connection from its transport address (FishNet's NetworkConnection.GetAddress): a SteamID64 (digits) gives
        /// "steam:id"; anything else "address:ip", lower case, an IPv4-mapped IPv6 address as its IPv4. Empty when the address is unknown
        /// (nothing to ban by).
        /// </summary>
        public static string KeyOf(string transportAddress)
        {
            if (string.IsNullOrWhiteSpace(transportAddress)) return string.Empty;
            string address = transportAddress.Trim();
            if (RichPresenceRules.TryParseSteamId(address, out ulong steamId)) return SteamPrefix + steamId;
            address = address.ToLowerInvariant();
            if (address.StartsWith(MappedIpv4, StringComparison.Ordinal) && address.IndexOf('.') > 0)
                address = address.Substring(MappedIpv4.Length);
            return AddressPrefix + address;
        }

        /// <summary>Bans the key for this session. False (nothing banned) for the host's own player or an empty key.</summary>
        public bool Ban(string key, bool isHost)
        {
            if (isHost || string.IsNullOrEmpty(key)) return false;
            _keys.Add(key);
            return true;
        }

        /// <summary>The key was kicked in this session (never the host's own player, never an empty key).</summary>
        public bool IsBanned(string key, bool isHost) => !isHost && !string.IsNullOrEmpty(key) && _keys.Contains(key);

        /// <summary>The shop closed: everyone may join the next one.</summary>
        public void Clear() => _keys.Clear();
    }
}
