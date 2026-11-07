using System.Collections.Generic;
using UnityEngine;
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
    /// </summary>
    public sealed class InteractionHighlight : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        public static readonly Color TakeColor = new Color(1f, 1f, 1f);
        public static readonly Color UseColor = new Color(0.35f, 1f, 0.45f);
        public static readonly Color RefuseColor = new Color(0.55f, 0.55f, 0.55f);
        private const float TintAmount = 0.35f;
        private const float Brighten = 1.15f;
        private const float EmissionAmount = 0.35f;
        private const float MarkerDiameter = 0.22f;
        private const float MarkerLift = 0.006f;

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

        public void Activate(PickupInteractor interactor)
        {
            if (_interactor != null || interactor == null) return;
            _interactor = interactor;
            _block = new MaterialPropertyBlock();
            _marker = CreateMarker();
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
                }
            }
            Apply();
            UpdateMarker(active && action == InteractionAction.Place && _interactor.TargetSpot >= 0);
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
        }
    }
}
