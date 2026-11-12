using System.Collections.Generic;
using UnityEngine;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 027b (docs/feel.md 4): a translucent copy of the item in your hands, drawn exactly where a click would put it
    /// (the spot the server would pick, or into the cookware). Local and visual only: copies of the item's meshes under
    /// one root, no colliders, no shadows, rebuilt only when the held item changes. Labels (TextMesh) are left out.
    /// </summary>
    public sealed class PlacementGhost
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly Material _material;
        private readonly List<GameObject> _parts = new List<GameObject>();
        private readonly List<Material> _scratch = new List<Material>(4);
        private readonly List<MeshRenderer> _renderers = new List<MeshRenderer>(16);
        private GameObject _root;
        private Transform _source;
        private int _sourceRenderers = -1;

        /// <param name="material">A translucent material (the place marker's); copied and tinted.</param>
        public PlacementGhost(Material material, Color color)
        {
            if (material == null) return;
            _material = new Material(material) { name = "PlacementGhost" };
            if (_material.HasProperty(BaseColorId)) _material.SetColor(BaseColorId, color);
        }

        public bool IsShown => _root != null && _root.activeSelf;

        /// <summary>Spec 062b: the ghost's tint (build mode: Mint where the piece fits, Terracotta where it does not).</summary>
        public void SetColor(Color color)
        {
            if (_material != null && _material.HasProperty(BaseColorId)) _material.SetColor(BaseColorId, color);
        }

        /// <summary>World position of the ghost (for tests and the visual check log).</summary>
        public Vector3 Position => _root != null ? _root.transform.position : Vector3.zero;

        /// <summary>Shows a copy of <paramref name="item"/> with its frame at <paramref name="pose"/>.</summary>
        public void Show(Transform item, Pose pose)
        {
            if (_material == null || item == null)
            {
                Hide();
                return;
            }
            int renderers = CountRenderers(item);
            if (item != _source || renderers != _sourceRenderers) Rebuild(item, renderers);
            _root.transform.SetPositionAndRotation(pose.position, pose.rotation);
            _root.transform.localScale = item.lossyScale;
            if (!_root.activeSelf) _root.SetActive(true);
        }

        public void Hide()
        {
            if (_root != null && _root.activeSelf) _root.SetActive(false);
        }

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _parts.Clear();
            _source = null;
            if (_material != null) Object.Destroy(_material);
        }

        private int CountRenderers(Transform item)
        {
            item.GetComponentsInChildren(false, _renderers);
            int count = 0;
            foreach (MeshRenderer r in _renderers)
                if (!r.TryGetComponent(out TextMesh _)) count++;
            _renderers.Clear();
            return count;
        }

        private void Rebuild(Transform item, int renderers)
        {
            if (_root == null) _root = new GameObject("PlacementGhost");
            foreach (GameObject part in _parts)
                if (part != null) Object.Destroy(part);
            _parts.Clear();
            _source = item;
            _sourceRenderers = renderers;
            Matrix4x4 toItem = item.worldToLocalMatrix;
            foreach (MeshRenderer source in item.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (source.TryGetComponent(out TextMesh _) || !source.enabled) continue;
                if (!source.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
                var part = new GameObject("Part");
                part.transform.SetParent(_root.transform, false);
                Matrix4x4 local = toItem * source.transform.localToWorldMatrix;
                part.transform.localPosition = local.GetColumn(3);
                part.transform.localRotation = local.rotation;
                part.transform.localScale = local.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var renderer = part.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                source.GetSharedMaterials(_scratch);
                var materials = new Material[Mathf.Max(1, _scratch.Count)];
                for (int i = 0; i < materials.Length; i++) materials[i] = _material;
                renderer.sharedMaterials = materials;
                _scratch.Clear();
                _parts.Add(part);
            }
        }
    }
}
