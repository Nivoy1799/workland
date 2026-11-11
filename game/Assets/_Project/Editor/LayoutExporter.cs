using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Cooking;
using Worktown.Game.Layout;
using Worktown.Game.Net;
using Worktown.Sim.Layout;

namespace Worktown.Game.Editor
{
    /// <summary>
    /// Spec 062a: the parity snapshot (Tests/EditMode/Data/kitchen-snapshot.json) and the default layout's file. Its first run, on main
    /// 075c101 before the builder's kitchen code was cut into piece prefabs, wrote both from the builder's kitchen: every station and place
    /// surface with its name, kind, old index, world pose, size, top and spot count; the items' homes; the guests' and players' anchors;
    /// and default.json with one piece per station (id = old index + 1), worktop and front counter run between the things standing on it.
    /// That snapshot is the parity reference (DefaultLayoutParityTests) and is never rewritten by the build. Since then the default layout
    /// is hand-maintained data; <see cref="ExportSnapshot"/> writes a snapshot of the default layout as spawned now (offline), keeping each
    /// entry's old index, for a later spec that changes the default kitchen on purpose (Worktown/Export Kitchen Snapshot).
    /// </summary>
    public static class LayoutExporter
    {
        public const string SnapshotPath = GrayboxSceneBuilder.Root + "/Tests/EditMode/Data/kitchen-snapshot.json";
        public const string DefaultLayoutPath = GrayboxSceneBuilder.Root + "/Resources/Layouts/default.json";

        [Serializable] private sealed class Snapshot
        {
            public string source = string.Empty;
            public string note = string.Empty;
            public StationEntry[] stations;
            public SurfaceEntry[] surfaces;
            public ItemEntry[] items;
            public AnchorEntry[] anchors;
        }

        [Serializable] private sealed class StationEntry
        {
            public string name; public string kind; public int oldIndex; public int piece;
            public Vector3 position; public float yaw; public Vector3 size; public float top; public int slots;
        }

        [Serializable] private sealed class SurfaceEntry
        {
            public string name; public string kind; public int oldIndex; public int piece; public int local;
            public Vector3 position; public float yaw; public Vector2 size; public float top; public int spots;
            public int station; public int burner; public int tap;
        }

        [Serializable] private sealed class ItemEntry
        {
            public string name; public string kind; public Vector3 position; public float yaw; public int piece; public int local; public int spot;
        }

        [Serializable] private sealed class AnchorEntry
        {
            public string name; public Vector3 position; public float yaw;
        }

        [MenuItem("Worktown/Export Kitchen Snapshot")]
        public static void ExportSnapshot()
        {
            Scene scene = EditorSceneManager.OpenScene(GrayboxSceneBuilder.ScenePath, OpenSceneMode.Single);
            var prefabs = AssetDatabase.LoadAssetAtPath<PiecePrefabs>(PiecePrefabBuilder.PrefabsAssetPath);
            ShopLayout layout = DefaultLayout.Load();
            LayoutRegistry.Clear();
            List<GameObject> made = LayoutSpawner.InstantiateOffline(layout, prefabs, scene);
            try
            {
                Snapshot old = File.Exists(Full(SnapshotPath)) ? JsonUtility.FromJson<Snapshot>(File.ReadAllText(Full(SnapshotPath))) : null;
                int OldStation(int piece) => old?.stations?.FirstOrDefault(s => s.piece == piece)?.oldIndex ?? -1;
                int OldSurface(int piece, int local) => old?.surfaces?.FirstOrDefault(s => s.piece == piece && s.local == local)?.oldIndex ?? -1;
                var snapshot = new Snapshot
                {
                    source = "LayoutExporter.ExportSnapshot: the default layout spawned offline, " + GrayboxSceneBuilder.ScenePath,
                    note = "Spec 062a parity reference. oldIndex: the station's or surface's index before spec 062a (-1: new since).",
                    stations = LayoutRegistry.Stations.Select(s => new StationEntry
                    {
                        name = s.name, kind = s.Kind.ToString(), oldIndex = OldStation(s.Id), piece = s.Id, position = s.transform.position,
                        yaw = Yaw(s.transform), size = BodySize(s), top = BodyTop(s), slots = s.SlotCount,
                    }).ToArray(),
                    surfaces = LayoutRegistry.Surfaces.Select(s => new SurfaceEntry
                    {
                        name = s.name, kind = s.IsBurner ? "burner" : s.IsCupSpot ? "cup" : "top", oldIndex = OldSurface(s.PieceId, SurfaceId.LocalOf(s.Id)),
                        piece = s.PieceId, local = SurfaceId.LocalOf(s.Id), position = s.transform.position, yaw = Yaw(s.transform), size = s.Size,
                        top = s.SpotWorldPosition(0).y, spots = s.SpotCount, station = s.IsBurner ? s.StovePieceId : s.IsCupSpot ? s.DispenserPieceId : -1,
                        burner = s.Burner, tap = s.CupTap,
                    }).ToArray(),
                    items = layout.Homes.Select(h =>
                    {
                        GameObject item = made.FirstOrDefault(g => g.name == LayoutSpawner.ItemName(h.ItemKind, layout, h));
                        return new ItemEntry
                        {
                            name = item != null ? item.name : h.ItemKind, kind = h.ItemKind, position = item != null ? item.transform.position : Vector3.zero,
                            yaw = item != null ? Yaw(item.transform) : 0f, piece = h.PieceId, local = h.LocalSurface, spot = h.Spot,
                        };
                    }).ToArray(),
                    anchors = Anchors(scene),
                };
                Write(SnapshotPath, JsonUtility.ToJson(snapshot, true));
                Debug.Log("[Layout] snapshot: " + snapshot.stations.Length + " stations, " + snapshot.surfaces.Length + " surfaces");
            }
            finally
            {
                foreach (GameObject go in made) if (go != null) UnityEngine.Object.DestroyImmediate(go);
                LayoutRegistry.Clear();
            }
        }

        private static string Full(string assetPath) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));

        private static void Write(string assetPath, string json)
        {
            string full = Full(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, json.Replace("\r\n", "\n") + "\n");
            AssetDatabase.Refresh();
        }

        private static AnchorEntry[] Anchors(Scene scene)
        {
            var all = new List<Transform>();
            foreach (GameObject root in scene.GetRootGameObjects()) all.AddRange(root.GetComponentsInChildren<Transform>(true));
            var names = new[] { "StreetEntry", "QueueHead", "PayHead", "Spawn 1", "Spawn 2", "Spawn 3", "Spawn 4" };
            var list = new List<AnchorEntry>();
            foreach (string n in names)
            {
                Transform t = all.FirstOrDefault(x => x.name == n);
                if (t != null) list.Add(new AnchorEntry { name = n, position = t.position, yaw = Yaw(t) });
            }
            return list.ToArray();
        }

        private static float Yaw(Transform t) => Mathf.Repeat(t.eulerAngles.y, 360f);

        private static Vector3 BodySize(Station s)
        {
            Transform body = s.transform.Find("Body");
            return body != null && body.TryGetComponent(out BoxCollider box) ? box.bounds.size : Vector3.zero;
        }

        private static float BodyTop(Station s)
        {
            Transform body = s.transform.Find("Body");
            return body != null && body.TryGetComponent(out BoxCollider box) ? box.bounds.max.y : 0f;
        }
    }
}
