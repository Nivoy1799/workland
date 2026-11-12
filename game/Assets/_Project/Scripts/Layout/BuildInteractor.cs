using FishNet.Connection;
using FishNet.Object;
using UnityEngine;
using Worktown.Game.Net;

namespace Worktown.Game.Layout
{
    /// <summary>
    /// Spec 062b: the build mode's wire on the player: one ServerRpc (<see cref="RequestBuild"/>: an intent and the request's number) and
    /// its answer (<see cref="BuildAnswer"/>, to the asker only: the number and the packed outcome, BuildDesk.Pack). The server keeps the
    /// spec 027b rate limit (RequestBucket) and hands the intent to the BuildService, which checks and decides everything. The owner gets a
    /// BuildModeView (the mode, the aim, the ghost, the prompts) added when it starts. Holds no rules.
    /// </summary>
    public sealed class BuildInteractor : NetworkBehaviour
    {
        // 062b review: the same rate limit and spam kick as the cooking requests (ServerRequestGate).
        private readonly ServerRequestGate _gate = new ServerRequestGate();
        private int _nextRequest;

        /// <summary>Owner: a request was answered (its number, the packed answer).</summary>
        public event System.Action<int, byte> Answered;

        /// <summary>The slot of this player (client id + 1; 0 before it is owned).</summary>
        public byte Slot => BuildService.SlotOf(Owner);

        public override void OnStartServer()
        {
            base.OnStartServer();
            _gate.Reset(); // a pooled player object starts a new life
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (IsOwner && GetComponent<BuildModeView>() == null) gameObject.AddComponent<BuildModeView>();
        }

        /// <summary>Owner: sends <paramref name="intent"/>; returns the request's number (its answer comes with it).</summary>
        public int Send(BuildIntent intent)
        {
            if (!IsOwner) return 0;
            int request = ++_nextRequest;
            RequestBuild(intent, request);
            return request;
        }

        [ServerRpc]
        private void RequestBuild(BuildIntent intent, int request)
        {
            bool tooFast = _gate.IsRateLimited(this);
            if (_gate.Tripped) return; // disconnected for spam
            BuildService service = BuildService.Find();
            byte answer = service != null ? service.Handle(this, intent, tooFast)
                : BuildDesk.Pack(BuildOutcome.NotBuilding, null);
            BuildAnswer(Owner, request, answer);
        }

        [TargetRpc]
        private void BuildAnswer(NetworkConnection target, int request, byte outcome)
        {
            try { Answered?.Invoke(request, outcome); }
            catch (System.Exception e) { Debug.LogException(e); }
        }
    }
}
