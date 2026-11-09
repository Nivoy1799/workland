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

        private const int BoundsFrames = 15;
        // Spec 031b: the spatula's flip in view (as long as the food's arc on the griddle).
        public const float FlipSeconds = 0.25f;
        private const float FlipReach = 0.06f;  // metres towards the view centre and forward
        private const float FlipLift = 0.05f;   // metres up
        private const float FlipPitch = 25f;    // degrees the blade tips up
        private const float FlipRoll = 70f;     // degrees it turns over
        private float _flipStart = float.NegativeInfinity;
        private readonly System.Collections.Generic.List<MeshFilter> _meshes = new System.Collections.Generic.List<MeshFilter>();
        private Pickup _boundsItem;
        private int _nextBoundsFrame;
        private Bounds _bounds;
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
        public bool TryGetViewPose(Pickup item, out Pose pose) => TryGetViewPose(item, out pose, out _);

        /// <summary>
        /// Spec 031b: as <see cref="TryGetViewPose(Pickup, out Pose)"/>, plus the scale the item is drawn at. Everything but the
        /// knife stands whole in the lower right (<see cref="HoldViewRules"/>): its middle at a fixed place on screen, tilted
        /// towards the eye, large things scaled down so they never reach the view centre.
        /// </summary>
        public bool TryGetViewPose(Pickup item, out Pose pose, out float scale)
        {
            pose = default;
            scale = 1f;
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
            Bounds local = LocalBounds(item);
            Vector3 lossy = item.transform.lossyScale;
            Vector3 centre = Vector3.Scale(lossy, local.center);
            float size = Vector3.Scale(lossy, local.size).magnitude; // the diagonal: the whole item fits, however it turns
            Transform eye = _camera.transform;
            scale = HoldViewRules.FitScale(size, _camera.fieldOfView);
            LastViewSize = size;
            LastViewScale = scale;
            Quaternion rotation = eye.rotation * HoldViewRules.Tilt;
            Vector3 inView = HoldViewRules.CentreInView(_camera.fieldOfView, _camera.aspect);
            if (item.TryGetComponent(out Worktown.Game.Cooking.Spatula _))
            {
                // Spec 031b: on a flip the spatula scoops forward and turns over its long side (local, like the knife's chop).
                float flip = FlipAmount(Time.unscaledTime - _flipStart);
                inView += new Vector3(-FlipReach * flip, FlipLift * flip, FlipReach * flip);
                rotation = eye.rotation * Quaternion.Euler(-FlipPitch * flip, 0f, FlipRoll * flip) * HoldViewRules.Tilt;
            }
            Vector3 middle = eye.TransformPoint(inView);
            pose = new Pose(middle - rotation * (centre * scale), rotation);
            // Spec 033: while pouring, the cup moves from the hand to under the tap (an ease-out, feel.md 2) and back after the release.
            float share = PourShare(Time.unscaledTime);
            if (share > 0f)
            {
                Vector3 bottom = new Vector3(local.center.x, local.min.y, local.center.z);
                Vector3 lossyBottom = Vector3.Scale(lossy, bottom);
                var under = new Pose(_pourPose.position - _pourPose.rotation * lossyBottom, _pourPose.rotation);
                pose = new Pose(Vector3.Lerp(pose.position, under.position, share), Quaternion.Slerp(pose.rotation, under.rotation, share));
                scale = Mathf.Lerp(scale, 1f, share);
            }
            return true;
        }

        // Spec 033: the cup under a tap while pouring (first person; others see it in the hand).
        private bool _pouring;
        private Pose _pourPose;
        private float _pourChanged = float.NegativeInfinity;
        private float _pourShareAtChange;

        /// <summary>
        /// Spec 033: the held cup goes under a tap (<paramref name="on"/>, its bottom at <paramref name="under"/>) and comes back on the
        /// release; the move takes <see cref="Worktown.Game.Art.DrinkArtRules.UnderTapSeconds"/> and starts where it is.
        /// </summary>
        public void SetPourPose(bool on, Pose under)
        {
            if (on) _pourPose = under;
            if (on == _pouring) return;
            float now = Time.unscaledTime;
            _pourShareAtChange = PourShare(now);
            _pouring = on;
            _pourChanged = now;
        }

        /// <summary>Spec 033: how far (0..1) the held cup is drawn towards the tap now.</summary>
        public float PourShare(float now)
        {
            float t = Worktown.Game.Art.DrinkArtRules.UnderTapShare(now - _pourChanged, Worktown.Game.Art.DrinkArtRules.UnderTapSeconds);
            return _pouring ? Mathf.Lerp(_pourShareAtChange, 1f, t) : Mathf.Lerp(_pourShareAtChange, 0f, t);
        }

        /// <summary>Spec 031b: the held item's size (m, its model's diagonal) and drawn scale in the view pose asked last (tour log).</summary>
        public float LastViewSize { get; private set; }
        public float LastViewScale { get; private set; } = 1f;

        /// <summary>Spec 031b: one flip with the spatula in view (the owner clicked a flip; other players see the food's arc).</summary>
        public void PlayFlip()
        {
            _flipStart = Time.unscaledTime;
        }

        /// <summary>0 at rest, up to 1 in the middle of a flip, back to 0 after <see cref="FlipSeconds"/>.</summary>
        public static float FlipAmount(float elapsed)
        {
            if (elapsed < 0f || elapsed >= FlipSeconds) return 0f;
            return Mathf.Sin(elapsed / FlipSeconds * Mathf.PI);
        }

        /// <summary>
        /// The item's model bounds in its root's space (labels excluded), measured while the model rests on the root (the view
        /// pose is asked before the frame's tween moves it). Looked up again for another item, or every few frames (a plate can
        /// get food while held). No allocation per frame.
        /// </summary>
        private Bounds LocalBounds(Pickup item)
        {
            if (item == _boundsItem && Time.frameCount < _nextBoundsFrame) return _bounds;
            _boundsItem = item;
            _nextBoundsFrame = Time.frameCount + BoundsFrames;
            Transform root = item.transform;
            item.GetComponentsInChildren(false, _meshes);
            bool any = false;
            Bounds result = new Bounds(Vector3.zero, Vector3.zero);
            foreach (MeshFilter f in _meshes)
            {
                // Only drawn meshes: no label, no particles (the steam's bounds are large), no switched-off fallback cube.
                if (f == null || f.sharedMesh == null || !f.TryGetComponent(out MeshRenderer r) || !r.enabled || f.TryGetComponent(out TextMesh _))
                    continue;
                Bounds b = f.sharedMesh.bounds;
                Transform t = f.transform;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    Vector3 p = root.InverseTransformPoint(t.TransformPoint(corner));
                    if (!any)
                    {
                        result = new Bounds(p, Vector3.zero);
                        any = true;
                    }
                    else result.Encapsulate(p);
                }
            }
            _meshes.Clear();
            _bounds = any ? result : new Bounds(Vector3.zero, Vector3.one * 0.1f);
            return _bounds;
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
