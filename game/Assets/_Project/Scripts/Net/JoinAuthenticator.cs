using System;
using System.Collections.Generic;
using FishNet.Authenticating;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Net
{
    /// <summary>
    /// Spec 047: FishNet's authenticator for every connection (the host's own client too). The client sends a JoinRequest (its build
    /// hash and name) as soon as its transport is up; the server admits it (JoinRules.Decide: the same build, a free place) or answers
    /// with a ConnectionNotice and disconnects it after the notice is out. A connection that sends nothing for JoinRules.AnswerSeconds
    /// is refused (NoAnswer). The server keeps the admitted names and sends the LobbyRoster to everyone on every join and leave; the
    /// host kicks through <see cref="Kick"/> and says goodbye through <see cref="NotifyClosing"/>. Lives on the NetworkManager object
    /// (FishNet's ServerManager finds it there). Only connection messages: no game data.
    /// Follow-up 047: a kicked player is banned for the session (Rules.SessionBans, keyed on what the transport knows: the SteamID64
    /// over Steam, else the IP address); its rejoin is refused as KickedBefore. The list is cleared when the server stops.
    /// </summary>
    public sealed class JoinAuthenticator : Authenticator
    {
        public override event Action<NetworkConnection, bool> OnAuthenticationResult;

        /// <summary>Client: the server's notice before it disconnects this client (reason, detail).</summary>
        public event Action<DisconnectReason, string> NoticeReceived;

        /// <summary>Client: a new roster arrived (or the connection ended: empty).</summary>
        public event Action RosterChanged;

        /// <summary>This process's build hash (Rules.BuildHash over the version and ProtocolSurface).</summary>
        public string BuildHash { get; private set; } = string.Empty;

        /// <summary>Server: the places, the host included (D36: 16; <c>-max-players</c> for the load test).</summary>
        public int MaxPlayers { get; set; } = JoinRules.MaxPlayers;

        /// <summary>Client: the name this client sends (the Steam name when Steam runs).</summary>
        public string LocalName { get; set; } = string.Empty;

        /// <summary>Client: who is in the shop (the last roster); empty arrays when not connected.</summary>
        public LobbyRoster Roster { get; private set; } = EmptyRoster();

        /// <summary>Server: admitted players.</summary>
        public int PlayerCount => _names.Count;

        private readonly Dictionary<int, float> _pending = new Dictionary<int, float>();
        private readonly Dictionary<int, string> _names = new Dictionary<int, string>();
        private readonly List<int> _expired = new List<int>();
        private readonly SessionBans _bans = new SessionBans();

        /// <summary>Server: players kicked in this session (Rules.SessionBans; cleared when the shop closes).</summary>
        public int BannedCount => _bans.Count;

        public override void InitializeOnce(NetworkManager networkManager)
        {
            base.InitializeOnce(networkManager);
            BuildHash = Rules.BuildHash.Compute(Application.version, ProtocolSurface.Lines(typeof(JoinAuthenticator).Assembly));
            Debug.Log("[Net] build " + Application.version + " hash " + BuildHash);
            networkManager.ServerManager.RegisterBroadcast<JoinRequest>(OnJoinRequest, false);
            networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
            networkManager.ClientManager.RegisterBroadcast<ConnectionNotice>(OnNotice);
            networkManager.ClientManager.RegisterBroadcast<LobbyRoster>(OnRoster);
            networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
            networkManager.ClientManager.OnAuthenticated += OnLocalAuthenticated;
        }

        private void OnDestroy()
        {
            if (NetworkManager == null) return;
            if (NetworkManager.ServerManager != null)
            {
                NetworkManager.ServerManager.UnregisterBroadcast<JoinRequest>(OnJoinRequest);
                NetworkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
                NetworkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
            }
            if (NetworkManager.ClientManager != null)
            {
                NetworkManager.ClientManager.UnregisterBroadcast<ConnectionNotice>(OnNotice);
                NetworkManager.ClientManager.UnregisterBroadcast<LobbyRoster>(OnRoster);
                NetworkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
                NetworkManager.ClientManager.OnAuthenticated -= OnLocalAuthenticated;
            }
        }

        // ---- Server ----

        /// <summary>
        /// The <see cref="JoinRules.AnswerSeconds"/> (10 s) pending entry starts here, when FishNet has accepted the client's version
        /// packet (ServerManager.ParseVersion calls this), not at the transport's connect. A client that stays silent before sending
        /// that packet is never pending here: FishNet's remote timeout (60 s on the ServerManager) drops it.
        /// </summary>
        public override void OnRemoteConnection(NetworkConnection connection)
        {
            base.OnRemoteConnection(connection);
            _pending[connection.ClientId] = Time.unscaledTime;
        }

        private void OnJoinRequest(NetworkConnection connection, JoinRequest request, Channel channel)
        {
            // Once per connection: a second request (or one after the answer) is ignored.
            if (connection.IsAuthenticated || !_pending.Remove(connection.ClientId)) return;
            // IsLocalClient has no effect here: FishNet marks the host's own connection as local only after it is authenticated
            // (OnLocalAuthenticated). The host is safe anyway: its own client joins first, when the session's ban list is empty.
            bool kickedBefore = _bans.IsBanned(BanKey(connection), connection.IsLocalClient);
            DisconnectReason reason = JoinRules.Decide(request.Build, BuildHash, _names.Count, MaxPlayers, kickedBefore);
            if (reason != DisconnectReason.None)
            {
                string detail = reason == DisconnectReason.VersionMismatch ? "host build " + Rules.BuildHash.Short(BuildHash)
                    : reason == DisconnectReason.ServerFull ? MaxPlayers.ToString() : string.Empty;
                Debug.Log("[Net] player " + connection.ClientId + " refused: " + reason + " (build "
                    + Rules.BuildHash.Short(request.Build) + ", players " + _names.Count + "/" + MaxPlayers + ")");
                Refuse(connection, reason, detail);
                return;
            }
            string name = JoinRules.CleanName(request.Name, connection.ClientId);
            _names[connection.ClientId] = name;
            Debug.Log("[Net] player " + connection.ClientId + " admitted as '" + name + "', players " + _names.Count + "/" + MaxPlayers);
            OnAuthenticationResult?.Invoke(connection, true);
            SendRoster();
        }

        private void Refuse(NetworkConnection connection, DisconnectReason reason, string detail)
        {
            NetworkManager.ServerManager.Broadcast(connection, new ConnectionNotice { Reason = (byte)reason, Detail = detail ?? string.Empty }, false);
            // FishNet disconnects a failed authentication after the pending data (the notice) is out.
            OnAuthenticationResult?.Invoke(connection, false);
        }

        private void Update()
        {
            if (NetworkManager == null || !NetworkManager.IsServerStarted || _pending.Count == 0) return;
            float now = Time.unscaledTime;
            _expired.Clear();
            foreach (KeyValuePair<int, float> pending in _pending)
                if (now - pending.Value > JoinRules.AnswerSeconds) _expired.Add(pending.Key);
            foreach (int clientId in _expired)
            {
                _pending.Remove(clientId);
                if (!NetworkManager.ServerManager.Clients.TryGetValue(clientId, out NetworkConnection connection) || !connection.IsActive) continue;
                Debug.Log("[Net] player " + clientId + " refused: no join request within " + JoinRules.AnswerSeconds + " s");
                Refuse(connection, DisconnectReason.NoAnswer, string.Empty);
            }
        }

        /// <summary>
        /// Server (the host): removes a player. The player gets a Kicked notice (no rejoin), then the server disconnects it. False for
        /// the host's own player, an unknown id, or when this is not the server.
        /// </summary>
        public bool Kick(int clientId)
        {
            if (NetworkManager == null || !NetworkManager.IsServerStarted) return false;
            if (!NetworkManager.ServerManager.Clients.TryGetValue(clientId, out NetworkConnection connection) || !connection.IsActive) return false;
            if (!JoinRules.CanKick(true, connection.IsLocalClient)) return false;
            string key = BanKey(connection);
            bool banned = _bans.Ban(key, connection.IsLocalClient);
            NetworkManager.ServerManager.Broadcast(connection, new ConnectionNotice { Reason = (byte)DisconnectReason.Kicked, Detail = string.Empty }, false);
            connection.Disconnect(false);
            // The kind of key only: the Steam id or the address itself stays out of the log.
            Debug.Log("[Net] player " + clientId + " kicked by the host" + (banned
                ? ", banned for this session by " + (key.StartsWith(SessionBans.SteamPrefix, StringComparison.Ordinal) ? "Steam id" : "address")
                : ", not banned (no address)"));
            return true;
        }

        /// <summary>Server (the host closes the shop): every other player gets a HostClosed notice before the server stops.</summary>
        public void NotifyClosing(DisconnectReason reason = DisconnectReason.HostClosed)
        {
            if (NetworkManager == null || !NetworkManager.IsServerStarted) return;
            foreach (NetworkConnection connection in NetworkManager.ServerManager.Clients.Values)
                if (connection != null && connection.IsActive && !connection.IsLocalClient)
                    NetworkManager.ServerManager.Broadcast(connection,
                        new ConnectionNotice { Reason = (byte)reason, Detail = string.Empty }, false);
        }

        /// <summary>Server: the connection's ban key from its transport address (never from the client's own data).</summary>
        private static string BanKey(NetworkConnection connection)
        {
            try
            {
                return SessionBans.KeyOf(connection.GetAddress());
            }
            catch (Exception e) // a transport that cannot name the address: nothing to ban by
            {
                Debug.LogWarning("[Net] player " + connection.ClientId + ": no address for the ban list (" + e.Message + ")");
                return string.Empty;
            }
        }

        /// <summary>Server: the admitted name of a connection, or empty.</summary>
        public string NameOf(int clientId) => _names.TryGetValue(clientId, out string name) ? name : string.Empty;

        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            _pending.Remove(connection.ClientId);
            if (_names.Remove(connection.ClientId)) SendRoster();
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Stopped && !NetworkManager.ServerManager.IsAnyServerStarted())
            {
                _pending.Clear();
                _names.Clear();
                _bans.Clear();
            }
        }

        private void SendRoster()
        {
            var ids = new List<int>(_names.Keys);
            ids.Sort();
            var names = new string[ids.Count];
            int hostId = -1;
            for (int i = 0; i < ids.Count; i++)
            {
                names[i] = _names[ids[i]];
                if (NetworkManager.ServerManager.Clients.TryGetValue(ids[i], out NetworkConnection c) && c.IsLocalClient) hostId = ids[i];
            }
            NetworkManager.ServerManager.Broadcast(new LobbyRoster { Ids = ids.ToArray(), Names = names, HostId = hostId, MaxPlayers = MaxPlayers });
        }

        // ---- Client ----

        /// <summary>The host's own client is in: FishNet knows its connection as the local one only now, so the roster names the host.</summary>
        private void OnLocalAuthenticated()
        {
            if (NetworkManager.IsServerStarted) SendRoster();
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                NetworkManager.ClientManager.Broadcast(new JoinRequest { Build = BuildHash, Name = LocalName ?? string.Empty });
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                Roster = EmptyRoster();
                RosterChanged?.Invoke();
            }
        }

        private void OnNotice(ConnectionNotice notice, Channel channel)
        {
            var reason = (DisconnectReason)notice.Reason;
            Debug.Log("[Net] the host says: " + reason + (string.IsNullOrEmpty(notice.Detail) ? string.Empty : " (" + notice.Detail + ")"));
            NoticeReceived?.Invoke(reason, notice.Detail ?? string.Empty);
        }

        private void OnRoster(LobbyRoster roster, Channel channel)
        {
            if (roster.Ids == null || roster.Names == null || roster.Ids.Length != roster.Names.Length) return;
            Roster = roster;
            RosterChanged?.Invoke();
        }

        private static LobbyRoster EmptyRoster() =>
            new LobbyRoster { Ids = new int[0], Names = new string[0], HostId = -1, MaxPlayers = JoinRules.MaxPlayers };
    }
}
