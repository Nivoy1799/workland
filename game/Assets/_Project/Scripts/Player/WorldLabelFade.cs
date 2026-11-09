using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Cooking;
using Worktown.Game.Net;
using Worktown.Game.Npc;
using Worktown.Game.UI;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 020 and 027d, owner only: the one place that decides how world text shows (<see cref="LabelRules"/>). It finds
    /// every station sign, item and cookware label, order bubble, ticket rail text and stove button caption, gives them a
    /// depth-tested material (the built-in font shader draws through walls) and a Panel-coloured back, and each frame:
    /// hides one-sided text seen from behind, sizes text by distance, shows item labels only for the aimed or held item,
    /// collapses far queue bubbles to an icon, keeps the first-person view centre clear, and finally hides the lower
    /// priority one of any two labels whose screen rectangles overlap (heat rings, spec 027c, keep their place and are
    /// never hidden). Local display only; the order text and every price stay as the server sent them.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class WorldLabelFade : MonoBehaviour
    {
        public const float FirstPersonScale = 0.5f;
        public const float FadeStart = 1.5f;
        public const float FadeEnd = 1.0f;
        /// <summary>Own size of item and bubble text (their prefab fonts are large), as a factor.</summary>
        public const float ItemScale = 0.5f;
        public const float BubbleScale = 0.4f;
        private const float ScanSeconds = 0.5f;
        private const float PadX = 0.07f, PadY = 0.05f; // panel padding in label units
        private const float BackOffset = 0.03f; // label units behind the text
        private const float RectPad = 4f; // pixels around a label's screen rectangle
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private sealed class Entry
        {
            public TextMesh Text;
            public MeshRenderer Renderer;
            public Transform Transform;
            public LabelKind Kind;
            public Vector3 BaseScale;
            public Pickup Pickup;
            public Customer Customer;
            public Transform Back;
            public MeshRenderer BackRenderer;
            public int Rank = -1;
            public float QueueKey; // squared distance to the counter, for the queue order
            public float Distance;
            public bool Hidden; // drawn with zero alpha (overlap): the renderer stays on so its bounds stay current
        }

        private readonly Dictionary<TextMesh, Entry> _entries = new Dictionary<TextMesh, Entry>();
        private readonly List<Entry> _list = new List<Entry>();
        private readonly List<TextMesh> _gone = new List<TextMesh>();
        private readonly List<Entry> _queue = new List<Entry>();
        private readonly List<Entry> _wanted = new List<Entry>();
        private readonly List<Rect> _rects = new List<Rect>();
        private readonly List<int> _priorities = new List<int>();
        private readonly List<float> _distances = new List<float>();
        // Second review: buffers and comparers kept between frames (no allocation per frame).
        private readonly LabelCull _cull = new LabelCull();
        private bool[] _keep = new bool[64];
        private static readonly IComparer<Entry> ByQueueKey = new QueueKeyComparer();
        private float _nextRigLookup;

        private sealed class QueueKeyComparer : IComparer<Entry>
        {
            public int Compare(Entry a, Entry b) => a.QueueKey.CompareTo(b.QueueKey);
        }
        private HeatRingView[] _rings = new HeatRingView[0];
        private PlayerCameraRig _rig;
        private PickupInteractor _interactor;
        private Transform _counter;
        private Transform _backs;
        private Material _textMaterial;
        private Material _panelMaterial;
        private MaterialPropertyBlock _hiddenBlock;
        private float _nextScan;
        private bool _active;

        /// <summary>Alpha of a label <paramref name="distance"/> metres from the camera in first person: 1 from
        /// <see cref="FadeStart"/> on, 0 at <see cref="FadeEnd"/> and closer, linear in between.</summary>
        public static float Alpha(float distance)
        {
            if (float.IsNaN(distance)) return 1f;
            return Mathf.Clamp01((distance - FadeEnd) / (FadeStart - FadeEnd));
        }

        public void Activate()
        {
            if (_active) return;
            _active = true;
            _interactor = GetComponent<PickupInteractor>();
            _nextScan = 0f;
        }

        private void OnDestroy()
        {
            if (_backs != null) Destroy(_backs.gameObject);
            if (_textMaterial != null) Destroy(_textMaterial);
            if (_panelMaterial != null) Destroy(_panelMaterial);
        }

        private void LateUpdate()
        {
            if (!_active) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            if (Time.unscaledTime >= _nextScan) Scan();
            if (_rig == null && Time.unscaledTime >= _nextRigLookup)
            {
                _nextRigLookup = Time.unscaledTime + 1f; // second review: not every frame while there is none
                _rig = FindFirstObjectByType<PlayerCameraRig>();
            }
            bool firstPerson = _rig != null && _rig.IsFirstPerson;
            Transform view = cam.transform;
            Vector3 eye = view.position;
            Component target = _interactor != null ? _interactor.Target : null;

            RankQueue(eye);

            _wanted.Clear();
            _rects.Clear();
            _priorities.Clear();
            _distances.Clear();
            foreach (Entry entry in _list)
            {
                if (entry.Text == null) continue;
                Vector3 to = entry.Transform.position - eye;
                float distance = to.magnitude;
                entry.Distance = distance;
                bool aimed = entry.Pickup != null && target != null && target.transform == entry.Pickup.transform;
                bool held = entry.Pickup != null && entry.Pickup.IsHeld && entry.Pickup.IsOwner;
                float facing = LabelRules.IsOneSided(entry.Kind) && distance > 0.0001f
                    ? Vector3.Dot(-entry.Transform.forward, -to / distance) : 1f;
                float angle = LabelRules.AngleFromCentre(view.forward, to);
                bool visible = LabelRules.IsVisible(entry.Kind, distance, facing, angle, firstPerson, aimed, held);
                float alpha = 1f;
                if (visible && entry.Kind == LabelKind.Sign && firstPerson)
                {
                    alpha = Alpha(distance);
                    if (alpha < 0.5f) visible = false; // close to the camera: gone, the prompt says everything
                }
                if (entry.Renderer.enabled != visible) entry.Renderer.enabled = visible;
                if (!visible)
                {
                    SetBack(entry, false);
                    continue;
                }
                Style(entry, distance, firstPerson);
                if (!ScreenRect(cam, entry.Renderer.bounds, out Rect rect))
                {
                    // Not drawn on screen: nothing to overlap, nothing to see.
                    SetBack(entry, false);
                    continue;
                }
                _wanted.Add(entry);
                _rects.Add(rect);
                _priorities.Add(LabelRules.Priority(entry.Kind, aimed, held, entry.Rank));
                _distances.Add(distance);
            }

            // Heat rings take their place after the aimed target and the held item, before every label.
            int labels = _wanted.Count;
            foreach (HeatRingView ring in _rings)
            {
                if (ring == null || !ring.TryGetScreenRect(out Rect rect)) continue;
                _rects.Add(rect);
                _priorities.Add(LabelRules.PriorityRing);
                _distances.Add(0f);
            }

            if (_keep.Length < _rects.Count) _keep = new bool[Mathf.NextPowerOfTwo(_rects.Count)];
            bool[] keep = _keep;
            _cull.Run(_rects, _priorities, _distances, keep);
            for (int i = 0; i < labels; i++)
            {
                Entry entry = _wanted[i];
                SetHidden(entry, !keep[i]);
                SetBack(entry, keep[i]);
                if (keep[i]) PlaceBack(entry);
            }
        }

        // Finds new labels now and then (items spawn and despawn) and drops the destroyed ones.
        private void Scan()
        {
            _nextScan = Time.unscaledTime + ScanSeconds;
            _rings = FindObjectsByType<HeatRingView>(FindObjectsSortMode.None);
            if (_counter == null)
                foreach (Station station in FindObjectsByType<Station>(FindObjectsSortMode.None))
                    if (station.Kind == StationKind.Counter) _counter = station.transform;

            _gone.Clear();
            foreach (KeyValuePair<TextMesh, Entry> pair in _entries)
                if (pair.Key == null) _gone.Add(pair.Key);
            foreach (TextMesh key in _gone)
            {
                if (_entries[key].Back != null) Destroy(_entries[key].Back.gameObject);
                _entries.Remove(key);
            }

            foreach (TextMesh text in FindObjectsByType<TextMesh>(FindObjectsSortMode.None))
            {
                if (_entries.ContainsKey(text) || !TryClassify(text, out LabelKind kind)) continue;
                var entry = new Entry
                {
                    Text = text,
                    Renderer = text.GetComponent<MeshRenderer>(),
                    Transform = text.transform,
                    Kind = kind,
                    BaseScale = text.transform.localScale,
                    Pickup = text.GetComponentInParent<Pickup>(),
                    Customer = text.GetComponentInParent<Customer>()
                };
                if (entry.Renderer == null) continue;
                if (_textMaterial == null) _textMaterial = LabelMaterials.CreateText(text.font);
                if (_textMaterial != null) entry.Renderer.sharedMaterial = _textMaterial;
                if (kind == LabelKind.Sign || kind == LabelKind.Item || kind == LabelKind.Bubble) MakeBack(entry);
                entry.Renderer.enabled = false;
                _entries.Add(text, entry);
            }
            if (_textMaterial != null && _textMaterial.mainTexture == null)
                foreach (Entry entry in _entries.Values)
                {
                    _textMaterial.mainTexture = entry.Text.font.material.mainTexture;
                    break;
                }
            _list.Clear();
            _list.AddRange(_entries.Values);
        }

        private static bool TryClassify(TextMesh text, out LabelKind kind)
        {
            kind = LabelKind.Sign;
            if (text.TryGetComponent(out StationSign _)) return true;
            if (text.GetComponentInParent<Customer>() != null) { kind = LabelKind.Bubble; return true; }
            if (text.GetComponentInParent<TicketRailView>() != null) { kind = LabelKind.Rail; return true; }
            if (text.GetComponentInParent<Pickup>() != null) { kind = LabelKind.Item; return true; }
            string name = text.name;
            if (name.StartsWith("SwitchLabel") || name.StartsWith("ModeLabel")) { kind = LabelKind.Button; return true; }
            return false; // emote bubbles, ping markers and the like keep their own way
        }

        private void MakeBack(Entry entry)
        {
            if (_panelMaterial == null) _panelMaterial = LabelMaterials.CreatePanel();
            if (_panelMaterial == null) return;
            if (_backs == null) _backs = new GameObject("LabelBacks").transform;
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "LabelBack";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(_backs, false);
            entry.Back = quad.transform;
            entry.BackRenderer = quad.GetComponent<MeshRenderer>();
            entry.BackRenderer.sharedMaterial = _panelMaterial;
            entry.BackRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            entry.BackRenderer.receiveShadows = false;
            quad.SetActive(false);
        }

        // Queue order of the waiting guests: nearest to the counter first.
        private void RankQueue(Vector3 eye)
        {
            _queue.Clear();
            foreach (Entry entry in _list)
            {
                if (entry.Kind != LabelKind.Bubble) continue;
                entry.Rank = -1;
                if (entry.Customer != null && entry.Customer.Waiting) _queue.Add(entry);
            }
            Vector3 head = _counter != null ? _counter.position : eye;
            foreach (Entry entry in _queue) entry.QueueKey = (entry.Transform.position - head).sqrMagnitude;
            _queue.Sort(ByQueueKey);
            for (int i = 0; i < _queue.Count; i++) _queue[i].Rank = i;
        }

        // Size by distance and, for bubbles, the full text or the icon.
        private void Style(Entry entry, float distance, bool firstPerson)
        {
            float kindScale = entry.Kind == LabelKind.Item ? ItemScale : entry.Kind == LabelKind.Bubble ? BubbleScale : 1f;
            if (entry.Kind == LabelKind.Sign && firstPerson) kindScale = FirstPersonScale;
            Vector3 scale = entry.BaseScale * (kindScale * LabelRules.DistanceScale(entry.Kind, distance));
            if (!Mathf.Approximately(scale.x, entry.Transform.localScale.x)) entry.Transform.localScale = scale;
            if (entry.Kind == LabelKind.Bubble && entry.Customer != null)
            {
                string full = entry.Customer.FullText;
                string wanted = LabelRules.ShowsFullText(entry.Rank, entry.Customer.Waiting) ? full : LabelRules.Icon(full);
                if (entry.Text.text != wanted) entry.Text.text = wanted;
            }
            if (entry.Hidden) SetHidden(entry, false);
        }

        private void SetHidden(Entry entry, bool hidden)
        {
            if (entry.Hidden == hidden) return;
            entry.Hidden = hidden;
            if (hidden)
            {
                _hiddenBlock ??= new MaterialPropertyBlock();
                _hiddenBlock.SetColor(ColorId, Palette.WithAlpha(Color.white, 0f));
                entry.Renderer.SetPropertyBlock(_hiddenBlock);
            }
            else entry.Renderer.SetPropertyBlock(null);
        }

        private static void SetBack(Entry entry, bool on)
        {
            if (entry.Back != null && entry.Back.gameObject.activeSelf != on) entry.Back.gameObject.SetActive(on);
        }

        private static void PlaceBack(Entry entry)
        {
            if (entry.Back == null) return;
            Bounds local = entry.Renderer.localBounds;
            if (local.size.x < 0.0001f)
            {
                entry.Back.gameObject.SetActive(false);
                return;
            }
            Transform t = entry.Transform;
            entry.Back.SetPositionAndRotation(t.TransformPoint(new Vector3(local.center.x, local.center.y, BackOffset)), t.rotation);
            entry.Back.localScale = Vector3.Scale(t.lossyScale, new Vector3(local.size.x + PadX * 2f, local.size.y + PadY * 2f, 1f));
        }

        /// <summary>The bounds' corners on screen in pixels (padded); false when any is behind the camera or the box is empty.</summary>
        private static bool ScreenRect(Camera cam, Bounds bounds, out Rect rect)
        {
            rect = default;
            if (bounds.size.sqrMagnitude < 1e-8f) return false;
            Vector3 min = bounds.min, max = bounds.max;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
                Vector3 p = cam.WorldToScreenPoint(corner);
                if (p.z <= 0f) return false;
                x0 = Mathf.Min(x0, p.x);
                y0 = Mathf.Min(y0, p.y);
                x1 = Mathf.Max(x1, p.x);
                y1 = Mathf.Max(y1, p.y);
            }
            rect = Rect.MinMaxRect(x0 - RectPad, y0 - RectPad, x1 + RectPad, y1 + RectPad);
            return true;
        }
    }
}
