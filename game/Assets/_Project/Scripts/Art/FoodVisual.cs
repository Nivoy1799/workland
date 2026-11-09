using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Player;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 017: client-side model of a food (or of a station's contents) built from the art catalog for a
    /// <see cref="FoodLook"/>. Display only, no game rules. Rebuilt only when the caller passes a new look;
    /// model instances are pooled per prefab, so a rebuild does not allocate once the pool is warm.
    /// Unknown looks or missing models show the fallback renderer (the coloured cube), never an error.
    /// </summary>
    public sealed class FoodVisual : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private ArtCatalog _catalog;
        [Tooltip("Shown when no model fits (unknown look or missing model): the spec 007 coloured cube. Optional.")]
        [SerializeField] private Renderer _fallback;
        [Tooltip("Scale of one part on a plate, in a bowl or on a station.")]
        [SerializeField] private float _partScale = 0.5f;
        [Tooltip("Distance of the parts from the centre when there is more than one.")]
        [SerializeField] private float _partRadius = 0.08f;
        [Tooltip("Spec 026: scale and distance of the parts in a bowl dish (the bowl is smaller than the plate).")]
        [SerializeField] private float _bowlPartScale = 0.45f;
        [SerializeField] private float _bowlPartRadius = 0.04f;
        [Tooltip("Height the parts sit at on a plate / in a bowl (set by the scene builder from the model).")]
        [SerializeField] private float _plateContentHeight = 0.015f;
        [SerializeField] private float _bowlContentHeight = 0.03f;
        [Tooltip("Spec 029: the fryer basket's contents: fries are loose sticks there, not the box.")]
        [SerializeField] private bool _loose;

        private readonly Dictionary<GameObject, Stack<GameObject>> _pool = new Dictionary<GameObject, Stack<GameObject>>();
        private readonly List<GameObject> _active = new List<GameObject>();
        private readonly List<GameObject> _activePrefabs = new List<GameObject>();
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private Dictionary<ArtKey, GameObject> _prefabs;
        private MaterialPropertyBlock _block;
        // Spec 027b: parts that just arrived drop in from above (every viewer).
        private int _shownParts = -1;
        private readonly List<Transform> _dropping = new List<Transform>();
        private readonly List<Vector3> _dropRest = new List<Vector3>();
        private float _dropStart;
        // Spec 027c: what each active instance shows (for the colour shift on the heat) and the current heat.
        private readonly List<PieceTint> _tints = new List<PieceTint>();
        private HeatZone _heatZone = HeatZone.None;
        private float _heatProgress;
        private bool _heatBoiled;

        private struct PieceTint
        {
            public FoodLook Look;
            public bool Tinted;
            public bool Food;
        }

        /// <summary>
        /// Spec 027c: the food shown here is on the heat (a pan's or pot's contents): every piece shifts colour with
        /// <paramref name="progress01"/> and <paramref name="zone"/> (<see cref="FoodArtRules.HeatTint"/>); None returns to
        /// the bucket colours. Only re-tints, nothing is rebuilt.
        /// </summary>
        public void SetHeat(HeatZone zone, float progress01, bool boiled)
        {
            if (zone == _heatZone && Mathf.Approximately(progress01, _heatProgress) && boiled == _heatBoiled) return;
            _heatZone = zone;
            _heatProgress = progress01;
            _heatBoiled = boiled;
            for (int i = 0; i < _active.Count; i++)
                if (_active[i] != null && _tints[i].Food) ApplyTint(_active[i], _tints[i]);
        }

        /// <summary>A food object: a single pile, or a plate / bowl with up to four parts.</summary>
        public void ShowFood(FoodLook look, IReadOnlyList<FoodLook> parts)
        {
            ReleaseAll();
            bool shown;
            if (look.Kind == FoodLook.KindSingle)
            {
                shown = AddPile(look, Vector3.zero, 1f);
            }
            else
            {
                bool plated = look.Kind == FoodLook.KindPlated;
                shown = Add(plated ? ArtKey.FoodPlate : ArtKey.FoodBowl, Vector3.zero, 0f, 1f, default, false, false);
                float height = plated ? _plateContentHeight : _bowlContentHeight;
                float scale = plated ? _partScale : _bowlPartScale;
                float radius = plated ? _partRadius : _bowlPartRadius;
                int count = PartCount(parts, look.PartCount);
                for (int i = 0; i < count; i++)
                    AddPile(parts[i], FoodArtRules.PartPosition(i, count, radius) + Vector3.up * height, scale);
            }
            ShowFallback(!shown, look);
        }

        /// <summary>
        /// A station's contents (plating table, bowl, pan, pot): up to four parts at this anchor. Spec 027b: when there are
        /// more parts than shown before, the new ones drop in from <see cref="FeelSettings.DropHeight"/> above (ease-in over
        /// <see cref="FeelSettings.DropSeconds"/>), for every viewer; the first contents after a (re)join just appear.
        /// </summary>
        public void ShowParts(IReadOnlyList<FoodLook> parts)
        {
            ReleaseAll();
            int count = PartCount(parts, FoodLook.MaxParts);
            int firstNew = -1;
            for (int i = 0; i < count; i++)
            {
                if (_shownParts >= 0 && i == _shownParts) firstNew = _active.Count;
                AddPile(parts[i], FoodArtRules.PartPosition(i, count, _partRadius), _partScale);
            }
            _shownParts = count;
            if (firstNew >= 0) StartDrop(firstNew);
            ShowFallback(false, default);
        }

        private void StartDrop(int firstInstance)
        {
            FeelSettings feel = FeelSettings.Current;
            if (!(feel.DropSeconds > 0f) || feel.DropHeight <= 0f) return;
            Vector3 lift = transform.InverseTransformVector(Vector3.up * feel.DropHeight);
            _dropStart = Time.unscaledTime;
            for (int i = firstInstance; i < _active.Count; i++)
            {
                Transform t = _active[i].transform;
                _dropping.Add(t);
                _dropRest.Add(t.localPosition);
                t.localPosition += lift;
            }
        }

        private void Update()
        {
            if (_dropping.Count == 0) return;
            FeelSettings feel = FeelSettings.Current;
            float t = feel.DropSeconds > 0f ? (Time.unscaledTime - _dropStart) / feel.DropSeconds : 1f;
            Vector3 lift = transform.InverseTransformVector(Vector3.up * feel.DropHeight);
            float left = 1f - FeelRules.EaseIn(t);
            for (int i = 0; i < _dropping.Count; i++)
                if (_dropping[i] != null) _dropping[i].localPosition = _dropRest[i] + lift * (t >= 1f ? 0f : left);
            if (t >= 1f) StopDrop();
        }

        private void StopDrop()
        {
            for (int i = 0; i < _dropping.Count; i++)
                if (_dropping[i] != null) _dropping[i].localPosition = _dropRest[i];
            _dropping.Clear();
            _dropRest.Clear();
        }

        private static int PartCount(IReadOnlyList<FoodLook> parts, int limit)
        {
            if (parts == null) return 0;
            return Mathf.Min(parts.Count, Mathf.Min(limit, FoodLook.MaxParts));
        }

        private bool AddPile(FoodLook look, Vector3 offset, float scale)
        {
            if (!FoodArtRules.TryGetPiece(look, _loose, out FoodPiece piece)) return false;
            if (!TryGetPrefab(piece.Key, out _)) return false;
            // Spec 029: a part that stands on its own (the fries box) keeps more of its size on a plate.
            if (scale < 1f) scale = Mathf.Min(1f, scale * FoodArtRules.PartBoost(piece.Key));
            for (int i = 0; i < piece.Count; i++)
            {
                Vector3 position = offset + FoodArtRules.PilePosition(i, piece.Count) * scale;
                Add(piece.Key, position, FoodArtRules.PileYaw(i, piece.Count), scale, look, piece.Tinted, true);
            }
            return true;
        }

        private bool Add(ArtKey key, Vector3 position, float yaw, float scale, FoodLook look, bool tinted, bool food)
        {
            if (!TryGetPrefab(key, out GameObject prefab)) return false;
            GameObject instance = Take(prefab);
            Transform t = instance.transform;
            t.localPosition = position;
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);
            t.localScale = Vector3.one * scale;
            var info = new PieceTint { Look = look, Tinted = tinted, Food = food };
            _tints.Add(info);
            ApplyTint(instance, info);
            return true;
        }

        /// <summary>A food piece: the bucket colour, or (spec 027c) the heat colour; anything else its own colours.</summary>
        private void ApplyTint(GameObject instance, PieceTint info)
        {
            if (!info.Food)
            {
                Tint(instance, Color.white, false);
                return;
            }
            Color tint = FoodArtRules.HeatTint(info.Look, info.Tinted, _heatZone, _heatProgress, _heatBoiled);
            Tint(instance, tint, info.Tinted || tint != Color.white);
        }

        private void Tint(GameObject instance, Color tint, bool custom)
        {
            instance.GetComponentsInChildren(true, _renderers);
            if (custom)
            {
                _block ??= new MaterialPropertyBlock();
                _block.Clear();
                _block.SetColor(BaseColorId, tint);
            }
            foreach (Renderer r in _renderers) r.SetPropertyBlock(custom ? _block : null);
            _renderers.Clear();
        }

        private void ShowFallback(bool show, FoodLook look)
        {
            if (_fallback == null) return;
            _fallback.enabled = show;
            if (!show) return;
            _block ??= new MaterialPropertyBlock();
            _block.Clear();
            _block.SetColor(BaseColorId, FoodArtRules.IngredientColor(look.Ingredient));
            _fallback.SetPropertyBlock(_block);
        }

        private bool TryGetPrefab(ArtKey key, out GameObject prefab)
        {
            if (_prefabs == null)
            {
                // Built once; ArtCatalog.TryGet allocates a pair list per call.
                _prefabs = new Dictionary<ArtKey, GameObject>();
                if (_catalog != null)
                    foreach (ArtCatalogEntry e in _catalog.Entries)
                        if (e.Prefab != null && !_prefabs.ContainsKey(e.Key)) _prefabs.Add(e.Key, e.Prefab);
            }
            return _prefabs.TryGetValue(key, out prefab);
        }

        private GameObject Take(GameObject prefab)
        {
            GameObject instance;
            if (_pool.TryGetValue(prefab, out Stack<GameObject> free) && free.Count > 0)
            {
                instance = free.Pop();
                instance.SetActive(true);
            }
            else
            {
                instance = Instantiate(prefab, transform, false);
            }
            _active.Add(instance);
            _activePrefabs.Add(prefab);
            return instance;
        }

        private void ReleaseAll()
        {
            StopDrop();
            for (int i = 0; i < _active.Count; i++)
            {
                GameObject instance = _active[i];
                if (instance == null) continue;
                instance.SetActive(false);
                GameObject prefab = _activePrefabs[i];
                if (!_pool.TryGetValue(prefab, out Stack<GameObject> free))
                {
                    free = new Stack<GameObject>();
                    _pool.Add(prefab, free);
                }
                free.Push(instance);
            }
            _active.Clear();
            _activePrefabs.Clear();
            _tints.Clear();
        }
    }
}
