using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Cooking;
using Worktown.Game.Player;

namespace Worktown.Game.Tutorial
{
    /// <summary>
    /// Spec 038, owner client: the tutorial's target pulses in Mustard even when it is not aimed at. It reuses the 027b outline idea (a
    /// slightly larger copy of each target mesh drawn from the inside, so only a rim shows) with its own hulls and its own material,
    /// a little wider than the Interact outline, so the aim's own highlight and outline (InteractionHighlight, per-material blocks) are
    /// never touched and both show together. The hulls are separate objects without labels, so the 027d label manager and the 031b calm
    /// view (rings) are unaffected. Hulls are made when the target changes; each frame only moves them and sets the pulse colour (no
    /// allocation). Hidden while the hulls' source is carried or flying (like the 027b outline).
    /// </summary>
    public sealed class TutorialHighlight
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int CullId = Shader.PropertyToID("_Cull");
        /// <summary>Times the 027b outline width (InteractionHighlight.OutlineScaleFor), so the pulse rings the Interact outline.</summary>
        public const float WidthFactor = 2.2f;
        public const float MinAlpha = 0.3f, MaxAlpha = 0.95f;

        private readonly Material _material;
        private readonly List<Renderer> _sources = new List<Renderer>(32);
        private readonly List<Transform> _hulls = new List<Transform>(32);
        private readonly List<Vector3> _centres = new List<Vector3>(32);
        private readonly List<float> _scales = new List<float>(32);
        private readonly List<Renderer> _scratch = new List<Renderer>(32);
        private readonly List<Material> _materials = new List<Material>(8);
        private Component _target;

        public TutorialHighlight(Material source)
        {
            if (source == null) return;
            _material = new Material(source) { name = "TutorialPulse" };
            if (_material.HasProperty(CullId)) _material.SetFloat(CullId, (float)UnityEngine.Rendering.CullMode.Front);
        }

        public Component Target => _target;
        public bool HasMaterial => _material != null;

        /// <summary>The world centre of what is lit (for the edge arrow); the target's position when it has no mesh.</summary>
        public bool TryGetCentre(out Vector3 centre)
        {
            centre = default;
            if (_target == null) return false;
            if (_sources.Count == 0)
            {
                centre = _target is StockSlot slot ? slot.Anchor : _target.transform.position;
                return true;
            }
            Bounds b = default;
            bool any = false;
            for (int i = 0; i < _sources.Count; i++)
            {
                Renderer r = _sources[i];
                if (r == null) continue;
                if (!any) b = r.bounds;
                else b.Encapsulate(r.bounds);
                any = true;
            }
            centre = any ? b.center : _target.transform.position;
            return true;
        }

        /// <summary>Lights <paramref name="target"/> (null: nothing). Rebuilds the hulls only when it changes.</summary>
        public void Show(Component target)
        {
            if (target == _target) return;
            Clear();
            _target = target;
            if (target == null || _material == null) return;
            Collect(target);
            if (_sources.Count == 0 && target is StockSlot slot && slot.Station != null) Collect(slot.Station); // a slot without its own mesh
            foreach (Renderer source in _sources)
            {
                if (!(source is MeshRenderer) || !source.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
                var hull = new GameObject("TutorialPulse");
                hull.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var renderer = hull.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                source.GetSharedMaterials(_materials);
                var materials = new Material[Mathf.Max(1, _materials.Count)];
                for (int i = 0; i < materials.Length; i++) materials[i] = _material;
                renderer.sharedMaterials = materials;
                _materials.Clear();
                _hulls.Add(hull.transform);
                _centres.Add(filter.sharedMesh.bounds.center);
                _scales.Add(1f + InteractionHighlight.OutlineScaleFor(source.bounds.extents.magnitude) * WidthFactor);
            }
            // Keep only the sources that got a hull, in the same order (index i matches).
            int k = 0;
            for (int i = 0; i < _sources.Count; i++)
            {
                Renderer source = _sources[i];
                if (!(source is MeshRenderer) || !source.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
                _sources[k++] = source;
            }
            if (k < _sources.Count) _sources.RemoveRange(k, _sources.Count - k);
        }

        /// <summary>Every frame: the hulls follow their meshes and pulse (<paramref name="pulse01"/>).</summary>
        public void Update(float pulse01)
        {
            if (_material == null || _hulls.Count == 0) return;
            _material.SetColor(BaseColorId, Palette.WithAlpha(Palette.Mustard, Mathf.Lerp(MinAlpha, MaxAlpha, pulse01)));
            bool moving = _target is Component c && c != null && c.TryGetComponent(out Worktown.Game.Net.Pickup item) && (item.IsHeld || item.HasPrediction);
            for (int i = 0; i < _hulls.Count; i++)
            {
                Transform hull = _hulls[i];
                Renderer source = i < _sources.Count ? _sources[i] : null;
                if (hull == null) continue;
                bool visible = !moving && source != null && source.enabled && source.gameObject.activeInHierarchy;
                if (hull.gameObject.activeSelf != visible) hull.gameObject.SetActive(visible);
                if (!visible) continue;
                Vector3 centre = _centres[i];
                float s = _scales[i];
                Matrix4x4 m = source.transform.localToWorldMatrix * Matrix4x4.Translate(centre) * Matrix4x4.Scale(Vector3.one * s)
                              * Matrix4x4.Translate(-centre);
                hull.SetPositionAndRotation(m.GetColumn(3), m.rotation);
                hull.localScale = m.lossyScale;
            }
        }

        public void Clear()
        {
            foreach (Transform hull in _hulls)
            {
                if (hull == null) continue;
                hull.gameObject.SetActive(false);
                Object.Destroy(hull.gameObject);
            }
            _hulls.Clear();
            _sources.Clear();
            _centres.Clear();
            _scales.Clear();
            _target = null;
        }

        public void Destroy()
        {
            Clear();
            if (_material != null) Object.Destroy(_material);
        }

        /// <summary>The target's visible renderers, without labels; a station without its buttons (unless a button is the target).</summary>
        private void Collect(Component target)
        {
            target.GetComponentsInChildren(false, _scratch);
            bool station = target is Station;
            foreach (Renderer r in _scratch)
            {
                if (r == null || r.TryGetComponent(out TextMesh _)) continue;
                if (station && r.TryGetComponent(out StationButton _)) continue;
                _sources.Add(r);
            }
            _scratch.Clear();
        }
    }
}
