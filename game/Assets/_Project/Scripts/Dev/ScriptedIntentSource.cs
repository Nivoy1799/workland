using UnityEngine;
using Worktown.Game.Net;
using Worktown.Game.Player;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 027: the intents a tour gives the owner's player instead of the devices (move, look, sprint, crouch, jump,
    /// view). Set on <see cref="PlayerController.IntentSource"/>; the controller builds its MoveData from it exactly as
    /// from the devices, so prediction and the server's checks are unchanged. Works without window focus.
    /// </summary>
    public sealed class ScriptedIntentSource : PlayerController.IIntentSource
    {
        private Vector2 _move;
        private Vector2 _look;
        private bool _jump;

        /// <summary>Camera-relative move (x right, y forward), clamped to length 1 like a stick.</summary>
        public Vector2 Move
        {
            get => _move;
            set => _move = MovementRules.SanitizeMove(value);
        }

        public bool Sprint { get; set; }
        public bool Crouch { get; set; }
        public ViewMode? View { get; set; }

        /// <summary>Asks for one jump; the next tick's MoveData carries it.</summary>
        public void Jump() => _jump = true;

        /// <summary>Adds a look delta in degrees (x yaw right, y pitch up); the camera rig takes it on its next frame.</summary>
        public void AddLook(Vector2 degrees)
        {
            if (float.IsNaN(degrees.x) || float.IsNaN(degrees.y) || float.IsInfinity(degrees.x) || float.IsInfinity(degrees.y)) return;
            _look += degrees;
        }

        /// <summary>Stops walking and sprinting (the stance and the view stay).</summary>
        public void Stop()
        {
            _move = Vector2.zero;
            Sprint = false;
        }

        public bool ConsumeJump()
        {
            bool jump = _jump;
            _jump = false;
            return jump;
        }

        public Vector2 ConsumeLook()
        {
            Vector2 look = _look;
            _look = Vector2.zero;
            return look;
        }
    }
}
