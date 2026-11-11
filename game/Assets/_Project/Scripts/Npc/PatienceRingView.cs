using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Sim.Economy;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// Spec 032, client only: the patience ring over a waiting guest (<see cref="PatienceRingRules"/>), from the synced
    /// <see cref="Customer.Patience"/> byte. It faces the camera beside the head and drains as the server's byte goes down; Terracotta
    /// and a small pulse near the end. Hidden while the guest pays or leaves. The mesh is rebuilt only when the byte changes; no
    /// allocations per frame.
    /// </summary>
    [RequireComponent(typeof(Customer))]
    public sealed class PatienceRingView : MonoBehaviour
    {
        private Customer _customer;
        private GameObject _ring;
        private MeshRenderer _renderer;
        private Mesh _mesh;
        private Material _material;
        private int _shownByte = -1;
        private int _shownPalette = -1; // spec 048: RingPalette.Version of the built mesh
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<int> _triangles = new List<int>();

        /// <summary>True while the ring is drawn (tests and the tour log).</summary>
        public bool IsShown => _renderer != null && _renderer.enabled;

        /// <summary>The share of patience the ring shows.</summary>
        public float ShownShare => _shownByte < 0 ? 1f : GuestPatience.FromByte((byte)_shownByte);

        private void Awake()
        {
            _customer = GetComponent<Customer>();
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                enabled = false;
                return;
            }
            _ring = new GameObject("PatienceRing");
            _ring.transform.SetParent(transform, false);
            _mesh = new Mesh { name = "PatienceRing" };
            _mesh.MarkDynamic();
            _ring.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _ring.AddComponent<MeshRenderer>();
            _material = OverlayMaterial.Create(null, "PatienceRing");
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.enabled = false;
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_material != null) Destroy(_material);
        }

        private void LateUpdate()
        {
            if (_renderer == null) return;
            Camera cam = Camera.main;
            bool show = cam != null && _customer.IsSpawned && PatienceRingRules.IsShown(_customer.Waiting);
            _renderer.enabled = show;
            if (!show) return;
            byte left = _customer.Patience.Value;
            float share = GuestPatience.FromByte(left);
            if (left != _shownByte || _shownPalette != RingPalette.Version)
            {
                _shownByte = left;
                _shownPalette = RingPalette.Version;
                PatienceRingRules.Build(share, _vertices, _colors, _triangles);
                HeatRingRules.ToVertexColors(_colors, QualitySettings.activeColorSpace == ColorSpace.Linear);
                _mesh.Clear();
                _mesh.SetVertices(_vertices);
                _mesh.SetColors(_colors);
                _mesh.SetTriangles(_triangles, 0);
            }
            Transform view = cam.transform;
            Vector3 right = view.right;
            right.y = 0f;
            Vector3 at = transform.position + Vector3.up * PatienceRingRules.Height + right.normalized * PatienceRingRules.SideOffset;
            _ring.transform.position = at;
            _ring.transform.rotation = Quaternion.LookRotation(at - view.position, view.up);
            float size = PatienceRingRules.WorldSize * 0.5f * PatienceRingRules.Pulse(share, Time.unscaledTime);
            _ring.transform.localScale = new Vector3(size, size, size);
        }
    }
}
