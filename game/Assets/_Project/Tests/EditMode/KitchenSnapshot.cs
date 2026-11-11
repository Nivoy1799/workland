using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Layout;
using Worktown.Game.Net;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 062a: the parity reference, Data/kitchen-snapshot.json (written by LayoutExporter from the builder's kitchen on main 075c101,
    /// before the kitchen became layout pieces): every station and place surface with its old index, the piece and local number it became,
    /// its world pose, size, top and spot count; the items' homes; the guests' and players' anchors.
    /// </summary>
    [Serializable]
    public sealed class KitchenSnapshot
    {
        public const string Path = "Assets/_Project/Tests/EditMode/Data/kitchen-snapshot.json";

        public string source = string.Empty;
        public string note = string.Empty;
        public StationEntry[] stations = new StationEntry[0];
        public SurfaceEntry[] surfaces = new SurfaceEntry[0];
        public ItemEntry[] items = new ItemEntry[0];
        public AnchorEntry[] anchors = new AnchorEntry[0];
        /// <summary>062a review: the stations' stock slots and buttons (piece, option, anchor), their nested objects, the counters' bodies.</summary>
        public PointEntry[] slots = new PointEntry[0];
        public PointEntry[] buttons = new PointEntry[0];
        public NestedEntry[] nested = new NestedEntry[0];
        public BodyEntry[] bodies = new BodyEntry[0];

        [Serializable] public sealed class PointEntry
        {
            public string name; public int piece; public int option; public Vector3 position;
        }

        [Serializable] public sealed class NestedEntry
        {
            public string name; public string kind; public int piece; public Vector3 position; public float yaw;
        }

        /// <summary>A worktop's (piece) or one of the builder's two front counter runs' (piece -1) body collider bounds.</summary>
        [Serializable] public sealed class BodyEntry
        {
            public string name; public int piece; public Vector3 center; public Vector3 size;
        }

        [Serializable] public sealed class StationEntry
        {
            public string name; public string kind; public int oldIndex; public int piece;
            public Vector3 position; public float yaw; public Vector3 size; public float top; public int slots;
        }

        [Serializable] public sealed class SurfaceEntry
        {
            public string name; public string kind; public int oldIndex; public int piece; public int local;
            public Vector3 position; public float yaw; public Vector2 size; public float top; public int spots;
            public int station; public int burner; public int tap;
        }

        [Serializable] public sealed class ItemEntry
        {
            public string name; public string kind; public Vector3 position; public float yaw; public int piece; public int local; public int spot;
        }

        [Serializable] public sealed class AnchorEntry
        {
            public string name; public Vector3 position; public float yaw;
        }

        private static KitchenSnapshot _loaded;

        public static KitchenSnapshot Load()
        {
            if (_loaded != null) return _loaded;
            string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", Path));
            Assert.IsTrue(File.Exists(full), "the parity snapshot " + Path);
            _loaded = JsonUtility.FromJson<KitchenSnapshot>(File.ReadAllText(full));
            return _loaded;
        }

        /// <summary>The surface's index before spec 062a (from its piece and local number), or -1.</summary>
        public static int OldIndexOf(PlaceSurface surface)
        {
            if (surface == null) return -1;
            int local = SurfaceId.LocalOf(surface.Id);
            foreach (SurfaceEntry e in Load().surfaces)
                if (e.piece == surface.PieceId && e.local == local) return e.oldIndex;
            return -1;
        }

        public AnchorEntry Anchor(string name) => Array.Find(anchors, a => a.name == name);
    }
}
