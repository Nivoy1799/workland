using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Player;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 027b: where an item's model is drawn, eased from its last drawn pose whenever its goal changes (docs/feel.md 2:
    /// nothing teleports). Client visuals only. A pose here is the item's frame: the model children sit at
    /// <c>pose.position + pose.rotation * (root scale x their local position)</c>, which is exactly where they are when the
    /// pose equals the root's own pose. <see cref="Apply"/> moves only the visual children (not labels) for one camera
    /// render and <see cref="Restore"/> puts them back right after, so the networked transform, the colliders and every
    /// rule always see the real state. The root itself is never moved (review of spec 027b): an item whose mesh sits on
    /// the root (the spec 003 cube) gets a visual child first (<see cref="EnsureVisualChild"/>); without one nothing moves.
    /// The list of children is cached and only collected again when the number of children changes.
    /// </summary>
    public sealed class ItemTween
    {
        /// <summary>Name of the child <see cref="EnsureVisualChild"/> adds.</summary>
        public const string VisualChildName = "TweenVisual";

        private readonly Transform _root;
        private readonly List<Transform> _parts = new List<Transform>();
        private readonly List<Vector3> _positions = new List<Vector3>();
        private readonly List<Quaternion> _rotations = new List<Quaternion>();
        private readonly List<Vector3> _scales = new List<Vector3>(); // spec 031b
        private bool _scaled;
        private int _partsForChildCount = -1;
        private bool _applied;

        private TweenStyle _style;
        private Pose _from;
        private float _start;
        private float _seconds;
        private float _amount;

        public ItemTween(Transform root)
        {
            _root = root;
        }

        /// <summary>The pose drawn last (valid once <see cref="HasDrawn"/>).</summary>
        public Pose Drawn { get; private set; }
        public bool HasDrawn { get; private set; }
        public TweenStyle Style => _style;

        /// <summary>The root's own pose: drawn there, the children are exactly where the scene has them.</summary>
        public static Pose RootPose(Transform root) => new Pose(root.position, root.rotation);

        /// <summary>
        /// Client only: if the item's mesh sits on the root and it has no visual child yet, moves the look to a new child
        /// (same mesh and materials) and turns the root's renderer off. The root keeps its collider and every networked
        /// component; only the child is ever moved. Returns true when a child was added.
        /// </summary>
        public static bool EnsureVisualChild(Transform root)
        {
            if (root == null || HasVisualChild(root)) return false;
            if (!root.TryGetComponent(out MeshRenderer renderer) || !root.TryGetComponent(out MeshFilter filter)) return false;
            var child = new GameObject(VisualChildName);
            child.transform.SetParent(root, false);
            child.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var copy = child.AddComponent<MeshRenderer>();
            copy.sharedMaterials = renderer.sharedMaterials;
            copy.shadowCastingMode = renderer.shadowCastingMode;
            copy.receiveShadows = renderer.receiveShadows;
            renderer.enabled = false;
            return true;
        }

        private static bool HasVisualChild(Transform root)
        {
            foreach (Transform child in root)
                if (!child.TryGetComponent(out TextMesh _)) return true;
            return false;
        }

        public bool IsActive(float now) => _style != TweenStyle.None && now - _start < _seconds;

        /// <summary>
        /// Starts easing from the last drawn pose to whatever goal <see cref="Evaluate"/> gets next (a moving goal, such as
        /// a walking holder's hand, is followed). Replaces a running tween cleanly: it starts from where the item is drawn
        /// now. Nothing to ease from before the first drawing, or for <see cref="TweenStyle.None"/>.
        /// </summary>
        public void Begin(TweenStyle style, float seconds, float amount, float now)
        {
            if (!HasDrawn || style == TweenStyle.None || !(seconds > 0f))
            {
                _style = TweenStyle.None;
                return;
            }
            _from = Drawn;
            _style = style;
            _seconds = seconds;
            _amount = amount;
            _start = now;
        }

        /// <summary>Forgets the history (respawned, pooled or despawned item): the next pose is drawn as it is.</summary>
        public void Reset()
        {
            _style = TweenStyle.None;
            HasDrawn = false;
        }

        /// <summary>The pose to draw at <paramref name="now"/> for <paramref name="goal"/>; remembers it as drawn.</summary>
        public Pose Evaluate(Pose goal, float now)
        {
            Pose result = goal;
            if (_style != TweenStyle.None)
            {
                float t = (now - _start) / _seconds;
                if (t >= 1f || float.IsNaN(t)) _style = TweenStyle.None;
                else
                    result = new Pose(FeelRules.TweenPosition(_style, _from.position, goal.position, t, _amount),
                        FeelRules.TweenRotation(_style, _from.rotation, goal.rotation, t));
            }
            Drawn = result;
            HasDrawn = true;
            return result;
        }

        /// <summary>
        /// Draws the item at <paramref name="pose"/> until <see cref="Restore"/>: moves the visual children (labels stay).
        /// Does nothing when the pose is the root's own, or when the item has no visual child (the root never moves).
        /// </summary>
        public void Apply(Pose pose) => Apply(pose, 1f);

        /// <summary>
        /// Spec 031b: as <see cref="Apply(Pose)"/>, drawn at <paramref name="size"/> times its size (a large item held in the
        /// first-person view is drawn smaller). The scale is put back by <see cref="Restore"/> as well.
        /// </summary>
        public void Apply(Pose pose, float size)
        {
            Restore();
            bool scaled = !float.IsNaN(size) && size > 0f && Mathf.Abs(size - 1f) > 1e-4f;
            if (_root == null || (IsRootPose(pose) && !scaled)) return;
            CollectParts();
            if (_parts.Count == 0) return;
            float s = scaled ? size : 1f;
            Vector3 scale = _root.lossyScale;
            for (int i = 0; i < _parts.Count; i++)
            {
                Transform part = _parts[i];
                if (part == null) continue;
                _positions[i] = part.localPosition;
                _rotations[i] = part.localRotation;
                _scales[i] = part.localScale;
                part.SetPositionAndRotation(pose.position + pose.rotation * (Vector3.Scale(scale, _positions[i]) * s), pose.rotation * _rotations[i]);
                if (scaled) part.localScale = _scales[i] * s;
            }
            _scaled = scaled;
            _applied = true;
        }

        /// <summary>Puts everything <see cref="Apply"/> moved back where it was.</summary>
        public void Restore()
        {
            if (!_applied) return;
            _applied = false;
            for (int i = 0; i < _parts.Count; i++)
            {
                if (_parts[i] == null) continue;
                _parts[i].SetLocalPositionAndRotation(_positions[i], _rotations[i]);
                if (_scaled) _parts[i].localScale = _scales[i];
            }
            _scaled = false;
        }

        /// <summary>The visual children (not labels), collected again only when the number of children changed.</summary>
        private void CollectParts()
        {
            if (_root.childCount == _partsForChildCount) return;
            _partsForChildCount = _root.childCount;
            _parts.Clear();
            _positions.Clear();
            _rotations.Clear();
            _scales.Clear();
            foreach (Transform child in _root)
            {
                if (child.TryGetComponent(out TextMesh _)) continue;
                _parts.Add(child);
                _positions.Add(Vector3.zero);
                _rotations.Add(Quaternion.identity);
                _scales.Add(Vector3.one);
            }
        }

        private bool IsRootPose(Pose pose)
        {
            return (pose.position - _root.position).sqrMagnitude < 1e-8f && Quaternion.Angle(pose.rotation, _root.rotation) < 0.01f;
        }
    }
}
