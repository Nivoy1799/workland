using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Net
{
    /// <summary>
    /// Spec 047, the Steam side of joining (FishySteamworks carries the game; this only finds and invites). While this PC hosts, its
    /// rich presence "connect" is <c>-steam its-SteamID</c> (RichPresenceRules), so friends see Join Game in Steam's friends list and
    /// can be invited (<see cref="OpenInviteDialog"/>); Steam starts a friend's game with that flag, or hands it to a running game
    /// (<see cref="JoinRequested"/>). <see cref="HostingFriends"/> lists friends playing this game with a shop open. Does nothing
    /// without Steam (the menu says so) and in dedicated server builds (no Steam friends there). Owned by NetBootstrap.
    /// Follow-up 047: the friends-list line, <c>steam_display</c> = "#Status_Hosting" with %players% (or "#Status_Playing" for a
    /// guest); the tokens live in game/Steam/rich_presence_localization.vdf, which must be uploaded in Steamworks for the real app id.
    /// </summary>
    public sealed class SteamFriendsLink
    {
        private const float StatusEverySeconds = 5f;

        private readonly SteamBootstrap _steam;
#if !UNITY_SERVER
        private Callback<GameRichPresenceJoinRequested_t> _joinRequested;
#endif
        private bool _presenceSet;
        /// <summary>Follow-up 047: the presence set is the host's (connect, players) rather than a guest's.</summary>
        private bool _presenceHosting;
        private float _nextStatus;
        private bool _wired;

        /// <summary>A friend's join (a SteamID64) arrived through Steam while the game runs.</summary>
        public event Action<ulong> JoinRequested;

        public SteamFriendsLink(SteamBootstrap steam)
        {
            _steam = steam;
        }

        /// <summary>Steam runs and this is a client build (friends, invites, rich presence work).</summary>
        public bool Available
        {
            get
            {
#if UNITY_SERVER
                return false;
#else
                return _steam != null && _steam.IsReady;
#endif
            }
        }

        /// <summary>
        /// Every frame: the join callback once Steam is up; the rich presence while hosting (connect, the friends-list line with the
        /// players) or playing in a friend's shop (the line only; follow-up 047), cleared after.
        /// </summary>
        public void Update(bool hosting, bool playing, int players, int maxPlayers)
        {
            if (!Available) return;
#if !UNITY_SERVER
            if (!_wired)
            {
                _wired = true;
                _joinRequested = Callback<GameRichPresenceJoinRequested_t>.Create(OnJoinRequested);
            }
            hosting = hosting && _steam.LocalSteamId != 0;
            if (_presenceSet && _presenceHosting != hosting && (hosting || playing))
            {
                // Host to guest (or back) without a pause: start from a clean presence (no stale connect string).
                SteamFriends.ClearRichPresence();
                _presenceSet = false;
            }
            if (hosting)
            {
                if (!_presenceSet)
                {
                    _presenceSet = true;
                    _presenceHosting = true;
                    SteamFriends.SetRichPresence(RichPresenceRules.ConnectKey, RichPresenceRules.ConnectString(_steam.LocalSteamId));
                    SteamFriends.SetRichPresence(RichPresenceRules.DisplayKey, RichPresenceRules.DisplayToken(true));
                    _nextStatus = 0f;
                    Debug.Log("[Steam] rich presence: friends can join " + _steam.LocalSteamId);
                }
                if (Time.unscaledTime >= _nextStatus)
                {
                    _nextStatus = Time.unscaledTime + StatusEverySeconds;
                    SteamFriends.SetRichPresence(RichPresenceRules.PlayersKey, RichPresenceRules.PlayersValue(players));
                    SteamFriends.SetRichPresence(RichPresenceRules.StatusKey, RichPresenceRules.Status(players, maxPlayers));
                }
            }
            else if (playing)
            {
                if (!_presenceSet)
                {
                    _presenceSet = true;
                    _presenceHosting = false;
                    SteamFriends.SetRichPresence(RichPresenceRules.DisplayKey, RichPresenceRules.DisplayToken(false));
                    SteamFriends.SetRichPresence(RichPresenceRules.StatusKey, RichPresenceRules.PlayingStatus);
                    Debug.Log("[Steam] rich presence: playing in a friend's shop");
                }
            }
            else if (_presenceSet)
            {
                _presenceSet = false;
                SteamFriends.ClearRichPresence();
                Debug.Log("[Steam] rich presence cleared");
            }
#endif
        }

        /// <summary>Opens Steam's invite dialog for this host's shop. False without Steam or when not hosting.</summary>
        public bool OpenInviteDialog()
        {
            if (!Available || !_presenceSet || !_presenceHosting || _steam.LocalSteamId == 0) return false;
#if !UNITY_SERVER
            SteamFriends.ActivateGameOverlayInviteDialogConnectString(RichPresenceRules.ConnectString(_steam.LocalSteamId));
#endif
            return true;
        }

        /// <summary>Friends playing this game whose rich presence offers a shop to join (asks Steam for fresh presence each call).</summary>
        public List<FriendHost> HostingFriends()
        {
            var result = new List<FriendHost>();
            if (!Available) return result;
#if !UNITY_SERVER
            try
            {
                AppId_t app = SteamUtils.GetAppID();
                int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
                for (int i = 0; i < count; i++)
                {
                    CSteamID friend = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                    if (!SteamFriends.GetFriendGamePlayed(friend, out FriendGameInfo_t game) || game.m_gameID.AppID() != app) continue;
                    SteamFriends.RequestFriendRichPresence(friend);
                    if (RichPresenceRules.TryParseConnect(SteamFriends.GetFriendRichPresence(friend, RichPresenceRules.ConnectKey), out ulong host))
                        result.Add(new FriendHost(SteamFriends.GetFriendPersonaName(friend), host));
                }
            }
            catch (InvalidOperationException e)
            {
                Debug.LogWarning("[Steam] friends list unavailable: " + e.Message);
            }
#endif
            return result;
        }

        /// <summary>This player's Steam name, or empty without Steam.</summary>
        public string PersonaName()
        {
            if (!Available) return string.Empty;
#if !UNITY_SERVER
            return SteamFriends.GetPersonaName();
#else
            return string.Empty;
#endif
        }

        /// <summary>Clears the rich presence (the game quits or the server stops for good).</summary>
        public void Shutdown()
        {
            if (!_presenceSet || !Available) return;
            _presenceSet = false;
#if !UNITY_SERVER
            SteamFriends.ClearRichPresence();
#endif
        }

#if !UNITY_SERVER
        private void OnJoinRequested(GameRichPresenceJoinRequested_t request)
        {
            if (!RichPresenceRules.TryParseConnect(request.m_rgchConnect, out ulong host))
            {
                Debug.LogWarning("[Steam] join request without a host: '" + request.m_rgchConnect + "'");
                return;
            }
            Debug.Log("[Steam] join request from Steam for host " + host);
            JoinRequested?.Invoke(host);
        }
#endif
    }
}
