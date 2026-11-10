using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Cooking;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 039 AC2: every counter-height top in the shop accepts items. The test samples a 0.1 m grid over the top face of every
    /// collider whose top is at counter height (0.85 to 1.10 m) inside the shop's walls. Each sample must lie inside a PlaceSurface's
    /// footprint at that height, or belong to a listed exception (PlaceCoverage.Exceptions: the collider itself, or within
    /// PlaceCoverage.Rim of the exception's footprint), or lie under or within the rim of something standing on that top (a register
    /// on the front counter; that thing is checked on its own). A dead point fails the test with the object and the point.
    /// </summary>
    public class PlaceCoverageTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/Graybox.unity";
        /// <summary>Something stands on a top when it reaches at least this far above the top and starts no higher than this.</summary>
        private const float StandsAbove = 0.02f;
        private const float StandsFrom = 0.25f;

        private static List<T> All<T>(Scene scene) where T : Component
        {
            var found = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects()) found.AddRange(root.GetComponentsInChildren<T>(true));
            return found;
        }

        private static string PathOf(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        /// <summary>The shop's floor area: the XZ bounds of its walls (the back wall and the side walls).</summary>
        private static Bounds ShopArea(Scene scene)
        {
            bool any = false;
            Bounds area = default;
            foreach (BoxCollider box in All<BoxCollider>(scene))
            {
                if (!box.name.StartsWith("Back wall") && !box.name.StartsWith("Side wall")) continue;
                if (!any) area = box.bounds;
                else area.Encapsulate(box.bounds);
                any = true;
            }
            Assert.IsTrue(any, "no shop walls in the scene");
            return area;
        }

        private static bool InShop(Bounds area, Vector3 p) => p.x >= area.min.x && p.x <= area.max.x && p.z >= area.min.z && p.z <= area.max.z;

        /// <summary>Below this height (m) a place surface's collider is only its thin aim collider, not a body (039 review).</summary>
        private const float AimColliderHeight = 0.05f;

        /// <summary>The "standing on the top" rule counts only things that start at most this far below the top (039 review).</summary>
        private const float StandsBelow = 0.02f;

        /// <summary>
        /// A loose item, a character or a surface's own thin aim collider is not furniture. 039 review: a place surface whose collider
        /// is a real body (a worktop is its own top) is furniture, so its top is sampled against its own footprint too.
        /// </summary>
        private static bool IsFurniture(Collider c)
        {
            if (!c.enabled || !c.gameObject.activeInHierarchy || c.isTrigger) return false;
            if (c is CharacterController) return false;
            if (c.GetComponentInParent(typeof(Pickup)) != null || c.GetComponentInParent(typeof(Worktown.Game.Npc.Customer)) != null) return false;
            if (c.GetComponent<PlaceSurface>() != null && c.bounds.size.y < AimColliderHeight) return false;
            return true;
        }

        /// <summary>The point relative to the box's centre in its own axes, in metres.</summary>
        private static Vector3 BoxLocal(BoxCollider box, Vector3 p)
        {
            Vector3 local = box.transform.InverseTransformPoint(p) - box.center;
            return Vector3.Scale(local, Abs(box.transform.lossyScale));
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        /// <summary>Whether the point's XZ lies on the collider's footprint grown by <paramref name="margin"/> (a box in its own frame).</summary>
        private static bool OverFootprint(Collider c, Vector3 p, float margin)
        {
            if (c is BoxCollider box)
            {
                Vector3 local = BoxLocal(box, p);
                Vector3 half = Vector3.Scale(box.size, Abs(box.transform.lossyScale)) * 0.5f;
                return Mathf.Abs(local.x) <= half.x + margin + 0.001f && Mathf.Abs(local.z) <= half.z + margin + 0.001f;
            }
            return Within(c.bounds, p, margin);
        }

        private static bool Within(Bounds b, Vector3 p, float margin) =>
            p.x >= b.min.x - margin - 0.001f && p.x <= b.max.x + margin + 0.001f && p.z >= b.min.z - margin - 0.001f && p.z <= b.max.z + margin + 0.001f;

        /// <summary>The sample points of a collider's top face, in world space at its top height.</summary>
        private static List<Vector3> TopSamples(Collider c, float top)
        {
            var points = new List<Vector3>();
            if (c is BoxCollider box && Mathf.Abs(Vector3.Dot(box.transform.up, Vector3.up) - 1f) < 0.001f)
            {
                Vector3 scale = Abs(box.transform.lossyScale);
                var size = new Vector2(box.size.x * scale.x, box.size.z * scale.z);
                foreach (Vector2 g in PlaceCoverage.Grid(size))
                {
                    Vector3 local = box.center + new Vector3(g.x / scale.x, 0f, g.y / scale.z);
                    Vector3 world = box.transform.TransformPoint(local);
                    points.Add(new Vector3(world.x, top, world.z));
                }
                return points;
            }
            Bounds b = c.bounds;
            foreach (Vector2 g in PlaceCoverage.Grid(new Vector2(b.size.x, b.size.z)))
                points.Add(new Vector3(b.center.x + g.x, top, b.center.z + g.y));
            return points;
        }

        private static bool OnSurface(List<PlaceSurface> surfaces, Vector3 p)
        {
            foreach (PlaceSurface s in surfaces)
            {
                float rest = s.transform.TransformPoint(new Vector3(0f, s.TopY, 0f)).y;
                if (Mathf.Abs(rest - p.y) > PlaceCoverage.HeightTolerance) continue;
                Vector3 local = s.transform.InverseTransformPoint(p);
                if (Mathf.Abs(local.x) <= s.Size.x * 0.5f + 0.005f && Mathf.Abs(local.z) <= s.Size.y * 0.5f + 0.005f) return true;
            }
            return false;
        }

        /// <summary>The exception the collider belongs to (its object or a parent is named by one), or -1.</summary>
        private static int ExceptionOf(Transform t)
        {
            for (; t != null; t = t.parent)
            {
                int e = PlaceCoverage.ExceptionFor(t.name);
                if (e >= 0) return e;
            }
            return -1;
        }

        private sealed class Area
        {
            public int Exception;
            public Bounds Bounds;
        }

        /// <summary>The XZ footprints (colliders and renderers, else the position) of every object an exception names.</summary>
        private static List<Area> ExceptionAreas(Scene scene, Bounds shop)
        {
            var areas = new List<Area>();
            foreach (Transform t in All<Transform>(scene))
            {
                int e = PlaceCoverage.ExceptionFor(t.name);
                if (e < 0 || !t.gameObject.activeInHierarchy) continue;
                var b = new Bounds(t.position, Vector3.zero);
                foreach (Collider c in t.GetComponentsInChildren<Collider>())
                    if (c.enabled) b.Encapsulate(c.bounds);
                foreach (Renderer r in t.GetComponentsInChildren<Renderer>())
                    if (!(r is ParticleSystemRenderer)) b.Encapsulate(r.bounds);
                if (InShop(shop, b.center)) areas.Add(new Area { Exception = e, Bounds = b });
            }
            return areas;
        }

        private sealed class Scan
        {
            public int Tops, Samples, OnSurfaces, Dead;
            public readonly StringBuilder Report = new StringBuilder();
        }

        /// <summary>Samples every counter-height top in the shop against <paramref name="surfaces"/> and the listed exceptions.</summary>
        private static Scan ScanTops(Scene scene, List<PlaceSurface> surfaces)
        {
            Bounds shop = ShopArea(scene);
            var exceptionAreas = new List<Area>();
            foreach (Area a in ExceptionAreas(scene, shop))
                if (!PlaceCoverage.Exceptions[a.Exception].GuestHalfOnly) exceptionAreas.Add(a);
            var furniture = new List<Collider>();
            foreach (Collider c in All<Collider>(scene))
                if (IsFurniture(c) && InShop(shop, c.bounds.center)) furniture.Add(c);

            var scan = new Scan();
            foreach (Collider c in furniture)
            {
                float top = c.bounds.max.y;
                if (!PlaceCoverage.IsCounterHeight(top)) continue;
                scan.Tops++;
                int own = ExceptionOf(c.transform);
                bool wholeException = own >= 0 && !PlaceCoverage.Exceptions[own].GuestHalfOnly;
                bool guestHalf = own >= 0 && PlaceCoverage.Exceptions[own].GuestHalfOnly && c is BoxCollider;
                int deadHere = 0, total = 0;
                Vector3 first = default;
                foreach (Vector3 p in TopSamples(c, top))
                {
                    // Something standing on this top (a station on the front counter) covers the point and its rim.
                    bool covered = false;
                    foreach (Collider d in furniture)
                    {
                        if (d == c) continue;
                        Bounds db = d.bounds;
                        if (db.max.y > top + StandsAbove && db.min.y >= top - StandsBelow && db.min.y < top + StandsFrom && OverFootprint(d, p, PlaceCoverage.Rim)) { covered = true; break; }
                    }
                    if (covered) continue;
                    total++;
                    scan.Samples++;
                    if (wholeException) continue;
                    if (guestHalf && BoxLocal((BoxCollider)c, p).z > 0.001f) continue;
                    if (OnSurface(surfaces, p))
                    {
                        scan.OnSurfaces++;
                        continue;
                    }
                    bool excepted = false;
                    foreach (Area a in exceptionAreas)
                        if (Within(a.Bounds, p, PlaceCoverage.Rim)) { excepted = true; break; }
                    if (excepted) continue;
                    if (deadHere == 0) first = p;
                    deadHere++;
                }
                if (deadHere == 0) continue;
                scan.Dead += deadHere;
                scan.Report.AppendLine(PathOf(c.transform) + " (top " + top.ToString("0.000") + " m): " + deadHere + " of " + total
                    + " points take no items, e.g. (" + first.x.ToString("0.00") + ", " + first.y.ToString("0.00") + ", "
                    + first.z.ToString("0.00") + ")");
            }
            return scan;
        }

        [Test]
        public void EveryCounterHeightTop_IsAPlaceSurface_OrAListedException()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Scan scan = ScanTops(scene, All<PlaceSurface>(scene));
                Assert.Greater(scan.Tops, 10, "the shop has counter-height tops");
                Assert.Greater(scan.Samples, 1000);
                Assert.Greater(scan.OnSurfaces, scan.Samples / 2, "most of the counter tops are place surfaces");
                Assert.AreEqual(0, scan.Dead, "counter-height tops that are neither a place surface nor a listed exception:\n" + scan.Report);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        /// <summary>The scan finds dead tops: without the spec 039 surfaces (index 12 and up) it names the front counter and the stations.</summary>
        [Test]
        public void WithoutTheNewSurfaces_TheScanNamesTheDeadTops()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var old = All<PlaceSurface>(scene).FindAll(s => s.Index < 12);
                Assert.AreEqual(12, old.Count);
                Scan scan = ScanTops(scene, old);
                string report = scan.Report.ToString();
                Assert.Greater(scan.Dead, 100, report);
                foreach (string dead in new[] { "Front counter 1", "Front counter 2", "Stove 1/Body", "Fryer/Deck", "Assembly/Body", "Sink/Body", "Plates/Body" })
                    StringAssert.Contains(dead, report, "the scan must name " + dead);
                StringAssert.Contains("points take no items, e.g. (", report, "and a point");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        /// <summary>
        /// 039 review: the scan sees a worktop body whose own surface footprint shrank (the bodies are sampled now), and a tall box from
        /// the floor over the whole footprint does not hide it (only things standing on the top cover it).
        /// </summary>
        [Test]
        public void AShrunkWorktop_FailsTheScan_EvenUnderAFullFootprintBox()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                List<PlaceSurface> surfaces = All<PlaceSurface>(scene);
                PlaceSurface worktop = surfaces.Find(s => s.name.StartsWith("Worktop") && s.TryGetComponent(out BoxCollider b) && b.bounds.size.y > 0.2f);
                Assert.IsNotNull(worktop, "a worktop that is its own body");
                Assert.AreEqual(0, ScanTops(scene, surfaces).Dead, "the scene as built");

                var so = new UnityEditor.SerializedObject(worktop);
                SerializedPropertyShrink(so.FindProperty("_size"));
                so.ApplyModifiedPropertiesWithoutUndo();
                Scan shrunk = ScanTops(scene, surfaces);
                Assert.Greater(shrunk.Dead, 0, "a worktop whose surface no longer covers its top");
                StringAssert.Contains(worktop.name, shrunk.Report.ToString());

                Bounds body = worktop.GetComponent<BoxCollider>().bounds;
                var box = new GameObject("Coverage test box");
                SceneManager.MoveGameObjectToScene(box, scene);
                box.transform.position = new Vector3(body.center.x, 0.65f, body.center.z);
                box.AddComponent<BoxCollider>().size = new Vector3(body.size.x + 0.1f, 1.3f, body.size.z + 0.1f);
                Scan boxed = ScanTops(scene, surfaces);
                Assert.Greater(boxed.Dead, 0, "a 1.3 m box from the floor does not stand on the top, so it hides nothing");
                StringAssert.Contains(worktop.name, boxed.Report.ToString());
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        private static void SerializedPropertyShrink(UnityEditor.SerializedProperty size)
        {
            Assert.IsNotNull(size, "PlaceSurface._size");
            size.vector2Value = new Vector2(size.vector2Value.x * 0.5f, size.vector2Value.y);
        }

        /// <summary>Rule 2: items rest on the real top of the furniture under the surface (the front counter 1.05 m, worktops 0.90 m).</summary>
        [Test]
        public void EverySurface_RestsOnTheTopUnderIt_AtCounterHeight()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Bounds shop = ShopArea(scene);
                var furniture = new List<Collider>();
                foreach (Collider c in All<Collider>(scene))
                    if (IsFurniture(c) && InShop(shop, c.bounds.center)) furniture.Add(c);
                int frontCounter = 0;
                foreach (PlaceSurface s in All<PlaceSurface>(scene))
                {
                    Assert.Greater(s.SpotCount, 0, s.name + ": rule 3, every surface has a spot");
                    Vector3 spot = s.SpotWorldPosition(0);
                    Assert.IsTrue(PlaceCoverage.IsCounterHeight(spot.y), s.name + " rests at " + spot.y);
                    // The highest furniture top right under the spot is where the item stands.
                    Bounds own = s.GetComponent<BoxCollider>().bounds;
                    float under = own.size.y > 0.2f ? own.max.y : float.MinValue; // a worktop is its own furniture
                    foreach (Collider c in furniture)
                        if (c.bounds.max.y <= spot.y + 0.05f && OverFootprint(c, spot, 0f)) under = Mathf.Max(under, c.bounds.max.y);
                    if (s.IsBurner || s.GetComponentInParent<Station>() != null)
                        Assert.That(under, Is.LessThanOrEqualTo(spot.y + 0.01f), s.name + ": nothing solid above its top");
                    else Assert.AreEqual(under, spot.y, 0.005f, s.name + " rests on the furniture under it, not floating or sunk");
                    if (s.transform.parent != null && s.transform.parent.name.StartsWith("Front counter"))
                    {
                        frontCounter++;
                        Assert.AreEqual(1.05f, spot.y, 0.005f, s.name + ": the front counter top");
                    }
                }
                Assert.GreaterOrEqual(frontCounter, 4, "both front counter runs take items, beside everything that stands on them");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        /// <summary>Spec 039 Q1: only the cook side of the front counter takes items; the aim collider still covers the whole top.</summary>
        [Test]
        public void FrontCounterTops_SpotsOnTheCookSideOnly()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                int tops = 0;
                foreach (PlaceSurface s in All<PlaceSurface>(scene))
                {
                    Transform run = s.transform.parent;
                    if (run == null || !run.name.StartsWith("Front counter")) continue;
                    tops++;
                    var runBox = run.GetComponent<BoxCollider>();
                    for (int i = 0; i < s.SpotCount; i++)
                        Assert.Less(BoxLocal(runBox, s.SpotWorldPosition(i)).z, 0f, s.name + " spot " + i + " lies on the cook side (local -z)");
                    Bounds aim = s.GetComponent<BoxCollider>().bounds;
                    Assert.AreEqual(runBox.bounds.size.z, aim.size.z, 0.01f, s.name + ": aiming anywhere across the top finds it");
                    // 039 review: and it lies over the run's whole depth (centred on it), not shifted onto one half.
                    Assert.AreEqual(runBox.bounds.center.z, aim.center.z, 0.01f, s.name + ": the aim collider is centred on the run's depth");
                }
                Assert.GreaterOrEqual(tops, 4);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void EveryException_HasAReason_AndNamesSomethingInTheShop()
        {
            var seen = new HashSet<string>();
            foreach ((string name, string reason, bool _) in PlaceCoverage.Exceptions)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(name));
                Assert.IsTrue(seen.Add(name), name + " is listed twice");
                Assert.That(reason, Is.Not.Null.And.Length.GreaterThan(20), name + " needs a reason");
            }
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Bounds shop = ShopArea(scene);
                var found = new HashSet<int>();
                foreach (Area a in ExceptionAreas(scene, shop)) found.Add(a.Exception);
                for (int i = 0; i < PlaceCoverage.Exceptions.Count; i++)
                    Assert.IsTrue(found.Contains(i), "exception '" + PlaceCoverage.Exceptions[i].Name + "' names nothing in the shop (renamed or removed?)");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void ExceptionNames_MatchTheObjectOrItsNumberedCopies()
        {
            Assert.IsTrue(PlaceCoverage.Names("Burner 0", "Burner"));
            Assert.IsTrue(PlaceCoverage.Names("Counter", "Counter"));
            Assert.IsFalse(PlaceCoverage.Names("Counter module 3", "Counter"), "the front counter's modules are not the serving counter");
            Assert.IsFalse(PlaceCoverage.Names("Counter top 2", "Counter"));
            Assert.IsFalse(PlaceCoverage.Names("Trash bag", "Trash"));
            Assert.IsTrue(PlaceCoverage.Names("Front counter 2", "Front counter"));
            Assert.AreEqual(-1, PlaceCoverage.ExceptionFor("Worktop 0"));
            Assert.AreEqual(-1, PlaceCoverage.ExceptionFor(null));
        }

        [Test]
        public void Grid_CoversTheTopInTenCentimetreCells_AtLeastOnePoint()
        {
            Assert.AreEqual(1, PlaceCoverage.Grid(new Vector2(0.05f, 0.05f)).Count);
            Assert.AreEqual(7 * 4, PlaceCoverage.Grid(new Vector2(0.7f, 0.35f)).Count);
            foreach (Vector2 p in PlaceCoverage.Grid(new Vector2(1.26f, 0.7f)))
            {
                Assert.LessOrEqual(Mathf.Abs(p.x), 0.63f);
                Assert.LessOrEqual(Mathf.Abs(p.y), 0.35f);
            }
            Assert.IsTrue(PlaceCoverage.IsCounterHeight(0.90f));
            Assert.IsTrue(PlaceCoverage.IsCounterHeight(1.05f));
            Assert.IsFalse(PlaceCoverage.IsCounterHeight(1.80f), "spec 039 Q2: the fridge and shelf tops are not counter height");
            Assert.IsFalse(PlaceCoverage.IsCounterHeight(0.70f));
            Assert.AreEqual(0.15f, PlaceCoverage.Rim, 0.001f, "a spot's edge margin plus a bit of a sample cell");
        }
    }
}
