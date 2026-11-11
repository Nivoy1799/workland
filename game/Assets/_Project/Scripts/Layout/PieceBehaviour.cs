using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Net;

namespace Worktown.Game.Layout
{
    /// <summary>
    /// Spec 062a: the root of every piece prefab (a station with its body, or a worktop, a front counter run, a counter-line module). The
    /// server (LayoutSpawner) sets <see cref="PieceId"/>, the pose (<see cref="PosePacked"/>: PosePacking) and the piece's number before
    /// it spawns it, so a late joiner gets them in the spawn payload. Both sides then apply them (<see cref="Apply"/>): the pose, a counter's
    /// length (its colliders, tops and cabinet modules), the number in the names ("Stove #" becomes "Stove 2"), and the place surfaces' ids
    /// (SurfaceId.Of(piece, local), local = the order in <see cref="Surfaces"/>); then the piece registers in the LayoutRegistry. Holds no
    /// rules. Offline (tests, the Editor) LayoutSpawner configures and registers it without the network.
    /// </summary>
    public sealed class PieceBehaviour : NetworkBehaviour
    {
        /// <summary>A front counter (or worktop) shorter than this has no top (spec 039: a gap under 0.1 m gets no counter top).</summary>
        public const float MinTopLength = 0.1f;
        /// <summary>The number's place in a prefab's names, its station's name and its sign.</summary>
        public const char NumberMark = '#';

        [SerializeField] private string _typeId = string.Empty;
        [Tooltip("The piece's place surfaces; index = the local number of the surface id (the builder's order).")]
        [SerializeField] private PlaceSurface[] _surfaces = new PlaceSurface[0];
        [Tooltip("The piece's station (at most one: its id is the piece id).")]
        [SerializeField] private Station[] _stations = new Station[0];

        [Header("Counters of variable length")]
        [SerializeField] private BoxCollider[] _lengthBoxes = new BoxCollider[0];
        [SerializeField] private PlaceSurface[] _lengthSurfaces = new PlaceSurface[0];
        [SerializeField] private Transform[] _lengthScaled = new Transform[0];
        [SerializeField] private GameObject _moduleArt;
        [SerializeField] private Transform _moduleParent;
        [SerializeField] private string _moduleName = "Module";
        [SerializeField] private float _moduleHeight;
        [SerializeField] private float _moduleArtHeight = 1f;
        [SerializeField] private float _moduleArtWidth = 0.6f;
        [SerializeField] private float _moduleDepthScale = 1f;

        private readonly SyncVar<int> _pieceId = new SyncVar<int>();
        private readonly SyncVar<long> _posePacked = new SyncVar<long>();
        private readonly SyncVar<byte> _number = new SyncVar<byte>();

        private readonly List<GameObject> _modules = new List<GameObject>();
        private List<(Transform Target, string Name)> _markedNames;
        private List<(TextMesh Target, string Text)> _markedTexts;
        private List<(Station Target, string Name)> _markedStations;
        private int _appliedLength = -1;
        private bool _registered;

        public string TypeId => _typeId;
        public int PieceId => _pieceId.Value;
        public long PosePacked => _posePacked.Value;
        /// <summary>The number in its names when its type is placed more than once (0: none).</summary>
        public int Number => _number.Value;
        /// <summary>The place surfaces in local order (SurfaceId's local number).</summary>
        public IReadOnlyList<PlaceSurface> Surfaces => _surfaces;
        public IReadOnlyList<Station> Stations => _stations;
        /// <summary>Its station, or null (a worktop or a front counter has none).</summary>
        public Station Station => _stations != null && _stations.Length > 0 ? _stations[0] : null;
        public bool HasLength => _lengthBoxes != null && _lengthBoxes.Length > 0;

        public int XMm { get { PosePacking.Unpack(PosePacked, out int x, out _, out _, out _); return x; } }
        public int ZMm { get { PosePacking.Unpack(PosePacked, out _, out int z, out _, out _); return z; } }
        public int Turns { get { PosePacking.Unpack(PosePacked, out _, out _, out int t, out _); return t; } }
        public int LengthMm { get { PosePacking.Unpack(PosePacked, out _, out _, out _, out int l); return l; } }

        /// <summary>Server before the spawn, or offline: the piece's id, pose and number; applies them at once.</summary>
        public void Configure(int pieceId, long posePacked, int number)
        {
            _pieceId.Value = pieceId;
            _posePacked.Value = posePacked;
            _number.Value = (byte)Mathf.Clamp(number, 0, 255);
            Apply();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            Apply();
            Register();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Apply();
            Register();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            if (!IsClientStarted) Unregister();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (!IsServerStarted) Unregister();
        }

        private void OnDestroy() => Unregister();

        /// <summary>
        /// Offline (tests): gives the piece <paramref name="pieceId"/> and its surfaces their ids without touching its pose (a surface test
        /// keeps its own transform). No network, no registration.
        /// </summary>
        public void SetIdOffline(int pieceId)
        {
            _pieceId.Value = pieceId;
            for (int i = 0; i < _surfaces.Length; i++)
                if (_surfaces[i] != null) _surfaces[i].AssignId(SurfaceId.Of(pieceId, i), pieceId);
        }

        /// <summary>Offline (tests, the Editor): registers without the network.</summary>
        public void RegisterOffline() => Register();

        private void Register()
        {
            if (PieceId <= 0) return;
            _registered = true;
            LayoutRegistry.Register(this);
        }

        private void Unregister()
        {
            if (!_registered) return;
            _registered = false;
            LayoutRegistry.Unregister(this);
        }

        /// <summary>Applies the synced values: the pose, the length, the number and the surface ids. Safe to call again.</summary>
        public void Apply()
        {
            PosePacking.Unpack(PosePacked, out int x, out int z, out int turns, out int length);
            transform.SetPositionAndRotation(new Vector3(x / 1000f, transform.position.y, z / 1000f), Quaternion.Euler(0f, PosePacking.Yaw(turns), 0f));
            if (HasLength && length > 0) ApplyLength(length);
            ApplyNumber(Number);
            int id = PieceId;
            for (int i = 0; i < _surfaces.Length; i++)
                if (_surfaces[i] != null) _surfaces[i].AssignId(id > 0 ? SurfaceId.Of(id, i) : SurfaceId.None, id > 0 ? id : 0);
        }

        /// <summary>A counter's colliders, tops and scaled parts take the length; its cabinet modules are laid out again (ArtSizeTable).</summary>
        private void ApplyLength(int lengthMm)
        {
            if (lengthMm == _appliedLength) return;
            _appliedLength = lengthMm;
            float length = lengthMm / 1000f;
            foreach (BoxCollider box in _lengthBoxes)
                if (box != null) box.size = new Vector3(length, box.size.y, box.size.z);
            for (int i = 0; i < _lengthSurfaces.Length; i++)
            {
                PlaceSurface surface = _lengthSurfaces[i];
                if (surface == null) continue;
                surface.SetWidth(length);
                if (length >= MinTopLength - 0.0005f) continue;
                // Spec 039: a gap narrower than 0.1 m gets no counter top (PlaceCoverage.Rim covers it); the piece has no surface then.
                surface.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(surface.gameObject);
                else DestroyImmediate(surface.gameObject);
                _lengthSurfaces[i] = null;
            }
            foreach (Transform t in _lengthScaled)
                if (t != null) t.localScale = new Vector3(length, t.localScale.y, t.localScale.z);
            LayModules(length);
        }

        private void LayModules(float length)
        {
            foreach (GameObject module in _modules)
            {
                if (module == null) continue;
                if (Application.isPlaying) Destroy(module);
                else DestroyImmediate(module);
            }
            _modules.Clear();
            if (_moduleArt == null || _moduleParent == null) return;
            (int count, float w) = Art.ArtSizeTable.WorktopModules(length);
            float at = -length * 0.5f;
            for (int i = 0; i < count; i++)
            {
                GameObject module = InstantiateArt(_moduleArt, _moduleParent);
                module.name = _moduleName + " " + (i + 1);
                module.transform.localPosition = new Vector3(at + w * 0.5f, 0f, 0f);
                module.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // spec 027a: the cabinet doors face the cook
                module.transform.localScale = new Vector3(w / _moduleArtWidth, _moduleHeight / Mathf.Max(0.0001f, _moduleArtHeight), _moduleDepthScale);
                _modules.Add(module);
                at += w;
            }
        }

        /// <summary>
        /// An instance of <paramref name="prefab"/> under <paramref name="parent"/>; in the Editor outside play mode a prefab instance (the
        /// art keeps its link to the catalog's wrapper, as the builder's modules did), else a plain copy.
        /// </summary>
        public static GameObject InstantiateArt(GameObject prefab, Transform parent)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && UnityEditor.PrefabUtility.IsPartOfPrefabAsset(prefab))
            {
                var linked = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
                if (linked != null) return linked;
            }
#endif
            return parent != null ? Instantiate(prefab, parent, false) : Instantiate(prefab);
        }

        /// <summary>
        /// Offline: an instance of a piece (or item) prefab; in the Editor outside play mode unpacked from its own prefab (so its length can
        /// change it) with the art inside still linked to the catalog's wrappers.
        /// </summary>
        public static GameObject InstantiateOffline(GameObject prefab)
        {
            GameObject go = InstantiateArt(prefab, null);
#if UNITY_EDITOR
            if (!Application.isPlaying && UnityEditor.PrefabUtility.IsPartOfPrefabInstance(go))
                UnityEditor.PrefabUtility.UnpackPrefabInstance(go, UnityEditor.PrefabUnpackMode.OutermostRoot, UnityEditor.InteractionMode.AutomatedAction);
#endif
            return go;
        }

        /// <summary>"Stove #" becomes "Stove 2" (number 2) or "Stove" (0): object names, its station's name and its texts.</summary>
        private void ApplyNumber(int number)
        {
            if (_markedNames == null)
            {
                _markedNames = new List<(Transform, string)>();
                _markedTexts = new List<(TextMesh, string)>();
                _markedStations = new List<(Station, string)>();
                foreach (Transform t in GetComponentsInChildren<Transform>(true))
                    if (t.name.IndexOf(NumberMark) >= 0) _markedNames.Add((t, t.name));
                foreach (TextMesh text in GetComponentsInChildren<TextMesh>(true))
                    if (text.text != null && text.text.IndexOf(NumberMark) >= 0) _markedTexts.Add((text, text.text));
                foreach (Station station in GetComponentsInChildren<Station>(true))
                    if (station.DisplayName.IndexOf(NumberMark) >= 0) _markedStations.Add((station, station.DisplayName));
            }
            foreach ((Transform target, string name) in _markedNames)
                if (target != null) target.name = Numbered(name, number);
            foreach ((TextMesh target, string text) in _markedTexts)
                if (target != null) target.text = Numbered(text, number);
            foreach ((Station target, string name) in _markedStations)
                if (target != null) target.SetDisplayName(Numbered(name, number));
        }

        /// <summary>"Stove #" with 2 is "Stove 2"; with 0 "Stove" (the mark and the space before it go).</summary>
        public static string Numbered(string marked, int number)
        {
            if (string.IsNullOrEmpty(marked)) return marked;
            string mark = NumberMark.ToString();
            return number > 0 ? marked.Replace(mark, number.ToString(System.Globalization.CultureInfo.InvariantCulture))
                : marked.Replace(" " + mark, string.Empty).Replace(mark, string.Empty);
        }
    }
}
