using UnityEngine;
using Worktown.Game.Net;
using Worktown.Game.Player;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 027: the intents a tour gives the owner's player instead of the devices (move, look, sprint, crouch, jump,
    /// view; spec 041: the hand inputs, a left click, a right click, wheel steps, on <see cref="PickupInteractor.HandIntentSource"/>). Set on <see cref="PlayerController.IntentSource"/>; the controller builds its MoveData from it exactly as
    /// from the devices, so prediction and the server's checks are unchanged. Works without window focus.
    /// </summary>
    public sealed class ScriptedIntentSource : PlayerController.IIntentSource, PickupInteractor.IHandIntentSource
    {
        private Vector2 _move;
        private Vector2 _look;
        private bool _jump;
        private bool _leftClick;
        private bool _rightClick;
        private int _turn;

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

        /// <summary>Spec 041: one whole left click (press and release), read by the interactor in the next frame.</summary>
        public void LeftClick() => _leftClick = true;

        /// <summary>Spec 041: one right click (the put-down), read in the next frame.</summary>
        public void RightClick() => _rightClick = true;

        /// <summary>Spec 041: mouse wheel notches (positive = rolled away), read in the next frame.</summary>
        public void Turn(int steps) => _turn += steps;

        public bool ConsumeLeftClick()
        {
            bool click = _leftClick;
            _leftClick = false;
            return click;
        }

        public bool ConsumeRightClick()
        {
            bool click = _rightClick;
            _rightClick = false;
            return click;
        }

        public int ConsumeTurn()
        {
            int turn = _turn;
            _turn = 0;
            return turn;
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
