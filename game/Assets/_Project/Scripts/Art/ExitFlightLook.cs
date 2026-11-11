using UnityEngine;
using Worktown.Game.Audio;
using Worktown.Game.Player;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 044 (027b and 035 feels off): another player's item that leaves the world into something (food into a pot, scraps into the
    /// trash can, the bag into the bin, a plate into the sink, a box into its slot) no longer vanishes from their hand. When such an item
    /// is despawned, every viewer that did not predict it (<see cref="ExitFlightRules.ShowsFlight"/>) draws a copy of its model (renderers
    /// only, no colliders, no scripts) that flies on an arc from where it was drawn last into the destination the server named, shrinks a
    /// little as it goes in, plays the landing sound there and is gone. Look only; client side.
    /// </summary>
    public sealed class ExitFlightLook : MonoBehaviour
    {
        /// <summary>Name of the copy's game object.</summary>
        public const string ObjectName = "ExitFlightLook";

        private Pose _from;
        private Vector3 _to;
        private float _start;
        private float _seconds;
        private float _lift;
        private Vector3 _scale;
        private SoundKey _sound;
        private bool _hasSound;
        private bool _done;

        /// <summary>Spec 044: a flight started (from, to); the visual check's observer films the first few. Client only.</summary>
        public static event System.Action<Vector3, Vector3> Started;

        /// <summary>True once it has landed (the sound played; the object is destroyed in play mode).</summary>
        public bool Landed => _done;

        /// <summary>
        /// A copy of <paramref name="root"/>'s model drawn at <paramref name="drawn"/> (the item's frame as it was drawn last), flying to
        /// <paramref name="to"/>. <paramref name="sound"/> (if any) plays when it lands. Null when the item shows no model.
        /// </summary>
        public static ExitFlightLook Begin(Transform root, Pose drawn, Vector3 to, SoundKey? sound, float now, FeelSettings feel = null)
        {
            if (root == null) return null;
            feel ??= FeelSettings.Current ?? FeelSettings.PresetA();
            var copy = new GameObject(ObjectName);
            copy.transform.SetPositionAndRotation(drawn.position, drawn.rotation);
            copy.transform.localScale = root.lossyScale;
            int parts = 0;
            var block = new MaterialPropertyBlock();
            Quaternion inverse = Quaternion.Inverse(root.rotation);
            Vector3 rootScale = root.lossyScale;
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!r.enabled || r.GetComponent<TextMesh>() != null || !r.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
                var part = new GameObject("Part");
                part.transform.SetParent(copy.transform, false);
                part.transform.localPosition = root.InverseTransformPoint(r.transform.position);
                part.transform.localRotation = inverse * r.transform.rotation;
                Vector3 s = r.transform.lossyScale;
                part.transform.localScale = new Vector3(Div(s.x, rootScale.x), Div(s.y, rootScale.y), Div(s.z, rootScale.z));
                part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var renderer = part.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = r.sharedMaterials;
                renderer.shadowCastingMode = r.shadowCastingMode;
                if (r.HasPropertyBlock())
                {
                    r.GetPropertyBlock(block);
                    renderer.SetPropertyBlock(block);
                }
                parts++;
            }
            if (parts == 0)
            {
                Gone(copy);
                return null;
            }
            var look = copy.AddComponent<ExitFlightLook>();
            look._from = drawn;
            look._to = to;
            look._start = now;
            look._seconds = ExitFlightRules.Seconds(feel);
            look._lift = ExitFlightRules.Lift(feel);
            look._scale = rootScale;
            look._hasSound = sound.HasValue;
            look._sound = sound ?? default;
            Started?.Invoke(drawn.position, to);
            return look;
        }

        private void Update() => Step(Time.unscaledTime);

        /// <summary>Places the copy for <paramref name="now"/>; lands (sound, gone) at the end.</summary>
        public void Step(float now)
        {
            if (_done) return;
            float t = (now - _start) / _seconds;
            if (t >= 1f || float.IsNaN(t))
            {
                _done = true;
                if (_hasSound) KitchenSounds.PlayOnce(_sound, _to);
                Gone(gameObject);
                return;
            }
            transform.position = FeelRules.TweenPosition(TweenStyle.Arc, _from.position, _to, t, _lift);
            transform.localScale = _scale * ExitFlightRules.Size(t);
        }

        private static float Div(float a, float b) => Mathf.Abs(b) > 1e-6f ? a / b : a;

        private static void Gone(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }
}
