using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Art;
using Worktown.Game.Cooking;
using Worktown.Game.Net;
using Object = UnityEngine.Object;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 026 AC4: the built kitchen at real heights (worktops, stove tops and every place surface at 0.90 m, the serving
    /// counter at 1.05 m), worktops made of 0.6 m cabinet modules, things resting on the tops (not floating, not sunk),
    /// contents inside their cookware and the real-size street buildings not overlapping.
    /// </summary>
    public class ScaleSceneTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/Graybox.unity";
        private const float HeightTolerance = 0.02f;

        private static List<T> All<T>(Scene scene) where T : Component
        {
            var found = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects()) found.AddRange(root.GetComponentsInChildren<T>(true));
            return found;
        }

        private static Bounds RendererBounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            Assert.IsNotEmpty(renderers, go.name + " draws nothing");
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>World top of a station's furniture (the wrapper's Model child, without a blender on top).</summary>
        private static float FurnitureTop(Station station)
        {
            Transform art = station.transform.Find("Art_" + ArtCatalogRules.StationKey(station.Kind));
            Assert.IsNotNull(art, station.name + " has no catalog art");
            Transform model = art.Find("Model");
            return RendererBounds((model != null ? model : art).gameObject).max.y;
        }

        [Test]
        public void BuiltScene_TopsAndPlaceSurfacesAreAtWorktopHeight_TheCounterAtServingHeight()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                int counters = 0, stoves = 0;
                foreach (Station station in All<Station>(scene))
                {
                    // Spec 028b: the fridge and the dry shelf are 1.80 m high (the guide's fridge).
                    // Spec 032: the register stands on the serving counter's run: its own height above the counter top.
                    bool register = station.Kind == StationKind.Register;
                    bool drinks = station.Kind == StationKind.DrinkDispenser; // spec 033: on the front counter too
                    bool terminal = station.Kind == StationKind.Terminal; // spec 034: a screen hanging on the back wall
                    float floor = register || drinks ? ArtSizeTable.ServingCounterHeight
                        : terminal ? TerminalArtRules.CentreHeight - TerminalArtRules.Height * 0.5f : 0f;
                    float expected = station.Kind == StationKind.Counter ? ArtSizeTable.ServingCounterHeight
                        : register ? floor + RegisterArtRules.Height
                        : drinks ? floor + DrinkArtRules.Height
                        : terminal ? floor + TerminalArtRules.Height
                        : StockLayout.IsStock(station.Kind) ? ArtSizeTable.StockHeight : ArtSizeTable.WorktopHeight;
                    Assert.AreEqual(expected, FurnitureTop(station), HeightTolerance, station.name + " furniture top");
                    Bounds body = station.transform.Find("Body").GetComponent<BoxCollider>().bounds;
                    // Spec 029: the fryer's collider stops under its oil wells (a lowered basket is aimed at above it).
                    float colliderTop = station.Kind == StationKind.Fryer ? FryerArt.BodyColliderTop : expected;
                    Assert.AreEqual(colliderTop, body.max.y, HeightTolerance, station.name + " collider top");
                    Assert.AreEqual(floor, body.min.y, 0.001f, station.name + (register || drinks ? " stands on the counter top"
                        : terminal ? " hangs at its height on the wall" : " stands on the floor"));
                    if (station.Kind == StationKind.Counter) counters++;
                    if (station.Kind == StationKind.Stove) stoves++;
                }
                Assert.AreEqual(1, counters, "one serving counter");
                Assert.AreEqual(2, stoves);

                List<PlaceSurface> surfaces = All<PlaceSurface>(scene);
                Assert.AreEqual(12, surfaces.Count); // spec 030: the griddle's deck
                foreach (PlaceSurface surface in surfaces)
                {
                    for (int i = 0; i < surface.SpotCount; i++)
                        Assert.AreEqual(ArtSizeTable.WorktopHeight, surface.SpotWorldPosition(i).y, HeightTolerance, surface.name + " spot " + i);
                    // The top is the furniture under it: nothing floats above it or sinks into it.
                    Station station = surface.GetComponentInParent<Station>();
                    float furniture = station != null ? FurnitureTop(station) : WorktopModulesTop(surface);
                    Assert.AreEqual(furniture, surface.SpotWorldPosition(0).y, 0.005f, surface.name + " rests on its furniture");
                    if (station == null)
                        Assert.AreEqual(ArtSizeTable.WorktopHeight, surface.GetComponent<BoxCollider>().bounds.max.y, HeightTolerance,
                            surface.name + " collider top");
                }
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        private static float WorktopModulesTop(PlaceSurface worktop)
        {
            float top = float.MinValue;
            foreach (Transform child in worktop.transform)
                if (child.name.StartsWith("Module ")) top = Mathf.Max(top, RendererBounds(child.gameObject).max.y);
            Assert.Greater(top, 0f, worktop.name + " has no modules");
            return top;
        }

        [Test]
        public void BuiltScene_WorktopsAreMadeOfSixtyCentimetreModules()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ArtCatalog>("Assets/_Project/Art/ArtCatalog.asset");
            Assert.IsTrue(catalog.TryGet(ArtKey.Worktop, out GameObject modulePrefab));
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                int worktops = 0;
                foreach (PlaceSurface surface in All<PlaceSurface>(scene))
                {
                    if (surface.GetComponentInParent<Station>() != null) continue;
                    worktops++;
                    Bounds box = surface.GetComponent<BoxCollider>().bounds;
                    var modules = new List<Transform>();
                    foreach (Transform child in surface.transform)
                    {
                        if (!child.name.StartsWith("Module ")) continue;
                        Assert.AreSame(modulePrefab, PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject), child.name);
                        modules.Add(child);
                    }
                    (int count, float width) = ArtSizeTable.WorktopModules(surface.Size.x);
                    Assert.AreEqual(count, modules.Count, surface.name + " modules");
                    modules.Sort((a, b) => a.position.x.CompareTo(b.position.x));
                    float x = box.min.x;
                    for (int i = 0; i < modules.Count; i++)
                    {
                        Bounds b = RendererBounds(modules[i].gameObject);
                        Assert.AreEqual(width, b.size.x, 0.01f, surface.name + " " + modules[i].name + " width");
                        Assert.AreEqual(x, b.min.x, 0.01f, surface.name + " " + modules[i].name + " follows the one before");
                        Assert.AreEqual(box.size.z, b.size.z, 0.02f, surface.name + " " + modules[i].name + " depth");
                        Assert.AreEqual(0f, b.min.y, 0.005f, modules[i].name + " stands on the floor");
                        x = b.max.x;
                    }
                    Assert.AreEqual(box.max.x, x, 0.02f, surface.name + ": the modules fill the run");
                    Assert.LessOrEqual(Mathf.Abs(width - ArtSizeTable.WorktopModuleWidth), ArtSizeTable.WorktopModuleSlack + 1e-4f,
                        surface.name + ": modules of " + width + " m");
                    Assert.IsNull(surface.transform.Find("Art_" + ArtKey.Worktop), surface.name + " still has the stretched cabinet");
                }
                Assert.AreEqual(5, worktops);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void BuiltScene_CookwareRestsOnItsSurface()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                List<Component> vessels = VesselTestKit.AllOf(scene, VesselTestKit.VesselType);
                // Spec 029: the two fryer baskets hang in the oil, not on a worktop.
                Assert.AreEqual(2, vessels.RemoveAll(v => VesselTestKit.KindOf(v) == VesselKind.Basket));
                // Spec 030: the six griddle spots lie on the griddle's plate.
                Assert.AreEqual(Worktown.Game.Cooking.GriddleRules.SpotsPerGriddle, vessels.RemoveAll(v => VesselTestKit.KindOf(v) == VesselKind.Griddle));
                Assert.AreEqual(1, vessels.RemoveAll(v => VesselTestKit.KindOf(v) == VesselKind.Assembly)); // spec 031: the build spot on its board
                Assert.AreEqual(6, vessels.Count);
                foreach (Component vessel in vessels)
                {
                    Bounds model = RendererBounds(vessel.transform.Find("Visual").gameObject);
                    Assert.AreEqual(ArtSizeTable.WorktopHeight, model.min.y, HeightTolerance, vessel.name + " model bottom on the top");
                    Assert.AreEqual(ArtSizeTable.WorktopHeight, vessel.GetComponent<BoxCollider>().bounds.min.y, HeightTolerance,
                        vessel.name + " collider bottom on the top");
                }
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        /// <summary>The cookware prefabs and their content anchor: a full load of the largest pieces stays inside the rim.</summary>
        [TestCase(VesselKind.Pan)]
        [TestCase(VesselKind.Pot)]
        [TestCase(VesselKind.Plate)]
        [TestCase(VesselKind.Bowl)]
        [TestCase(VesselKind.Board)]
        public void CookwareContents_SitInsideTheCookware(VesselKind kind)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/" + kind + ".prefab");
            Assert.IsNotNull(prefab, kind + " prefab missing");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Component vessel = VesselTestKit.VesselOn(instance);
                var anchor = new SerializedObject(vessel).FindProperty("_visual").objectReferenceValue as FoodVisual;
                Assert.IsNotNull(anchor);
                Bounds model = RendererBounds(instance.transform.Find("Visual").gameObject);
                Vector2 centre = BaseCentre(instance.transform.Find("Visual").gameObject);
                float rim = Mathf.Min(model.size.x, model.size.z) * 0.5f;
                Assert.AreEqual(centre.x, anchor.transform.position.x, 0.01f, kind + ": contents over the middle of the base");
                Assert.AreEqual(centre.y, anchor.transform.position.z, 0.01f, kind + ": contents over the middle of the base");
                Assert.That(anchor.transform.position.y, Is.InRange(model.min.y, model.max.y + 0.001f), kind + " contents height");

                // Four halves (the widest pile) and four whole potatoes (the largest piece).
                foreach (FoodLook part in new[] { new FoodLook(1, FoodLook.ShapeHalves, 0, 0, FoodLook.KindSingle, 0),
                                                  new FoodLook(0, FoodLook.ShapeWhole, 0, 0, FoodLook.KindSingle, 0) })
                {
                    anchor.ShowParts(new[] { part, part, part, part });
                    Bounds contents = ActiveBounds(anchor.gameObject);
                    float reach = Mathf.Max(
                        Mathf.Max(Mathf.Abs(contents.min.x - centre.x), Mathf.Abs(contents.max.x - centre.x)),
                        Mathf.Max(Mathf.Abs(contents.min.z - centre.y), Mathf.Abs(contents.max.z - centre.y)));
                    Assert.LessOrEqual(reach, rim + 0.002f, kind + ": " + part + " sticks out over the rim");
                    Assert.GreaterOrEqual(contents.min.y, model.min.y - 0.001f, kind + ": contents sink through the bottom");
                }
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void FoodPrefab_PlatedAndBowlDishesStayOnTheirDish()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Food.prefab");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                FoodVisual visual = instance.GetComponentInChildren<FoodVisual>(true);
                var half = new FoodLook(1, FoodLook.ShapeHalves, 0, 0, FoodLook.KindSingle, 0);
                var parts = new[] { half, half, half, half };
                foreach (byte kind in new[] { FoodLook.KindPlated, FoodLook.KindMixed })
                {
                    visual.ShowFood(new FoodLook(0, 0, 0, 0, kind, 4), parts);
                    Bounds dish = default, contents = default;
                    bool anyDish = false, anyContents = false;
                    foreach (Transform child in visual.transform)
                    {
                        if (!child.gameObject.activeSelf) continue;
                        Bounds b = RendererBounds(child.gameObject);
                        if (child.name.StartsWith("Art_FoodPlate") || child.name.StartsWith("Art_FoodBowl")) Encapsulate(ref dish, ref anyDish, b);
                        else Encapsulate(ref contents, ref anyContents, b);
                    }
                    Assert.IsTrue(anyDish && anyContents, "dish kind " + kind);
                    float rim = Mathf.Min(dish.size.x, dish.size.z) * 0.5f;
                    float reach = Mathf.Max(
                        Mathf.Max(Mathf.Abs(contents.min.x - dish.center.x), Mathf.Abs(contents.max.x - dish.center.x)),
                        Mathf.Max(Mathf.Abs(contents.min.z - dish.center.z), Mathf.Abs(contents.max.z - dish.center.z)));
                    Assert.LessOrEqual(reach, rim + 0.002f, "parts stick out of dish kind " + kind);
                    Assert.GreaterOrEqual(contents.min.y, dish.min.y - 0.001f, "parts sink through dish kind " + kind);
                }
            }
            finally { Object.DestroyImmediate(instance); }
        }

        /// <summary>Free areas of the street: the road strip, the customers' path and the crossroad's south arm.</summary>
        private static readonly (string Name, Rect Area)[] StreetAreas =
        {
            ("the road", Rect.MinMaxRect(9f, 5.5f, 39f, 10.5f)),
            ("the customers' path", Rect.MinMaxRect(6.1f, 10.3f, 24f, 11.3f)), // spec 027a: the queue's tail at x 6.64 (was 8.5)
            ("the crossroad's south arm", Rect.MinMaxRect(34f, -50f, 39f, 5.5f))
        };

        private static bool Overlaps(Bounds a, Bounds b) =>
            a.min.x < b.max.x && a.max.x > b.min.x && a.min.z < b.max.z && a.max.z > b.min.z;

        private static bool Overlaps(Bounds a, Rect area) =>
            a.min.x < area.xMax && a.max.x > area.xMin && a.min.z < area.yMax && a.max.z > area.yMin;

        [Test]
        public void BuiltScene_StreetBuildingsAndPropsDoNotOverlapEachOtherOrTheStreet()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                GameObject street = Array.Find(scene.GetRootGameObjects(), g => g.name == "Street");
                Assert.IsNotNull(street);
                var buildings = new List<(string Name, Bounds Bounds)>();
                var props = new List<(string Name, Bounds Bounds)>();
                foreach (Transform child in street.transform)
                {
                    if (child.name.StartsWith("Art_Building")) buildings.Add((child.name, RendererBounds(child.gameObject)));
                    else if (child.name.StartsWith("Art_Prop")) props.Add((child.name, RendererBounds(child.gameObject)));
                }
                Assert.AreEqual(5, buildings.Count, "the shop and four houses");
                Assert.AreEqual(3 + 2 + 1 + 1 + 2 + 10 + 1, props.Count, "lamps, trees, dumpster, trash can, planters, fence, awning");

                for (int i = 0; i < buildings.Count; i++)
                {
                    Bounds a = buildings[i].Bounds;
                    Assert.AreEqual(ArtSizeTable.TargetSize(ArtKey.BuildingHouseA), a.size.y, 7.5f * ArtSizeTable.Tolerance, buildings[i].Name);
                    Assert.IsTrue(a.min.z > 11.3f || a.max.z < 5.5f, buildings[i].Name + " stands on the street or the path");
                    foreach ((string area, Rect rect) in StreetAreas)
                        Assert.IsFalse(Overlaps(a, rect), buildings[i].Name + " stands on " + area);
                    Assert.Less(Mathf.Max(Mathf.Abs(a.min.x), Mathf.Abs(a.max.x)), 50f, buildings[i].Name + " leaves the ground");
                    Assert.Less(Mathf.Max(Mathf.Abs(a.min.z), Mathf.Abs(a.max.z)), 50f, buildings[i].Name + " leaves the ground");
                    for (int j = i + 1; j < buildings.Count; j++)
                        Assert.IsFalse(Overlaps(a, buildings[j].Bounds), buildings[i].Name + " overlaps " + buildings[j].Name);
                }

                Bounds shop = buildings.Find(b => b.Name.StartsWith("Art_BuildingShop")).Bounds;
                foreach ((string name, Bounds p) in props)
                {
                    if (name.StartsWith("Art_PropAwning"))
                    {
                        // On the shop's front: its back edge at most 5 cm into the wall, the rest outside, above the heads.
                        Assert.LessOrEqual(p.max.z, shop.min.z + 0.05f, "the awning is buried in the shop");
                        Assert.GreaterOrEqual(p.max.z, shop.min.z - 0.01f, "the awning floats in front of the shop");
                        Assert.Less(p.min.z, shop.min.z - 0.5f, "the awning stays outside the shop");
                        Assert.IsTrue(p.min.x >= shop.min.x - 0.01f && p.max.x <= shop.max.x + 0.01f, "the awning is wider than the shop");
                        Assert.GreaterOrEqual(p.min.y, 2f, "the awning hangs above the heads");
                    }
                    foreach ((string building, Bounds b) in buildings)
                        if (!(name.StartsWith("Art_PropAwning") && building.StartsWith("Art_BuildingShop")))
                            Assert.IsFalse(Overlaps(p, b), name + " overlaps " + building);

                    if (name.StartsWith("Art_PropLamp"))
                    {
                        // The arm hangs over the road by design; the post (its back end, as deep as it is wide) stands beside it.
                        Bounds post = new Bounds(new Vector3(p.center.x, p.center.y, p.min.z + p.size.x * 0.5f), new Vector3(p.size.x, p.size.y, p.size.x));
                        foreach ((string area, Rect rect) in StreetAreas)
                            Assert.IsFalse(Overlaps(post, rect), name + " post stands on " + area);
                        continue;
                    }
                    foreach ((string area, Rect rect) in StreetAreas)
                        Assert.IsFalse(Overlaps(p, rect), name + " stands on " + area);
                }
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        private static void Encapsulate(ref Bounds bounds, ref bool any, Bounds add)
        {
            if (!any) bounds = add;
            else bounds.Encapsulate(add);
            any = true;
        }

        private static Bounds ActiveBounds(GameObject root)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(false)) Encapsulate(ref bounds, ref any, r.bounds);
            Assert.IsTrue(any, "nothing drawn");
            return bounds;
        }

        /// <summary>XZ centre of a model's base: its vertices in the lowest quarter of its height (the pan without its handle).</summary>
        private static Vector2 BaseCentre(GameObject model)
        {
            var points = new List<Vector3>();
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>())
                foreach (Vector3 v in filter.sharedMesh.vertices) points.Add(filter.transform.TransformPoint(v));
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector3 p in points) { minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
            var b = new Bounds();
            bool any = false;
            foreach (Vector3 p in points)
                if (p.y <= minY + (maxY - minY) * 0.25f) Encapsulate(ref b, ref any, new Bounds(p, Vector3.zero));
            return new Vector2(b.center.x, b.center.z);
        }
    }
}
