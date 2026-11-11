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
        // Spec 031b: grill marks on grilled food, except on a griddle spot (GriddleSpotView draws its own there).
        private bool _noGrillMarks;
        private Vector3 _barSize;

        private void Awake()
        {
            _noGrillMarks = GetComponentInParent<GriddleSpot>() != null;
        }

        private struct PieceTint
        {
            public FoodLook Look;
            public bool Tinted;
            public bool Food;
            /// <summary>Spec 031: a fixed palette colour (the wrap paper), used when <see cref="HasFixed"/>.</summary>
            public bool HasFixed;
            public Color Fixed;
        }

        /// <summary>Spec 031: set up a visual made at runtime (the build stack's layers): its catalog and whether fries lie loose.</summary>
        internal void Configure(ArtCatalog catalog, bool loose)
        {
            _catalog = catalog;
            _loose = loose;
            _prefabs = null;
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
            bool wasCup = _cupShown;
            byte wasKind = _cupKind;
            ReleaseAll();
            bool shown;
            if (FoodLookRules.IsCup(look))
            {
                // Spec 033: a cup (the Kenney cup) with its liquid at the level, the fill line and (lidded) its lid. The lid's click
                // plays for everyone but the player who predicted it (it heard it at its click).
                shown = AddCup(look);
                if (wasCup && wasKind == FoodLook.KindCup && look.Kind == FoodLook.KindLidded && !_lidPredicted) PlayHere(Audio.SoundKey.LidClick);
                if (look.Kind == FoodLook.KindLidded) _lidPredicted = false; // confirmed (a level change keeps a pending lid)
                ShowFallback(!shown, look);
                return;
            }
            _predictedMl = -1f;
            _lidPredicted = false;
            if (look.Kind == FoodLook.KindSingle)
            {
                shown = AddPile(look, Vector3.zero, 1f);
            }
            else if (FoodLookRules.IsBuild(look))
            {
                shown = AddBuild(look, parts, Vector3.zero, 1f); // spec 031: a build in the hands
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
        /// <summary>Spec 044: the model drawn last (the last part of the contents), or null when nothing is drawn.</summary>
        public Transform LastPart
        {
            get
            {
                for (int i = _active.Count - 1; i >= 0; i--)
                    if (_active[i] != null && _active[i].activeInHierarchy) return _active[i].transform;
                return null;
            }
        }

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

        /// <summary>
        /// Spec 031: one layer of a build at this anchor: a bread base or a sauce is a flat disc in its colour, anything else the
        /// ingredient's own pile at <see cref="AssemblyArtRules.PileScale"/>. Drawn for every viewer by Art/BuildStackView.
        /// </summary>
        public void ShowLayer(FoodLook look)
        {
            ReleaseAll();
            if (AssemblyArtRules.IsFlat(look))
            {
                float disc = AssemblyArtRules.IsSauce(look.Ingredient) ? AssemblyArtRules.SauceScale : 1f;
                Add(ArtKey.FoodLayerDisc, Vector3.zero, 0f, disc, look, true, true);
            }
            else AddPile(look, Vector3.zero, AssemblyArtRules.PileScale);
            ShowFallback(false, default);
        }

        /// <summary>Spec 031: nothing drawn here (an empty layer slot).</summary>
        public void Clear()
        {
            ReleaseAll();
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
            // Spec 033: an unconfirmed level of the owner's last pour does not stay for ever.
            if (_predictedMl >= 0f && !_pouring && Time.unscaledTime > _predictionUntil && DrinkRules.LevelOfMl(_predictedMl) != _cupLevel)
                EndPrediction();
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
            if (FoodLookRules.IsBuild(look)) return AddBuild(look, null, offset, scale); // spec 031: a build on a plate
            if (!FoodArtRules.TryGetPiece(look, _loose, out FoodPiece piece)) return false;
            if (!TryGetPrefab(piece.Key, out _)) return false;
            // Spec 029: a part that stands on its own (the fries box) keeps more of its size on a plate.
            if (scale < 1f) scale = Mathf.Min(1f, scale * FoodArtRules.PartBoost(piece.Key));
            bool marks = !_noGrillMarks && FoodArtRules.HasGrillMarks(look);
            for (int i = 0; i < piece.Count; i++)
            {
                Vector3 position = offset + FoodArtRules.PilePosition(i, piece.Count) * scale;
                Add(piece.Key, position, FoodArtRules.PileYaw(i, piece.Count), scale, look, piece.Tinted, true);
                if (marks && i < FoodArtRules.MarkedPieces) AddGrillMarks(_active[_active.Count - 1]);
            }
            return true;
        }

        /// <summary>
        /// Spec 031b: grill marks on a grilled piece (in the hands, on a plate, in a build): dark Charcoal bars across its top, as
        /// the griddle spot draws them (GriddleSpotView). Pooled like every piece.
        /// </summary>
        private void AddGrillMarks(GameObject piece)
        {
            if (piece == null || !TryGetPrefab(ArtKey.FoodPieceStick, out GameObject bar)) return;
            piece.GetComponentsInChildren(false, _renderers);
            bool any = false;
            Bounds b = default;
            foreach (Renderer r in _renderers)
            {
                if (!any) b = r.bounds;
                else b.Encapsulate(r.bounds);
                any = true;
            }
            _renderers.Clear();
            if (!any) return;
            float unit = Mathf.Max(1e-4f, transform.lossyScale.x);
            Vector3 top = transform.InverseTransformPoint(new Vector3(b.center.x, b.max.y, b.center.z));
            float length = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) / unit, 0.01f, 0.25f);
            Vector3 barSize = BarSize(bar);
            var scale = new Vector3(length * 0.12f / barSize.x, length * 0.05f / barSize.y, length * 0.75f / barSize.z);
            for (int k = 0; k < FoodArtRules.MarksPerPiece; k++)
            {
                float across = (k - (FoodArtRules.MarksPerPiece - 1) * 0.5f) * length * 0.3f;
                AddFixed(ArtKey.FoodPieceStick, top + new Vector3(across, length * 0.01f, 0f), scale, Palette.Charcoal);
            }
        }

        /// <summary>The bar prefab's own size (m, its renderers at scale 1), measured once.</summary>
        private Vector3 BarSize(GameObject prefab)
        {
            if (_barSize.x > 0f) return _barSize;
            Vector3 size = Vector3.one * 0.03f;
            foreach (MeshFilter f in prefab.GetComponentsInChildren<MeshFilter>(true))
                if (f.sharedMesh != null)
                {
                    size = Vector3.Scale(f.sharedMesh.bounds.size, f.transform.lossyScale);
                    break;
                }
            _barSize = new Vector3(Mathf.Max(size.x, 1e-4f), Mathf.Max(size.y, 1e-4f), Mathf.Max(size.z, 1e-4f));
            return _barSize;
        }

        /// <summary>
        /// Spec 031: a build. Open: its first layers stacked (a bread or sauce layer is a flat disc in its colour, the rest the
        /// ingredient's own pile, each on top of the one before); folded: the folded wrap in the bread's colour; in paper: the paper
        /// (Cream); boxed and bagged: the Kenney box and bag.
        /// </summary>
        private bool AddBuild(FoodLook look, IReadOnlyList<FoodLook> parts, Vector3 offset, float scale)
        {
            switch (look.Kind)
            {
                case FoodLook.KindFolded: return Add(ArtKey.FoodWrapFolded, offset, 0f, scale, BreadLook(look), true, true);
                case FoodLook.KindWrapped:
                    // The folded wrap with the paper round two thirds of it: its end peeks out, so it reads as a wrap in paper.
                    bool wrap = Add(ArtKey.FoodWrapFolded, offset, 0f, scale, BreadLook(look), true, true);
                    return AddFixed(ArtKey.FoodWrapPaper, offset + new Vector3(0.04f, -0.004f, 0f) * scale,
                        new Vector3(0.7f, 1.12f, 1.12f) * scale, Palette.Cream) || wrap;
                case FoodLook.KindBoxed: return Add(ArtKey.PropTakeawayBox, offset, 0f, scale, default, false, false);
                case FoodLook.KindBagged: return Add(ArtKey.PropPaperBag, offset, 0f, scale, default, false, false);
            }
            int count = parts != null ? Mathf.Min(parts.Count, FoodLook.MaxParts) : 0;
            if (count == 0) return Add(ArtKey.FoodLayerDisc, offset, 0f, scale, BreadLook(look), true, true);
            float height = 0f;
            bool any = false;
            for (int i = 0; i < count; i++)
            {
                FoodLook part = parts[i];
                Vector2 shift = AssemblyArtRules.LayerShift(i, part);
                Vector3 at = offset + new Vector3(shift.x, height, shift.y) * scale;
                if (AssemblyArtRules.IsFlat(part))
                {
                    float disc = AssemblyArtRules.IsSauce(part.Ingredient) ? AssemblyArtRules.SauceScale : 1f;
                    any |= Add(ArtKey.FoodLayerDisc, at, 0f, scale * disc, part, true, true);
                }
                else any |= AddPile(part, at, scale * AssemblyArtRules.PileScale);
                height += AssemblyArtRules.LayerHeight(part);
            }
            return any;
        }

        // ---- Spec 033: a cup ----

        private bool _cupShown;
        private byte _cupKind;
        private byte _cupLevel;
        private Transform _liquid;
        private Transform _lid;
        private Bounds _cupBounds;
        private Vector3 _discSize;
        private float _predictedMl = -1f;
        private bool _pouring;
        private bool _lidPredicted;
        private static Audio.KitchenSounds _sounds;
        private static bool _soundsLooked;

        /// <summary>Spec 033: this visual shows a cup now.</summary>
        public bool ShowsCup => _cupShown;

        /// <summary>Spec 033: the drawn liquid height of the cup (fraction of the cup), for tests and the tour log.</summary>
        public float DrawnFill { get; private set; }

        /// <summary>Spec 033: the lid is drawn (synced or predicted).</summary>
        public bool LidShown => _lid != null && _lid.gameObject.activeSelf;

        private bool AddCup(FoodLook look)
        {
            _cupShown = false;
            _liquid = null;
            _lid = null;
            if (!TryGetPrefab(ArtKey.PropCup, out GameObject cupPrefab) || !Add(ArtKey.PropCup, Vector3.zero, 0f, 1f, default, false, false))
                return false;
            _cupShown = true;
            _cupKind = look.Kind;
            _cupLevel = look.Shape;
            _cupBounds = PrefabBounds(cupPrefab, ref _cupBoundsMeasured, ref _cupBoundsCache);
            Bounds b = _cupBounds;
            int tap = DrinkRules.TapOfDrinkByte(look.Ingredient);
            if (TryGetPrefab(ArtKey.FoodLayerDisc, out GameObject disc))
            {
                _discSize = PrefabBounds(disc, ref _discMeasured, ref _discCache).size;
                // The liquid: a disc in the drink's colour, moved to the level (rebuilt only on a new look; the level moves it). The cup's
                // model carries its fill line.
                AddFixed(ArtKey.FoodLayerDisc, new Vector3(b.center.x, b.min.y, b.center.z), Vector3.one, DrinkArtRules.DrinkColour(tap));
                _liquid = _active[_active.Count - 1].transform;
                // The lid: a Cream disc over the rim (shown when lidded, or from the owner's click).
                float lid = 2f * (DrinkArtRules.CupTopRadius + DrinkArtRules.CupWall) * 1.05f;
                AddFixed(ArtKey.FoodLayerDisc, new Vector3(b.center.x, b.max.y - 0.002f, b.center.z),
                    new Vector3(lid / _discSize.x, 0.012f / _discSize.y, lid / _discSize.z), Palette.Cream);
                _lid = _active[_active.Count - 1].transform;
                _lid.gameObject.SetActive(look.Kind == FoodLook.KindLidded || _lidPredicted);
            }
            // Spec 033: the owner's own pour keeps its exact level while the server's bucket agrees with it.
            if (_predictedMl >= 0f && !_pouring && DrinkRules.LevelOfMl(_predictedMl) != look.Shape) _predictedMl = -1f;
            ApplyLevel();
            return true;
        }

        private bool _cupBoundsMeasured, _discMeasured;
        private Bounds _cupBoundsCache, _discCache;

        private void ApplyLevel()
        {
            if (!_cupShown) return;
            float fill = _predictedMl >= 0f ? DrinkRules.DrawnFill(_predictedMl) : DrinkRules.DrawnFill(_cupLevel);
            DrawnFill = fill;
            if (_liquid == null) return;
            bool on = fill > 0.001f;
            if (_liquid.gameObject.activeSelf != on) _liquid.gameObject.SetActive(on);
            if (!on) return;
            float share = DrinkArtRules.CupLiquidHeight(fill);
            float d = DrinkArtRules.CupInnerDiameter(share) * 0.97f;
            _liquid.localPosition = new Vector3(_cupBounds.center.x, _cupBounds.min.y + _cupBounds.size.y * share - 0.004f, _cupBounds.center.z);
            _liquid.localScale = new Vector3(d / Mathf.Max(1e-4f, _discSize.x), 0.004f / Mathf.Max(1e-4f, _discSize.y), d / Mathf.Max(1e-4f, _discSize.z));
        }

        /// <summary>
        /// Spec 033, the pouring player only: its cup holds <paramref name="ml"/> now (its own prediction from the press, docs/feel.md 1).
        /// The level moves without a rebuild. <paramref name="pouring"/> false: the pour ended; the level stays while the server's bucket
        /// agrees with it, else it goes back to the bucket's.
        /// </summary>
        public void PredictMl(float ml, bool pouring)
        {
            _predictedMl = Mathf.Max(0f, ml);
            _pouring = pouring;
            _predictionUntil = Time.unscaledTime + PredictionKeepSeconds;
            ApplyLevel();
        }

        /// <summary>Spec 033: a pour's level that the server's bucket never confirmed goes after this long (a lost answer).</summary>
        public const float PredictionKeepSeconds = 3f;
        private float _predictionUntil;

        /// <summary>Spec 033: the owner's predicted ml in this cup (-1: none).</summary>
        public float PredictedMl => _predictedMl;

        /// <summary>Spec 033: the level the owner's prediction shows (LevelEmpty without one).</summary>
        public byte PredictedLevel => _predictedMl >= 0f ? DrinkRules.LevelOfMl(_predictedMl) : DrinkRules.LevelEmpty;

        /// <summary>Spec 033: the tap the owner's prediction pours from (-1: none).</summary>
        public int PredictedTap => _predictedMl >= 0f ? _predictedTap : -1;

        private int _predictedTap = -1;

        /// <summary>Spec 033: the owner pours from tap <paramref name="tap"/> into this cup (the rules see its drink before the server's look).</summary>
        public void SetPredictedTap(int tap)
        {
            _predictedTap = tap;
            if (_liquid != null && DrinkRules.IsTap(tap)) Tint(_liquid.gameObject, DrinkArtRules.DrinkColour(tap), true); // an empty cup fills
        }

        /// <summary>Spec 033: the owner's pour was refused: back to the synced level.</summary>
        public void EndPrediction()
        {
            _predictedMl = -1f;
            _pouring = false;
            ApplyLevel();
        }

        /// <summary>Spec 033, the owner's lid click: the lid shows now (<paramref name="on"/>), or goes again on a refusal.</summary>
        public void PredictLid(bool on)
        {
            _lidPredicted = on;
            if (_lid != null) _lid.gameObject.SetActive(on || _cupKind == FoodLook.KindLidded);
        }

        /// <summary>The model bounds of a prefab in its root's space (renderers' meshes), measured once.</summary>
        private static Bounds PrefabBounds(GameObject prefab, ref bool measured, ref Bounds cache)
        {
            if (measured) return cache;
            measured = true;
            bool any = false;
            Bounds result = new Bounds(Vector3.zero, Vector3.one * 0.1f);
            foreach (MeshFilter f in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null) continue;
                Bounds m = f.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = m.center + Vector3.Scale(m.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    Vector3 p = prefab.transform.InverseTransformPoint(f.transform.TransformPoint(corner));
                    if (!any)
                    {
                        result = new Bounds(p, Vector3.zero);
                        any = true;
                    }
                    else result.Encapsulate(p);
                }
            }
            cache = result;
            return cache;
        }

        private void PlayHere(Audio.SoundKey key)
        {
            if (!_soundsLooked)
            {
                _soundsLooked = true;
                _sounds = FindFirstObjectByType<Audio.KitchenSounds>();
            }
            if (_sounds != null) _sounds.Play(key, transform.position);
        }

        private static FoodLook BreadLook(FoodLook build) => new FoodLook(build.Ingredient, FoodLook.ShapeWhole, 0, 0, FoodLook.KindSingle, 0);

        private bool AddFixed(ArtKey key, Vector3 position, Vector3 scale, Color colour)
        {
            if (!TryGetPrefab(key, out GameObject prefab)) return false;
            GameObject instance = Take(prefab);
            Transform t = instance.transform;
            t.localPosition = position;
            t.localRotation = Quaternion.identity;
            t.localScale = scale;
            var info = new PieceTint { HasFixed = true, Fixed = colour };
            _tints.Add(info);
            ApplyTint(instance, info);
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
            if (info.HasFixed)
            {
                Tint(instance, info.Fixed, true); // spec 031: the wrap paper
                return;
            }
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
            _cupShown = false; // spec 033
            _liquid = null;
            _lid = null;
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
