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
    /// rule always see the real state. An item without children (the spec 003 cube) moves as a whole for the render, as
    /// the spec 024 hand drawing did.
    /// </summary>
    public sealed class ItemTween
    {
        private readonly Transform _root;
        private readonly List<Transform> _moved = new List<Transform>();
        private readonly List<Vector3> _positions = new List<Vector3>();
        private readonly List<Quaternion> _rotations = new List<Quaternion>();
        private Vector3 _rootPosition;
        private Quaternion _rootRotation;
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
        /// Draws the item at <paramref name="pose"/> until <see cref="Restore"/>: moves the visual children (labels stay),
        /// or the root of an item without children. Does nothing when the pose is the root's own (nothing to move).
        /// </summary>
        public void Apply(Pose pose)
        {
            Restore();
            if (_root == null) return;
            if (IsRootPose(pose)) return;
            foreach (Transform child in _root)
                if (!child.TryGetComponent(out TextMesh _)) _moved.Add(child);
            if (_moved.Count == 0)
            {
                _rootPosition = _root.position;
                _rootRotation = _root.rotation;
                _root.SetPositionAndRotation(pose.position, pose.rotation);
                _applied = true;
                return;
            }
            Vector3 scale = _root.lossyScale;
            foreach (Transform moved in _moved)
            {
                _positions.Add(moved.localPosition);
                _rotations.Add(moved.localRotation);
                moved.SetPositionAndRotation(pose.position + pose.rotation * Vector3.Scale(scale, moved.localPosition),
                    pose.rotation * moved.localRotation);
            }
            _applied = true;
        }

        /// <summary>Puts everything <see cref="Apply"/> moved back where it was.</summary>
        public void Restore()
        {
            if (!_applied) return;
            _applied = false;
            if (_moved.Count == 0)
            {
                if (_root != null) _root.SetPositionAndRotation(_rootPosition, _rootRotation);
                return;
            }
            for (int i = 0; i < _moved.Count; i++)
                if (_moved[i] != null) _moved[i].SetLocalPositionAndRotation(_positions[i], _rotations[i]);
            _moved.Clear();
            _positions.Clear();
            _rotations.Clear();
        }

        private bool IsRootPose(Pose pose)
        {
            return (pose.position - _root.position).sqrMagnitude < 1e-8f && Quaternion.Angle(pose.rotation, _root.rotation) < 0.01f;
        }
    }
}
