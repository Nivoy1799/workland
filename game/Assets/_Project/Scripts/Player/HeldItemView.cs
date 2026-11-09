using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Net;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 018, owner only: in first person the item you hold is drawn at the <see cref="PlayerCameraRig.ViewHold"/>
    /// anchor in the lower right of the view, and its label above your head is hidden. The networked object, its
    /// collider and the server's hold point do not move; other players still see the item in your hand.
    /// Spec 019: a held knife is drawn the same way, turned onto its edge with the tip forward, and chops down
    /// once on every stroke (<see cref="PlayChop"/>, local only; other players see no chop).
    /// Spec 027b: the item draws itself (<see cref="Pickup"/> eases its model between places, so picking up arcs into the
    /// view instead of appearing there); this component only answers where the view pose is
    /// (<see cref="TryGetViewPose"/>, asked right before the camera renders, after every transform has moved this frame,
    /// so the item does not trail the view) and hides the labels.
    /// </summary>
    public sealed class HeldItemView : MonoBehaviour
    {
        // Spec 019: the knife in view, relative to the view anchor; numbers for the visual check.
        private static readonly Vector3 KnifeHoldOffset = new Vector3(-0.05f, 0.05f, 0.05f);
        // Turned 180 degrees after the spec 020 play-test (the human: "knife is held the wrong way around"),
        // then rolled over so the cutting edge points down (second play-test note).
        private static readonly Quaternion KnifeHoldRotation = Quaternion.Euler(15f, 170f, -90f);
        private const float ChopSeconds = 0.18f;
        private const float ChopAngle = 35f;      // degrees the tip dips
        private const float ChopDrop = 0.08f;     // metres the knife moves down

        private PlayerCameraRig _rig;
        private PickupInteractor _interactor;
        private Camera _camera;
        private float _chopStart = float.NegativeInfinity;

        private Pickup _held;
        private readonly List<Renderer> _hiddenLabels = new List<Renderer>();

        public void Activate(PlayerCameraRig rig, PickupInteractor interactor)
        {
            if (_rig != null || rig == null || interactor == null) return;
            _rig = rig;
            _interactor = interactor;
            _camera = rig.ViewHold != null ? rig.ViewHold.GetComponentInParent<Camera>() : null;
            if (_camera == null) Debug.LogWarning("[View] no ViewHold anchor under a camera; held food stays above the head");
        }

        /// <summary>
        /// Spec 027b: the pose (frame) the held <paramref name="item"/> is drawn at in your first-person view; false when
        /// it is not shown in view (third person, no anchor, or not activated: then it goes to the hand).
        /// </summary>
        public bool TryGetViewPose(Pickup item, out Pose pose)
        {
            pose = default;
            if (_rig == null || item == null || !_rig.IsFirstPerson || _camera == null || _rig.ViewHold == null) return false;
            Transform anchor = _rig.ViewHold;
            if (item.TryGetComponent(out Worktown.Game.Cooking.Knife _))
            {
                // Spec 019: on its edge, tip forward; a short dip while chopping.
                float chop = ChopAmount(Time.unscaledTime - _chopStart);
                pose = new Pose(anchor.TransformPoint(KnifeHoldOffset + new Vector3(0f, -ChopDrop * chop, 0.02f * chop)),
                    anchor.rotation * Quaternion.Euler(ChopAngle * chop, 0f, 0f) * KnifeHoldRotation);
                return true;
            }
            pose = new Pose(anchor.position, anchor.rotation);
            return true;
        }

        private void LateUpdate()
        {
            if (_rig == null) return;
            Pickup held = _rig.IsFirstPerson && _camera != null ? _interactor.FindHeldPickup() : null;
            if (held == _held) return;
            ShowLabels();
            _held = held;
            if (held != null) HideLabels(held);
        }

        /// <summary>Spec 019: one chop of the knife in view (the owner sent a stroke).</summary>
        public void PlayChop()
        {
            _chopStart = Time.unscaledTime;
        }

        private void HideLabels(Pickup held)
        {
            foreach (Transform child in held.transform)
            {
                // The name and stars above your head: you would look through them.
                if (!child.TryGetComponent(out TextMesh _)) continue;
                if (child.TryGetComponent(out Renderer label) && label.enabled)
                {
                    label.enabled = false;
                    _hiddenLabels.Add(label);
                }
            }
        }

        private void ShowLabels()
        {
            // A despawned and destroyed food needs nothing back; a pooled one gets its label back.
            foreach (Renderer label in _hiddenLabels)
                if (label != null) label.enabled = true;
            _hiddenLabels.Clear();
            _held = null;
        }

        /// <summary>0 at rest, up to 1 in the middle of a chop, back to 0 after <see cref="ChopSeconds"/>.</summary>
        private static float ChopAmount(float elapsed)
        {
            if (elapsed < 0f || elapsed >= ChopSeconds) return 0f;
            return Mathf.Sin(elapsed / ChopSeconds * Mathf.PI);
        }

        private void OnDestroy()
        {
            ShowLabels();
        }
    }
}
