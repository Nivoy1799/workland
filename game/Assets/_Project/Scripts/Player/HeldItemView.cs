using UnityEngine;
using Worktown.Game.Net;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 018, owner only: in first person the item you hold is drawn at the <see cref="PlayerCameraRig.ViewHold"/>
    /// anchor in the lower right of the view. The networked object, its
    /// collider and the server's hold point do not move; other players still see the item in your hand.
    /// Spec 019: a held knife is drawn the same way, turned onto its edge with the tip forward, and chops down
    /// once on every stroke (<see cref="PlayChop"/>, local only; other players see no chop).
    /// Spec 027b: the item draws itself (<see cref="Pickup"/> eases its model between places, so picking up arcs into the
    /// view instead of appearing there); this component only answers where the view pose is
    /// (<see cref="TryGetViewPose"/>, asked right before the camera renders, after every transform has moved this frame,
    /// so the item does not trail the view). Labels are WorldLabelFade's (spec 027d: a carried item shows none in first
    /// person).
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
        private Camera _camera;
        private float _chopStart = float.NegativeInfinity;

        public void Activate(PlayerCameraRig rig, PickupInteractor interactor)
        {
            if (_rig != null || rig == null || interactor == null) return;
            _rig = rig;
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

        /// <summary>Spec 019: one chop of the knife in view (the owner sent a stroke).</summary>
        public void PlayChop()
        {
            _chopStart = Time.unscaledTime;
        }

        /// <summary>0 at rest, up to 1 in the middle of a chop, back to 0 after <see cref="ChopSeconds"/>.</summary>
        private static float ChopAmount(float elapsed)
        {
            if (elapsed < 0f || elapsed >= ChopSeconds) return 0f;
            return Mathf.Sin(elapsed / ChopSeconds * Mathf.PI);
        }
    }
}
