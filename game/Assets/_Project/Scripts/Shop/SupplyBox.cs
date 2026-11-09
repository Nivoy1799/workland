using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using Worktown.Game.Audio;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 034: a delivery box (a Kenney cardboard box, carried like any pickup with the Pickup component on the same object). The
    /// server's StockRoom owns what is in it; the one synced ushort <see cref="Contents"/> (BoxContents.Pack: the line and the units)
    /// only shows it: the label, the prompt "Click · unpack 6 lettuce", the unpack look. Clients: the delivery knock when a box arrives,
    /// and the predicted unpack (the box tips towards the slot and shakes, <see cref="PlayUnpack"/>). No RPC.
    /// </summary>
    public sealed class SupplyBox : NetworkBehaviour
    {
        public readonly SyncVar<ushort> Contents = new SyncVar<ushort>(0);

        [SerializeField] private TextMesh _label;
        [Tooltip("The model (a child of the pickup's visual): the unpack look turns and shakes it.")]
        [SerializeField] private Transform _model;

        private float _unpackStart = -1f;
        private Quaternion _modelRest = Quaternion.identity;
        private bool _restKnown;
        private ushort _shown = ushort.MaxValue;
        // The owner's whole unpack: the model hides at the click (its copy flies into the slot) until the server empties the box.
        private float _hiddenUntil = -1f;
        private Renderer[] _renderers;
        private static Material _textMaterial;

        /// <summary>What the box holds as clients see it (false while unknown).</summary>
        public bool TryGetContents(out BoxContents box) => BoxContents.TryUnpack(Contents.Value, out box);

        private string _pickupName;
        private ushort _pickupNameOf = ushort.MaxValue;

        /// <summary>"the box of 6 tomato" (built once per contents, not per frame).</summary>
        public string PickupName(BoxContents box)
        {
            if (_pickupNameOf != Contents.Value || _pickupName == null)
            {
                _pickupNameOf = Contents.Value;
                _pickupName = "the box of " + StockCatalog.Describe(box.Line, box.Units);
            }
            return _pickupName;
        }

        /// <summary>The box's model (the unpack look copies it).</summary>
        public GameObject Model => _model != null ? _model.gameObject : null;

        private void Awake()
        {
            Contents.OnChange += (_, _, _) => Refresh();
            _renderers = _model != null ? _model.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ShowModel(true); // a pooled box comes back whole
            _hiddenUntil = -1f;
            // Spec 027d: world text is depth tested (the label never shows through a wall).
            if (_label != null && !Application.isBatchMode)
            {
                if (_textMaterial == null) _textMaterial = Worktown.Game.Art.LabelMaterials.CreateText(_label.font);
                if (_textMaterial != null && _label.TryGetComponent(out MeshRenderer r)) r.sharedMaterial = _textMaterial;
            }
            Refresh();
            // A box that arrives while this client plays is a delivery: the knock and the boxes set down at the back door.
            if (Time.timeSinceLevelLoad > 3f && !IsServerOnlyStarted)
            {
                KitchenSounds sounds = FindFirstObjectByType<KitchenSounds>();
                if (sounds != null) sounds.Play(SoundKey.Delivery, transform.position);
            }
        }

        /// <summary>Server: publishes what is in the box (only a change is sent).</summary>
        internal void ServerSet(BoxContents box)
        {
            ushort packed = box.Pack();
            if (Contents.Value != packed) Contents.Value = packed;
            Refresh();
        }

        /// <summary>
        /// Client, the owner's whole unpack (everything fits): the model hides at the click while its copy flies into the slot
        /// (Art.UnpackLook); it shows again if the server did not empty the box within <paramref name="seconds"/>.
        /// </summary>
        public void HideForUnpack(float seconds)
        {
            _hiddenUntil = Time.unscaledTime + seconds;
            ShowModel(false);
        }

        /// <summary>Client: a refused unpack: the box shows again at once.</summary>
        public void ShowAgain()
        {
            _hiddenUntil = -1f;
            ShowModel(true);
        }

        private void ShowModel(bool on)
        {
            if (_renderers == null) return;
            foreach (Renderer r in _renderers)
                if (r != null) r.enabled = on;
            if (_label != null && _label.TryGetComponent(out MeshRenderer label)) label.enabled = on;
        }

        /// <summary>Client: the unpack look at the click on the box in the hands (part of it fits: the rest stays in the box).</summary>
        public void PlayUnpack()
        {
            if (_model == null) return;
            if (!_restKnown)
            {
                _modelRest = _model.localRotation;
                _restKnown = true;
            }
            _unpackStart = Time.unscaledTime;
        }

        private void Refresh()
        {
            if (_label == null || _shown == Contents.Value) return;
            _shown = Contents.Value;
            _label.text = TryGetContents(out BoxContents box) ? StockCatalog.Describe(box.Line, box.Units) : string.Empty;
        }

        private void LateUpdate()
        {
            if (_hiddenUntil >= 0f && Time.unscaledTime > _hiddenUntil) ShowAgain(); // not emptied after all
            if (_unpackStart >= 0f && _model != null)
            {
                float t = Time.unscaledTime - _unpackStart;
                if (t >= BoxLookRules.UnpackSeconds)
                {
                    _unpackStart = -1f;
                    _model.localRotation = _modelRest;
                }
                else
                {
                    _model.localRotation = _modelRest * Quaternion.Euler(BoxLookRules.UnpackAngle(t), 0f, 0f);
                }
            }
            Camera cam = Camera.main;
            if (_label == null || cam == null) return;
            _label.transform.rotation = Quaternion.LookRotation(_label.transform.position - cam.transform.position);
        }
    }
}
