using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Cooking;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 020, owner only, first person: lights up what a click would act on, in the colour of
    /// <see cref="PickupInteractor.CurrentAction"/>: white to take (PickUp, Take, Buy), green to put or use
    /// (PutInto, PutOnBoard, Place, Sell, Press, Cut), grey when a click does nothing but the prompt says why. For
    /// Place a flat translucent disc marks the exact spot the item will land on.
    /// The glow tints the target's renderers through per-material MaterialPropertyBlocks (base colour pulled
    /// towards the glow colour, plus emission where the material has it enabled). URP Lit ignores
    /// <c>_EmissionColor</c> unless emission is on, and the asset-pack materials have it off, so the base colour
    /// tint is what is visible. The renderer-wide blocks other scripts use (FoodVisual tints, stove buttons) stay
    /// untouched and are read as the base, so the tint follows them; per-material blocks are owned by this
    /// component. One target at a time; it is re-applied every frame without allocating and cleared when the
    /// target or the colour changes.
    /// Spec 027b (docs/feel.md 3, 4): plus an outline in the Interact colour (grey when the click does nothing, such as
    /// "Too far"): a slightly larger copy of each target mesh drawn from the inside (front faces culled), so only its rim
    /// shows around the silhouette. While a click would put the held item somewhere, a translucent copy of it shows
    /// exactly where it lands (<see cref="PlacementGhost"/>). The target itself never flickers for single frames
    /// (PickupInteractor keeps it at least FeelSettings.HighlightMinSeconds).
    /// </summary>
    public sealed class InteractionHighlight : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        // Spec 026: palette colours (the guide keeps Interact for the highlight; using a station glows sage, refused grey).
        public static readonly Color TakeColor = Palette.Interact;
        public static readonly Color UseColor = Palette.Sage;
        public static readonly Color RefuseColor = Palette.Steel;
        private const float TintAmount = 0.35f;
        private const float Brighten = 1.15f;
        private const float EmissionAmount = 0.35f;
        private const float MarkerDiameter = 0.22f;
        private const float MarkerLift = 0.006f;
        /// <summary>Spec 027b: m of outline around a target, as a fraction of its size within the limits below.</summary>
        public const float OutlineWidth = 0.012f;
        public const float OutlineMinScale = 0.03f;
        public const float OutlineMaxScale = 0.25f;
        private static readonly int CullId = Shader.PropertyToID("_Cull");
        private const float GhostAlpha = 0.35f;
        private const float OutlineAlpha = 0.95f;

        [Tooltip("Translucent material of the place marker disc.")]
        [SerializeField] private Material _markerMaterial;

        private PickupInteractor _interactor;
        private PlayerCameraRig _rig;
        private PlayerInputReader _input;
        private Component _target;
        private Color _color;
        private Transform _marker;
        private readonly List<Renderer> _renderers = new List<Renderer>(32);
        private readonly List<Renderer> _scratch = new List<Renderer>(32);
        private readonly List<Material> _materials = new List<Material>(8);
        private MaterialPropertyBlock _block;
        // Spec 027b: the outline hulls (one per target mesh, not parented, synced every frame) and the placement ghost.
        private Material _outline;
        private readonly List<Renderer> _hullSources = new List<Renderer>(32);
        private readonly List<Transform> _hulls = new List<Transform>(32);
        private readonly List<Vector3> _hullCentres = new List<Vector3>(32);
        private readonly List<float> _hullScales = new List<float>(32);
        private PlacementGhost _ghost;

        public void Activate(PickupInteractor interactor)
        {
            if (_interactor != null || interactor == null) return;
            _interactor = interactor;
            _block = new MaterialPropertyBlock();
            _marker = CreateMarker();
            if (_markerMaterial != null)
            {
                _outline = new Material(_markerMaterial) { name = "InteractOutline" };
                if (_outline.HasProperty(CullId)) _outline.SetFloat(CullId, (float)UnityEngine.Rendering.CullMode.Front);
                _ghost = new PlacementGhost(_markerMaterial, Palette.WithAlpha(Palette.Interact, GhostAlpha));
            }
        }

        /// <summary>Spec 027b: the outline colour: Interact for any action, grey when the click does nothing (a reason).</summary>
        public static Color OutlineColorFor(InteractionAction action) =>
            action == InteractionAction.None ? RefuseColor : Palette.Interact;

        /// <summary>Spec 027b: outline scale for a mesh with this bounding radius (m): about <see cref="OutlineWidth"/> wide.</summary>
        public static float OutlineScaleFor(float radius)
        {
            if (!(radius > 0.001f)) return OutlineMaxScale;
            return Mathf.Clamp(OutlineWidth / radius, OutlineMinScale, OutlineMaxScale);
        }

        /// <summary>The glow colour for an action; null for no glow. <paramref name="hasReason"/>: a None with a prompt.</summary>
        public static Color? ColorFor(InteractionAction action, bool hasReason)
        {
            switch (action)
            {
                case InteractionAction.PickUp:
                case InteractionAction.Take:
                case InteractionAction.Buy:
                    return TakeColor;
                case InteractionAction.None:
                    return hasReason ? RefuseColor : (Color?)null;
                default:
                    return UseColor;
            }
        }

        private void LateUpdate()
        {
            if (_interactor == null) return;
            if (_rig == null) _rig = FindFirstObjectByType<PlayerCameraRig>();
            if (_input == null) _input = FindFirstObjectByType<PlayerInputReader>();
            bool active = _rig != null && _rig.IsFirstPerson && (_input == null || !_input.IsUiOpen);

            Component target = active ? _interactor.Target : null;
            InteractionAction action = active ? _interactor.CurrentAction : InteractionAction.None;
            Color? glow = target != null
                ? ColorFor(action, action == InteractionAction.None && InteractionRules.Reason(_interactor.CurrentHeld, _interactor.CurrentAim) != null)
                : null;
            if (!glow.HasValue) target = null;

            if (target != _target || (glow.HasValue && glow.Value != _color))
            {
                Clear();
                _target = target;
                if (target != null)
                {
                    _color = glow.Value;
                    Collect(target);
                    BuildOutline(OutlineColorFor(action));
                }
            }
            Apply();
            SyncOutline();
            UpdateMarker(active && action == InteractionAction.Place && _interactor.TargetSpot >= 0);
            UpdateGhost(active);
        }

        private void BuildOutline(Color color)
        {
            if (_outline == null) return;
            _outline.SetColor(BaseColorId, Palette.WithAlpha(color, OutlineAlpha));
            foreach (Renderer source in _renderers)
            {
                if (!(source is MeshRenderer) || !source.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
                var hull = new GameObject("InteractOutline");
                hull.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var renderer = hull.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                source.GetSharedMaterials(_materials);
                var materials = new Material[Mathf.Max(1, _materials.Count)];
                for (int i = 0; i < materials.Length; i++) materials[i] = _outline;
                renderer.sharedMaterials = materials;
                _materials.Clear();
                _hullSources.Add(source);
                _hulls.Add(hull.transform);
                _hullCentres.Add(filter.sharedMesh.bounds.center);
                _hullScales.Add(1f + OutlineScaleFor(source.bounds.extents.magnitude));
            }
        }

        /// <summary>Keeps every hull on its mesh, scaled about the mesh centre.</summary>
        private void SyncOutline()
        {
            // An item that is flying (predicted) or carried is drawn elsewhere for the render: no outline left behind.
            bool moving = TargetMoves();
            for (int i = 0; i < _hulls.Count; i++)
            {
                Renderer source = _hullSources[i];
                Transform hull = _hulls[i];
                if (hull == null) continue;
                bool visible = !moving && source != null && source.enabled && source.gameObject.activeInHierarchy;
                if (hull.gameObject.activeSelf != visible) hull.gameObject.SetActive(visible);
                if (!visible) continue;
                Vector3 c = _hullCentres[i];
                float s = _hullScales[i];
                Matrix4x4 m = source.transform.localToWorldMatrix * Matrix4x4.Translate(c) * Matrix4x4.Scale(Vector3.one * s)
                              * Matrix4x4.Translate(-c);
                hull.SetPositionAndRotation(m.GetColumn(3), m.rotation);
                hull.localScale = m.lossyScale;
            }
        }

        private bool TargetMoves()
        {
            Pickup item = _target as Pickup;
            if (item == null && _target != null) item = _target.GetComponent<Pickup>();
            return item != null && (item.IsHeld || item.HasPrediction);
        }

        private void ClearOutline()
        {
            foreach (Transform hull in _hulls)
            {
                if (hull == null) continue;
                hull.gameObject.SetActive(false); // gone this frame (Destroy waits for the end of the update)
                Destroy(hull.gameObject);
            }
            _hulls.Clear();
            _hullSources.Clear();
            _hullCentres.Clear();
            _hullScales.Clear();
        }

        private void UpdateGhost(bool active)
        {
            if (_ghost == null) return;
            if (active && _interactor.TryGetLandingPose(out Pickup held, out Pose pose)) _ghost.Show(held.transform, pose);
            else _ghost.Hide();
        }

        /// <summary>The target's visible renderers, without labels; a station without its buttons (they light up on their own).</summary>
        private void Collect(Component target)
        {
            target.GetComponentsInChildren(false, _scratch);
            bool station = target is Station;
            foreach (Renderer r in _scratch)
            {
                if (r == null || r.TryGetComponent(out TextMesh _)) continue;
                if (station && r.TryGetComponent(out StationButton _)) continue;
                _renderers.Add(r);
            }
            _scratch.Clear();
        }

        private void Apply()
        {
            for (int i = 0; i < _renderers.Count; i++)
            {
                Renderer r = _renderers[i];
                if (r == null) continue;
                r.GetSharedMaterials(_materials);
                for (int m = 0; m < _materials.Count; m++)
                {
                    // Start from the renderer-wide block (other scripts' tints), then add the glow for this material.
                    r.GetPropertyBlock(_block);
                    Color baseColor = _block.HasColor(BaseColorId) ? _block.GetColor(BaseColorId) : MaterialBase(_materials[m]);
                    Color emission = _block.HasColor(EmissionColorId) ? _block.GetColor(EmissionColorId) : Color.black;
                    Color tinted = Color.Lerp(baseColor, _color, TintAmount) * Brighten;
                    tinted.a = baseColor.a;
                    _block.SetColor(BaseColorId, tinted);
                    _block.SetColor(EmissionColorId, emission + _color * EmissionAmount);
                    r.SetPropertyBlock(_block, m);
                }
            }
            _materials.Clear();
        }

        private void Clear()
        {
            for (int i = 0; i < _renderers.Count; i++)
            {
                Renderer r = _renderers[i];
                if (r == null) continue;
                r.GetSharedMaterials(_materials);
                for (int m = 0; m < _materials.Count; m++) r.SetPropertyBlock(null, m);
            }
            _materials.Clear();
            _renderers.Clear();
            _target = null;
            ClearOutline();
        }

        private static Color MaterialBase(Material material)
        {
            return material != null && material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : Color.white;
        }

        private Transform CreateMarker()
        {
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "PlaceMarker";
            Destroy(disc.GetComponent<Collider>()); // never in the way of the aim ray
            disc.transform.localScale = new Vector3(MarkerDiameter, 0.002f, MarkerDiameter);
            var renderer = disc.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (_markerMaterial != null) renderer.sharedMaterial = _markerMaterial;
            disc.SetActive(false);
            return disc.transform;
        }

        private void UpdateMarker(bool show)
        {
            if (_marker == null) return;
            if (show)
            {
                Transform surface = _interactor.Target != null ? _interactor.Target.transform : null;
                Quaternion rotation = surface != null ? surface.rotation : Quaternion.identity;
                _marker.SetPositionAndRotation(_interactor.TargetSpotPosition + rotation * Vector3.up * MarkerLift, rotation);
            }
            if (_marker.gameObject.activeSelf != show) _marker.gameObject.SetActive(show);
        }

        private void OnDestroy()
        {
            Clear();
            if (_marker != null) Destroy(_marker.gameObject);
            _ghost?.Destroy();
            if (_outline != null) Destroy(_outline);
        }
    }
}
