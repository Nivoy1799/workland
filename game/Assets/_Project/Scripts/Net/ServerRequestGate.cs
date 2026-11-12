using FishNet.Connection;
using FishNet.Object;
using UnityEngine;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Net
{
    /// <summary>
    /// Server, one per player behaviour that takes requests (PickupInteractor, spec 062b's BuildInteractor): refuse requests that come faster
    /// than a person can click (spam costs no validation work). Review of spec 027b: a token bucket per player (RequestBucket: burst 2, one
    /// token per 0.2 s), so requests sent far enough apart but arriving bunched all pass; a refused one is answered (TooFast, Busy). Spec 045:
    /// after SpamGuard.MaxTooFastInWindow refusals within SpamGuard.WindowSeconds (a sliding window) the client is disconnected (a flood, not
    /// a person); the host's own player is never kicked from its own server. 062b review: one helper for both, not a copy.
    /// </summary>
    public sealed class ServerRequestGate
    {
        private readonly RequestBucket _bucket = new RequestBucket();
        private readonly SpamGuard _spam = new SpamGuard();

        /// <summary>The player was disconnected for spam: its later requests are dropped unanswered.</summary>
        public bool Tripped => _spam.Tripped;

        /// <summary>A pooled player object starts a new life (spec 045).</summary>
        public void Reset() => _spam.Reset();

        /// <summary>True when this request of <paramref name="player"/>'s owner comes too fast; kicks a flood.</summary>
        public bool IsRateLimited(NetworkBehaviour player)
        {
            double now = Time.unscaledTimeAsDouble;
            bool limited = !_bucket.TryTake(now);
            if (!_spam.Note(limited, now)) return limited;
            NetworkConnection owner = player != null ? player.Owner : null;
            if (!SpamGuard.ShouldKick(_spam.Tripped, owner == null || !owner.IsValid || owner.IsLocalClient))
            {
                _spam.Reset(); // the host's own player (or nobody): keep answering
            }
            else
            {
                Debug.LogWarning("[Net] player " + owner.ClientId + " disconnected: " + SpamGuard.MaxTooFastInWindow
                    + " requests in " + SpamGuard.WindowSeconds + " s came too fast (spam)");
                player.NetworkManager.ServerManager.Kick(owner, FishNet.Managing.Server.KickReason.ExcessiveData,
                    FishNet.Managing.Logging.LoggingType.Off);
            }
            return limited;
        }
    }
}
