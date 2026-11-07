using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;
using Worktown.Game.Player;
using Worktown.Game.UI;

namespace Worktown.Game.Social
{
    /// <summary>
    /// On the player (spec 012). The owner opens the emote wheel (hold G / LB), pings where the camera
    /// looks (middle mouse / RB, double-click = danger) and sends quick chat (hold T + 1-6 / D-pad).
    /// Clients send only ids and a ping point; the server checks them, rate-limits and broadcasts to
    /// the players who observe this player (the 40 m interest radius).
    /// </summary>
    public sealed class SocialInteractor : NetworkBehaviour
    {
        private const float DoubleClickSeconds = 0.35f;
        private const float WheelMouseScale = 0.01f;

        [SerializeField] private PingMarker _pingPrefab;
        [SerializeField] private EmoteBubble _bubble;

        // Server only.
        private readonly SocialRateLimiter _serverLimiter = new SocialRateLimiter();

        // Owner only.
        private PlayerInputReader _input;
        private EmoteWheelView _wheel;
        private bool _wheelOpen;
        private Vector2 _wheelVector;
        private float _lastPingClick = -10f;

        // Dev only: `-dev-social` makes the owner send an emote, a quick-chat line and a ping on a
        // timer, so agents can check the network path with screenshots (spec 012 AC3).
        private float _devNext = -1f;
        private int _devStep;

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (IsOwner && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-dev-social") >= 0)
                _devNext = Time.unscaledTime + 8f;
        }

        private void RunDevSocial()
        {
            if (_devNext < 0f || Time.unscaledTime < _devNext) return;
            _devNext = Time.unscaledTime + 2f;
            switch (_devStep++ % 3)
            {
                case 0: RequestEmote(0); break;
                case 1: RequestQuickChat(0); break;
                default: RequestPing(transform.position + transform.forward * 4f, (byte)PingKind.LookHere); break;
            }
        }

        private void Update()
        {
            if (!IsOwner) return;
            RunDevSocial();
            if (_input == null) _input = FindFirstObjectByType<PlayerInputReader>();
            if (_wheel == null) _wheel = FindFirstObjectByType<EmoteWheelView>();
            if (_input == null || _wheel == null) return;
            if (_input.IsUiOpen && !_wheelOpen) return;

            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            Gamepad gamepad = Gamepad.current;

            if (UpdateWheel(keyboard, mouse, gamepad)) return;
            UpdateQuickChat(keyboard, gamepad);
            UpdatePing(mouse, gamepad);
        }

        /// <summary>True while the wheel is open (it takes all input).</summary>
        private bool UpdateWheel(Keyboard keyboard, Mouse mouse, Gamepad gamepad)
        {
            bool held = (keyboard != null && keyboard.gKey.isPressed) || (gamepad != null && gamepad.leftShoulder.isPressed);
            if (held && !_wheelOpen)
            {
                _wheelOpen = true;
                _wheelVector = Vector2.zero;
                _input.SuppressLook = true;
                _wheel.SetWheelOpen(true);
            }
            if (!_wheelOpen) return false;

            if (mouse != null) _wheelVector = Vector2.ClampMagnitude(_wheelVector + mouse.delta.ReadValue() * WheelMouseScale, 1.5f);
            if (gamepad != null)
            {
                Vector2 stick = gamepad.rightStick.ReadValue();
                if (stick.magnitude > SocialRules.DeadZone) _wheelVector = stick;
            }
            int slot = SocialRules.WheelSlot(_wheelVector);
            _wheel.SetSelection(slot);

            if (!held)
            {
                _wheelOpen = false;
                _input.SuppressLook = false;
                _wheel.SetWheelOpen(false);
                if (slot >= 0) RequestEmote((byte)slot);
            }
            return true;
        }

        private void UpdateQuickChat(Keyboard keyboard, Gamepad gamepad)
        {
            bool held = keyboard != null && keyboard.tKey.isPressed;
            _wheel.SetQuickChatOpen(held);
            if (held)
            {
                int pressed = PressedDigit(keyboard);
                if (pressed >= 1 && pressed <= QuickChatCatalog.All.Count) RequestQuickChat((byte)(pressed - 1));
            }
            if (gamepad == null) return;
            if (gamepad.dpad.up.wasPressedThisFrame) RequestQuickChat(0);
            else if (gamepad.dpad.right.wasPressedThisFrame) RequestQuickChat(1);
            else if (gamepad.dpad.down.wasPressedThisFrame) RequestQuickChat(2);
            else if (gamepad.dpad.left.wasPressedThisFrame) RequestQuickChat(3);
        }

        private void UpdatePing(Mouse mouse, Gamepad gamepad)
        {
            bool clicked = (mouse != null && mouse.middleButton.wasPressedThisFrame)
                || (gamepad != null && gamepad.rightShoulder.wasPressedThisFrame);
            if (!clicked) return;
            float now = Time.unscaledTime;
            PingKind kind = now - _lastPingClick < DoubleClickSeconds ? PingKind.Danger : PingKind.LookHere;
            _lastPingClick = now;
            if (TryAimPoint(out Vector3 point)) RequestPing(point, (byte)kind);
        }

        /// <summary>Where the camera centre looks, ignoring our own colliders.</summary>
        private bool TryAimPoint(out Vector3 point)
        {
            point = default;
            Camera cam = Camera.main;
            if (cam == null) return false;
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            RaycastHit[] hits = Physics.RaycastAll(ray, SocialRules.PingRange + 20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= best) continue;
                best = hit.distance;
                point = hit.point;
            }
            return best < float.MaxValue;
        }

        private static int PressedDigit(Keyboard keyboard)
        {
            if (keyboard.digit1Key.wasPressedThisFrame) return 1;
            if (keyboard.digit2Key.wasPressedThisFrame) return 2;
            if (keyboard.digit3Key.wasPressedThisFrame) return 3;
            if (keyboard.digit4Key.wasPressedThisFrame) return 4;
            if (keyboard.digit5Key.wasPressedThisFrame) return 5;
            if (keyboard.digit6Key.wasPressedThisFrame) return 6;
            return 0;
        }

        // ---------- server ----------

        [ServerRpc]
        private void RequestEmote(byte id)
        {
            if (!EmoteCatalog.TryGet(id, out SocialPhrase emote) || !Allow()) return;
            Debug.Log("[Social] player " + OwnerId + " emote " + emote.Text);
            ShowEmote(id);
        }

        [ServerRpc]
        private void RequestQuickChat(byte id)
        {
            if (!QuickChatCatalog.TryGet(id, out SocialPhrase line) || !Allow()) return;
            Debug.Log("[Social] player " + OwnerId + " says " + line.Text);
            ShowQuickChat(id);
        }

        [ServerRpc]
        private void RequestPing(Vector3 point, byte kind)
        {
            if (kind > (byte)PingKind.Danger || !SocialRules.IsValidPing(transform.position, point) || !Allow()) return;
            Debug.Log("[Social] player " + OwnerId + " ping " + (PingKind)kind + " at " + point);
            ShowPing(point, kind);
        }

        private bool Allow()
        {
            if (_serverLimiter.TryConsume(Time.unscaledTime)) return true;
            Debug.Log("[Social] player " + OwnerId + " rate-limited");
            return false;
        }

        // ---------- everyone who sees this player ----------

        [ObserversRpc]
        private void ShowEmote(byte id)
        {
            if (EmoteCatalog.TryGet(id, out SocialPhrase emote) && _bubble != null) _bubble.Show(emote.Bubble);
        }

        [ObserversRpc]
        private void ShowQuickChat(byte id)
        {
            if (!QuickChatCatalog.TryGet(id, out SocialPhrase line)) return;
            if (_bubble != null) _bubble.Show(line.Bubble);
            SocialFeed.Publish(OwnerId, line.Text);
        }

        [ObserversRpc]
        private void ShowPing(Vector3 point, byte kind)
        {
            PingKind pingKind = (PingKind)kind;
            string text = pingKind == PingKind.Danger ? "Danger / not here" : "Look here";
            if (_pingPrefab != null)
            {
                PingMarker marker = Instantiate(_pingPrefab, point, Quaternion.identity);
                marker.Init(pingKind, "Player " + OwnerId + ": " + text);
            }
            SocialFeed.Publish(OwnerId, "pinged: " + text);
        }
    }
}
