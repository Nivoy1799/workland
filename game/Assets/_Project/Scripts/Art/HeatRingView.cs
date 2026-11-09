using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Net;
using Worktown.Game.Player;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 027c, client only: a small ring next to a pan or pot on the heat (reusable for the fryer and griddle, specs
    /// 029/030: it only needs a <see cref="Vessel"/>'s heat bytes and a "hot" source). The fill is the synced progress, the
    /// rim marks the good window (Sage, its Perfect half Mustard), the fill's colour is the zone and it pulses while the
    /// warning is on (<see cref="HeatRingRules"/>). It faces the camera, is <see cref="HeatRingRules.RingPixels"/> wide on
    /// screen at any distance, stands to the camera's right of the contents, and hides in first person when it would cover
    /// the view centre. Display only: the server computes the bytes. The ring is its own scene object (not a child of the
    /// vessel), so the item tween, the placement ghost and the highlight never pick it up.
    /// </summary>
    public sealed class HeatRingView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        /// <summary>The ring's centre from the contents: to the camera's right by this much plus its radius and gap.</summary>
        private const float SideOffset = 0.16f;
        private const float UpOffset = 0.05f;
        private const float WarnPulseHz = 4f;
        /// <summary>Spec 029: how far above a fryer basket's contents its ring stands (m).</summary>
        public const float BasketLift = 0.2f;
        private float _lift;
        private float _side = 1f;

        private Vessel _vessel;
        private VesselSteam _steam;
        private Pickup _pickup;
        private Transform _anchor;
        private GameObject _ring;
        private MeshRenderer _renderer;
        private Material _material;
        private float _nextRigLookup;
        private Mesh _mesh;
        private MaterialPropertyBlock _block;
        private PlayerCameraRig _rig;
        private float _lastHot = float.NegativeInfinity;
        private Rect _screenRect;
        private bool _screenInFront;
        private int _shownProgress = -1;
        private HeatZone _shownZone = HeatZone.None;
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<int> _triangles = new List<int>();

        /// <summary>True while the ring is drawn (for tests and the tour log).</summary>
        public bool IsShown => _renderer != null && _renderer.enabled;

        /// <summary>Spec 027d: the ring's screen rectangle in pixels while it is drawn; world labels give way to it (it is never culled).</summary>
        public bool TryGetScreenRect(out Rect rect)
        {
            rect = _screenRect;
            return IsShown && _screenInFront; // second review: not a rectangle when the ring is behind the camera
        }

        /// <summary>Called once by the vessel's <see cref="VesselSteam"/> (client only).</summary>
        public void Init(Vessel vessel, VesselSteam steam, Material source)
        {
            _vessel = vessel;
            _steam = steam;
            _pickup = vessel.GetComponent<Pickup>();
            _anchor = vessel.ContentsAnchor;
            if (_anchor == null) _anchor = vessel.transform;
            // Spec 029: a basket's contents sit under the oil; its ring stands above the oil and the basket's rim.
            _lift = vessel.Kind == VesselKind.Basket ? BasketLift : 0f;
            // Spec 029: the left basket's ring stands on its left, so the other basket never hides it.
            _side = vessel.TryGetComponent(out FryerBasket basket) && basket.Basket == 0 ? -1f : 1f;

            _ring = new GameObject("HeatRing (" + vessel.name + ")");
            _mesh = new Mesh { name = "HeatRing" };
            _mesh.MarkDynamic();
            _ring.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _ring.AddComponent<MeshRenderer>();
            _material = OverlayMaterial.Create(source, "HeatRing");
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.enabled = false;
        }

        private void OnDisable()
        {
            if (_renderer != null) _renderer.enabled = false;
        }

        private void OnDestroy()
        {
            if (_ring != null) Destroy(_ring);
            if (_mesh != null) Destroy(_mesh);
            if (_material != null) Destroy(_material); // review: each ring owns its overlay material
        }

        private void LateUpdate()
        {
            if (_vessel == null || _renderer == null) return;
            float now = Time.unscaledTime;
            bool hot = _steam != null && _steam.IsHot;
            if (hot) _lastHot = now;
            HeatZone zone = _vessel.Zone;
            Camera cam = Camera.main;
            bool held = _pickup != null && _pickup.IsHeld;
            if (cam == null || !HeatRingRules.IsShown(zone, held, hot, now - _lastHot))
            {
                _renderer.enabled = false;
                return;
            }

            Transform view = cam.transform;
            Vector3 anchor = _anchor.position + Vector3.up * _lift;
            float distance = Vector3.Dot(anchor - view.position, view.forward);
            float perPixel = HeatRingRules.WorldPerPixel(distance, cam.fieldOfView, cam.pixelHeight);
            if (perPixel <= 0f)
            {
                _renderer.enabled = false;
                return;
            }
            float radius = HeatRingRules.RingPixels * 0.5f * perPixel;
            Vector3 centre = anchor + view.right * (_side * (SideOffset + radius + HeatRingRules.GapPixels * perPixel))
                + view.up * (UpOffset + radius * 0.3f);

            if (_rig == null && now >= _nextRigLookup)
            {
                // Review: looked up at most once a second until the local player has bound it, not every frame.
                _nextRigLookup = now + 1f;
                _rig = FindAnyObjectByType<PlayerCameraRig>();
            }
            if (_rig != null && _rig.IsFirstPerson)
            {
                Vector3 screen = cam.WorldToScreenPoint(centre);
                if (HeatRingRules.CoversCentre(screen, HeatRingRules.RingPixels * 0.5f, new Vector2(cam.pixelWidth, cam.pixelHeight)))
                {
                    _renderer.enabled = false;
                    return;
                }
            }

            int progress = _vessel.HeatProgress.Value;
            if (progress != _shownProgress || zone != _shownZone) Rebuild(progress, zone);
            Vector3 ringScreen = cam.WorldToScreenPoint(centre);
            _screenInFront = ringScreen.z > 0f;
            float halfPixels = HeatRingRules.RingPixels * 0.5f;
            _screenRect = new Rect(ringScreen.x - halfPixels, ringScreen.y - halfPixels, halfPixels * 2f, halfPixels * 2f);
            _ring.transform.SetPositionAndRotation(centre, view.rotation);
            _ring.transform.localScale = Vector3.one * radius;

            _block ??= new MaterialPropertyBlock();
            float alpha = _vessel.Warning ? 0.55f + 0.45f * Mathf.Abs(Mathf.Cos(now * Mathf.PI * WarnPulseHz)) : 1f;
            _block.SetColor(BaseColorId, Palette.WithAlpha(Color.white, alpha));
            _renderer.SetPropertyBlock(_block);
            _renderer.enabled = true;
        }

        private void Rebuild(int progress, HeatZone zone)
        {
            _shownProgress = progress;
            _shownZone = zone;
            HeatRingRules.Build(progress / 255f, zone, _vertices, _colors, _triangles);
            HeatRingRules.ToVertexColors(_colors, QualitySettings.activeColorSpace == ColorSpace.Linear);
            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_triangles, 0);
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2.2f);
        }
    }
}
