using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Net;

namespace Worktown.Game.Layout
{
    /// <summary>
    /// Spec 062a: the spawned layout by id, on the server and on every client (the host has one copy for both): each registered piece
    /// (PieceBehaviour, at its OnStartServer / OnStartClient, or offline), its place surfaces by surface id and its station by the piece id.
    /// Replaces the scene arrays that were indexed by number (PickupService's surfaces, KitchenService's stations). <see cref="Version"/>
    /// counts every change, so a cache built from it (the client's aim lists) knows when to look again. One registry per process: a
    /// process runs one game. Cleared when the game's subsystems reset (play mode without a domain reload).
    /// </summary>
    public static class LayoutRegistry
    {
        private static readonly SortedDictionary<int, PieceBehaviour> PiecesById = new SortedDictionary<int, PieceBehaviour>();
        private static readonly SortedDictionary<int, PlaceSurface> SurfacesById = new SortedDictionary<int, PlaceSurface>();
        private static readonly SortedDictionary<int, Station> StationsById = new SortedDictionary<int, Station>();
        private static readonly List<PlaceSurface> SurfaceList = new List<PlaceSurface>();
        private static readonly List<Station> StationList = new List<Station>();
        private static readonly List<PieceBehaviour> PieceList = new List<PieceBehaviour>();
        private static bool _listsDirty = true;

        /// <summary>Changes with every registration or removal.</summary>
        public static int Version { get; private set; }

        /// <summary>Every registered piece, by ascending id.</summary>
        public static IReadOnlyList<PieceBehaviour> Pieces { get { Refresh(); return PieceList; } }
        /// <summary>Every registered (active) place surface, by ascending surface id.</summary>
        public static IReadOnlyList<PlaceSurface> Surfaces { get { Refresh(); return SurfaceList; } }
        /// <summary>Every registered station, by ascending id.</summary>
        public static IReadOnlyList<Station> Stations { get { Refresh(); return StationList; } }

        public static PieceBehaviour Piece(int id) => PiecesById.TryGetValue(id, out PieceBehaviour p) && p != null ? p : null;

        /// <summary>The place surface with <paramref name="id"/> (SurfaceId), or null.</summary>
        public static PlaceSurface Surface(int id) => SurfacesById.TryGetValue(id, out PlaceSurface s) && s != null ? s : null;

        /// <summary>The station with <paramref name="id"/> (its piece id), or null.</summary>
        public static Station Station(int id) => StationsById.TryGetValue(id, out Station s) && s != null ? s : null;

        /// <summary>The first station of <paramref name="kind"/> by ascending id, or null.</summary>
        public static Station First(StationKind kind)
        {
            foreach (KeyValuePair<int, Station> kv in StationsById)
                if (kv.Value != null && kv.Value.Kind == kind) return kv.Value;
            return null;
        }

        public static void Register(PieceBehaviour piece)
        {
            if (piece == null || piece.PieceId <= 0) return;
            if (PiecesById.TryGetValue(piece.PieceId, out PieceBehaviour known) && known != null && known != piece)
                Debug.LogWarning("[Layout] piece id " + piece.PieceId + " registered twice (" + known.name + ", " + piece.name + "); the newer one wins");
            Remove(piece.PieceId);
            PiecesById[piece.PieceId] = piece;
            if (piece.Parked)
            {
                Changed(); // 062b review: a piece on the storage pallet: known by id, its stations and surfaces not usable
                return;
            }
            foreach (PlaceSurface surface in piece.Surfaces)
                if (surface != null && surface.Id >= 0 && surface.gameObject.activeSelf) SurfacesById[surface.Id] = surface;
            Station station = piece.Station;
            if (station != null) StationsById[piece.PieceId] = station;
            Changed();
        }

        public static void Unregister(PieceBehaviour piece)
        {
            if (piece == null) return;
            int id = piece.PieceId;
            if (!PiecesById.TryGetValue(id, out PieceBehaviour known) || known != piece) return;
            Remove(id);
            Changed();
        }

        /// <summary>Spec 062b: a registered piece moved (build mode): the version changes, so the aim caches look again.</summary>
        public static void Touch() => Changed();

        /// <summary>Forgets everything (a test's teardown, a reset).</summary>
        public static void Clear()
        {
            PiecesById.Clear();
            SurfacesById.Clear();
            StationsById.Clear();
            Changed();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Clear();

        private static void Remove(int pieceId)
        {
            PiecesById.Remove(pieceId);
            StationsById.Remove(pieceId);
            var gone = new List<int>();
            foreach (KeyValuePair<int, PlaceSurface> kv in SurfacesById)
                if (SurfaceId.PieceOf(kv.Key) == pieceId) gone.Add(kv.Key);
            foreach (int id in gone) SurfacesById.Remove(id);
        }

        private static void Changed()
        {
            Version++;
            _listsDirty = true;
        }

        private static void Refresh()
        {
            if (!_listsDirty) return;
            _listsDirty = false;
            PieceList.Clear();
            foreach (PieceBehaviour p in PiecesById.Values) if (p != null) PieceList.Add(p);
            SurfaceList.Clear();
            foreach (PlaceSurface s in SurfacesById.Values) if (s != null) SurfaceList.Add(s);
            StationList.Clear();
            foreach (Station s in StationsById.Values) if (s != null) StationList.Add(s);
        }
    }
}
