using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Worktown.Game.Net
{
    /// <summary>
    /// Server-authoritative movement. The owning client only sends intents (move direction and
    /// yaw) every tick; the server moves the CharacterController and NetworkTransform
    /// replicates the result to everyone, including the owner. No client prediction (spec 003).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : NetworkBehaviour
    {
        [SerializeField] private float _moveSpeed = 4f;
        [SerializeField] private float _mouseYawSensitivity = 0.15f;
        [SerializeField] private float _keyYawSpeed = 120f;
        [SerializeField] private Vector3 _cameraOffset = new Vector3(0f, 3f, -5f);

        private CharacterController _controller;

        // Owner-side input state.
        private float _localYaw;

        // Server-side last accepted intent.
        private Vector2 _serverMove;
        private float _serverYaw;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        public override void OnStartNetwork()
        {
            TimeManager.OnTick += OnTick;
        }

        public override void OnStopNetwork()
        {
            if (TimeManager != null)
                TimeManager.OnTick -= OnTick;
        }

        public override void OnStartServer()
        {
            _serverYaw = transform.eulerAngles.y;
        }

        public override void OnStartClient()
        {
            if (IsOwner)
                _localYaw = transform.eulerAngles.y;
        }

        private void Update()
        {
            if (!IsOwner) return;

            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.isPressed) _localYaw -= _keyYawSpeed * Time.deltaTime;
                if (keyboard.rightArrowKey.isPressed) _localYaw += _keyYawSpeed * Time.deltaTime;
            }
            if (mouse != null && mouse.rightButton.isPressed)
                _localYaw += mouse.delta.ReadValue().x * _mouseYawSensitivity;
            _localYaw = Mathf.Repeat(_localYaw, 360f);
        }

        private void LateUpdate()
        {
            if (!IsOwner) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            Quaternion rotation = Quaternion.Euler(0f, _localYaw, 0f);
            cam.transform.position = transform.position + rotation * _cameraOffset;
            cam.transform.LookAt(transform.position + Vector3.up * 1f);
        }

        private void OnTick()
        {
            if (IsOwner)
                SubmitInput(ReadMove(), _localYaw);

            if (IsServerInitialized)
                ApplyMovement((float)TimeManager.TickDelta);
        }

        private static Vector2 ReadMove()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return Vector2.zero;
            Vector2 move = Vector2.zero;
            if (keyboard.wKey.isPressed) move.y += 1f;
            if (keyboard.sKey.isPressed) move.y -= 1f;
            if (keyboard.dKey.isPressed) move.x += 1f;
            if (keyboard.aKey.isPressed) move.x -= 1f;
            return move;
        }

        /// <summary>Intent from the owner. Sent every tick, so a lost packet is replaced by the next one.</summary>
        [ServerRpc]
        private void SubmitInput(Vector2 move, float yaw, Channel channel = Channel.Unreliable)
        {
            if (!IsFinite(move.x) || !IsFinite(move.y) || !IsFinite(yaw)) return;
            _serverMove = Vector2.ClampMagnitude(move, 1f);
            _serverYaw = Mathf.Repeat(yaw, 360f);
        }

        private void ApplyMovement(float delta)
        {
            Quaternion rotation = Quaternion.Euler(0f, _serverYaw, 0f);
            transform.rotation = rotation;
            Vector3 direction = rotation * new Vector3(_serverMove.x, 0f, _serverMove.y);
            Vector3 velocity = direction * _moveSpeed + Vector3.down * 9.81f;
            _controller.Move(velocity * delta);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
