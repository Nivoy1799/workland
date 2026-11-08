using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Cooking;

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

        private readonly Dictionary<GameObject, Stack<GameObject>> _pool = new Dictionary<GameObject, Stack<GameObject>>();
        private readonly List<GameObject> _active = new List<GameObject>();
        private readonly List<GameObject> _activePrefabs = new List<GameObject>();
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private Dictionary<ArtKey, GameObject> _prefabs;
        private MaterialPropertyBlock _block;

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
                shown = Add(plated ? ArtKey.FoodPlate : ArtKey.FoodBowl, Vector3.zero, 0f, 1f, Color.white, false);
                float height = plated ? _plateContentHeight : _bowlContentHeight;
                float scale = plated ? _partScale : _bowlPartScale;
                float radius = plated ? _partRadius : _bowlPartRadius;
                int count = PartCount(parts, look.PartCount);
                for (int i = 0; i < count; i++)
                    AddPile(parts[i], FoodArtRules.PartPosition(i, count, radius) + Vector3.up * height, scale);
            }
            ShowFallback(!shown, look);
        }

        /// <summary>A station's contents (plating table, bowl, pan, pot): up to four parts at this anchor.</summary>
        public void ShowParts(IReadOnlyList<FoodLook> parts)
        {
            ReleaseAll();
            int count = PartCount(parts, FoodLook.MaxParts);
            for (int i = 0; i < count; i++)
                AddPile(parts[i], FoodArtRules.PartPosition(i, count, _partRadius), _partScale);
            ShowFallback(false, default);
        }

        private static int PartCount(IReadOnlyList<FoodLook> parts, int limit)
        {
            if (parts == null) return 0;
            return Mathf.Min(parts.Count, Mathf.Min(limit, FoodLook.MaxParts));
        }

        private bool AddPile(FoodLook look, Vector3 offset, float scale)
        {
            if (!FoodArtRules.TryGetPiece(look, out FoodPiece piece)) return false;
            if (!TryGetPrefab(piece.Key, out _)) return false;
            Color tint = FoodArtRules.Tint(look, piece.Tinted);
            bool custom = piece.Tinted || tint != Color.white;
            for (int i = 0; i < piece.Count; i++)
            {
                Vector3 position = offset + FoodArtRules.PilePosition(i, piece.Count) * scale;
                Add(piece.Key, position, FoodArtRules.PileYaw(i, piece.Count), scale, tint, custom);
            }
            return true;
        }

        private bool Add(ArtKey key, Vector3 position, float yaw, float scale, Color tint, bool custom)
        {
            if (!TryGetPrefab(key, out GameObject prefab)) return false;
            GameObject instance = Take(prefab);
            Transform t = instance.transform;
            t.localPosition = position;
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);
            t.localScale = Vector3.one * scale;
            Tint(instance, tint, custom);
            return true;
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
        }
    }
}
