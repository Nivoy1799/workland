using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Worktown.Game.Layout
{
    /// <summary>
    /// Spec 062b: the shop's open or closed state (ShopDoorRules), on the door sign piece by the front: one synced byte, written by the
    /// server only (CustomerService, when a cook presses the sign or a service's summary ends), read by every client (build mode may be
    /// entered while it is Closed). Its sign text shows the state. A game starts Open (a new spawn). Holds no rules.
    /// </summary>
    public sealed class ShopDoor : NetworkBehaviour
    {
        /// <summary>The state (ShopDoorState as a byte).</summary>
        public readonly SyncVar<byte> State = new SyncVar<byte>((byte)ShopDoorState.Open);

        [SerializeField] private TextMesh _sign;

        private static ShopDoor _current;
        private byte _serverState;

        /// <summary>Spec 062b, server: the door changed (before, after).</summary>
        public static event System.Action<ShopDoorState, ShopDoorState> ServerChanged;

        /// <summary>The door of the spawned layout (null: none: the shop is open, build mode cannot be entered).</summary>
        public static ShopDoor Current => _current != null ? _current : null;

        /// <summary>The state now (the server's own copy on the server; Open without a door).</summary>
        public static ShopDoorState Now => _current == null ? ShopDoorState.Open : _current.StateNow;

        public ShopDoorState StateNow => (ShopDoorState)(IsServerInitialized ? _serverState : State.Value);

        private void Awake()
        {
            State.OnChange += (_, next, __) => ShowSign((ShopDoorState)next);
            ShowSign(ShopDoorState.Open);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _current = this;
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            if (_current == this) _current = null;
        }

        /// <summary>Spec 062b, server: sets the state (and raises <see cref="ServerChanged"/> when it changed).</summary>
        public void ServerSet(ShopDoorState state)
        {
            if (!IsServerInitialized) return;
            var before = (ShopDoorState)_serverState;
            _serverState = (byte)state;
            State.Value = (byte)state;
            ShowSign(state);
            if (before == state) return;
            Debug.Log("[Door] " + before + " -> " + state);
            try { ServerChanged?.Invoke(before, state); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        /// <summary>Dev builds: a tour host starts closed (-tour-build).</summary>
        public static bool StartsClosedForTour(string[] args)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            return args != null && System.Array.IndexOf(args, "-tour-build") >= 0;
#else
            return false;
#endif
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _serverState = (byte)ShopDoorState.Open;
            if (StartsClosedForTour(System.Environment.GetCommandLineArgs()))
            {
                Debug.Log("[Door] the tour host starts closed (-tour-build)");
                ServerSet(ShopDoorState.Closed);
            }
        }

        private void ShowSign(ShopDoorState state)
        {
            if (_sign != null) _sign.text = ShopDoorRules.SignText(state);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            _current = null;
            ServerChanged = null;
        }
    }
}
