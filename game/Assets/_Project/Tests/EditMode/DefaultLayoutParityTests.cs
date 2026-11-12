using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Cooking;
using Worktown.Game.Layout;
using Worktown.Game.Net;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 062a AC3: the default layout, spawned offline into the built scene, is today's kitchen: every station and place surface of the
    /// parity snapshot (the builder's kitchen on main 075c101) is there by its piece and local number, with the same name and kind, its
    /// position within 1 mm, its yaw within 0.1 degrees, the same size, top height and spot count; the items lie on their old homes; the
    /// guests' and players' anchors did not move. A worktop's name carried its old surface index ("Worktop 5"); it is now numbered like
    /// any piece placed more than once ("Worktop 3"): NewWorktopNames pins each. 062a review: also the stock slots' and buttons'
    /// anchors, the nested objects (baskets, griddle spots, build spot, spit parts) and the counters' body colliders.
    /// </summary>
    public class DefaultLayoutParityTests
    {
        private const float Mm = 0.0011f;
        private const float Degrees = 0.1f;
        private static readonly Regex WorktopName = new Regex(@"^Worktop \d+$");

        private static List<T> All<T>(Scene scene) where T : Component
        {
            var found = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects()) found.AddRange(root.GetComponentsInChildren<T>(true));
            return found;
        }

        private static void Near(Vector3 expected, Vector3 actual, float tolerance, string what) =>
            Assert.LessOrEqual(Vector3.Distance(expected, actual), tolerance, what + ": " + expected.ToString("F4") + " vs " + actual.ToString("F4"));

        private static void Yaw(float expected, float actual, string what) =>
            Assert.LessOrEqual(Mathf.Abs(Mathf.DeltaAngle(expected, actual)), Degrees, what + " yaw " + expected + " vs " + actual);

        /// <summary>062a review: each worktop's old name (its old surface index) and its new one (its rank among the worktops by id).</summary>
        private static readonly Dictionary<string, string> NewWorktopNames = new Dictionary<string, string>
        {
            { "Worktop 0", "Worktop 1" }, // piece 19, the landing by the staff gap
            { "Worktop 1", "Worktop 2" }, // piece 20, prep
            { "Worktop 5", "Worktop 3" }, // piece 21, the pass at the right end
            { "Worktop 9", "Worktop 4" }, // piece 22, plating
        };

        private static void Name(string expected, string actual, string what)
        {
            if (WorktopName.IsMatch(expected))
            {
                Assert.IsTrue(NewWorktopNames.TryGetValue(expected, out string renamed), what + ": an unknown worktop");
                Assert.AreEqual(renamed, actual, what);
            }
            else Assert.AreEqual(expected, actual, what);
        }

        [Test]
        public void TheSnapshot_IsTodaysKitchen()
        {
            KitchenSnapshot snapshot = KitchenSnapshot.Load();
            StringAssert.Contains("075c101", snapshot.source, "written from main before the builder's kitchen was cut into pieces");
            Assert.AreEqual(18, snapshot.stations.Length);
            Assert.AreEqual(27, snapshot.surfaces.Length);
            Assert.AreEqual(9, snapshot.items.Length);
            for (int i = 0; i < snapshot.stations.Length; i++)
            {
                Assert.AreEqual(i, snapshot.stations[i].oldIndex);
                Assert.AreEqual(snapshot.stations[i].oldIndex + 1, snapshot.stations[i].piece, "a station's piece id is its old index + 1");
            }
            Assert.AreEqual(56, snapshot.slots.Length);
            Assert.AreEqual(13, snapshot.buttons.Length);
            Assert.AreEqual(11, snapshot.nested.Length, "2 baskets, 6 griddle spots, the build spot, the spit's cone and tray");
            Assert.AreEqual(6, snapshot.bodies.Length, "4 worktops and the 2 front counter runs");
            for (int i = 0; i < snapshot.surfaces.Length; i++) Assert.AreEqual(i, snapshot.surfaces[i].oldIndex);
        }

        [Test]
        public void EveryStation_IsWhereItWas()
        {
            KitchenSnapshot snapshot = KitchenSnapshot.Load();
            Scene scene = LayoutScene.Open();
            try
            {
                // Spec 057: the goal board is new (not in the old kitchen's snapshot); every other station is where it was.
                Assert.AreEqual(snapshot.stations.Length, LayoutRegistry.Stations.Count(s => s.Kind != StationKind.GoalBoard), "as many stations");
                Assert.AreEqual(1, LayoutRegistry.Stations.Count(s => s.Kind == StationKind.GoalBoard), "and the goal board");
                Assert.AreEqual(snapshot.stations.Length + 1, All<Station>(scene).Count, "and none outside the registry");
                foreach (KitchenSnapshot.StationEntry e in snapshot.stations)
                {
                    Station s = LayoutRegistry.Station(e.piece);
                    Assert.IsNotNull(s, e.name + " (piece " + e.piece + ")");
                    Assert.AreEqual(e.piece, s.Id);
                    Assert.AreEqual(e.name, s.name, "name of piece " + e.piece);
                    Assert.AreEqual(e.name, s.DisplayName, "display name of piece " + e.piece);
                    Assert.AreEqual(e.kind, s.Kind.ToString(), e.name);
                    Near(e.position, s.transform.position, Mm, e.name);
                    Yaw(e.yaw, s.transform.eulerAngles.y, e.name);
                    Bounds body = s.transform.Find("Body").GetComponent<BoxCollider>().bounds;
                    Near(e.size, body.size, Mm, e.name + " size");
                    Assert.AreEqual(e.top, body.max.y, Mm, e.name + " top");
                    Assert.AreEqual(e.slots, s.SlotCount, e.name + " slots");
                }
            }
            finally { LayoutScene.Close(scene); }
        }

        [Test]
        public void EverySurface_IsWhereItWas_ByItsPieceAndLocalNumber()
        {
            KitchenSnapshot snapshot = KitchenSnapshot.Load();
            Scene scene = LayoutScene.Open();
            try
            {
                Assert.AreEqual(snapshot.surfaces.Length, LayoutRegistry.Surfaces.Count, "as many surfaces");
                Assert.AreEqual(snapshot.surfaces.Length, All<PlaceSurface>(scene).Count, "and none outside the registry");
                foreach (KitchenSnapshot.SurfaceEntry e in snapshot.surfaces)
                {
                    PlaceSurface s = LayoutRegistry.Surface(SurfaceId.Of(e.piece, e.local));
                    Assert.IsNotNull(s, e.name + " (piece " + e.piece + ", local " + e.local + ")");
                    string what = e.name + " (old index " + e.oldIndex + ")";
                    Name(e.name, s.name, what);
                    Assert.AreEqual(e.kind, s.IsBurner ? "burner" : s.IsCupSpot ? "cup" : "top", what);
                    Near(e.position, s.transform.position, Mm, what);
                    Yaw(e.yaw, s.transform.eulerAngles.y, what);
                    Assert.AreEqual(e.size.x, s.Size.x, Mm, what + " width");
                    Assert.AreEqual(e.size.y, s.Size.y, Mm, what + " depth");
                    Assert.AreEqual(e.top, s.SpotWorldPosition(0).y, Mm, what + " top");
                    Assert.AreEqual(e.spots, s.SpotCount, what + " spots");
                    Assert.AreEqual(e.burner, s.Burner, what + " burner");
                    Assert.AreEqual(e.tap, s.CupTap, what + " tap");
                    // The stove's or dispenser's old station index is its piece id - 1.
                    int owner = s.IsBurner ? s.StovePieceId : s.IsCupSpot ? s.DispenserPieceId : -1;
                    Assert.AreEqual(e.station < 0 ? -1 : e.station + 1, owner, what + " station");
                    for (int spot = 0; spot < s.SpotCount; spot++) Assert.IsTrue(s.SpotWorldPosition(spot).y > 0.5f);
                }
            }
            finally { LayoutScene.Close(scene); }
        }

        [Test]
        public void TheItems_LieOnTheirOldHomes()
        {
            KitchenSnapshot snapshot = KitchenSnapshot.Load();
            Scene scene = LayoutScene.Open();
            try
            {
                List<Pickup> items = All<Pickup>(scene).Where(p => p.GetComponent<Vessel>() != null || p.GetComponent<Knife>() != null
                    || p.GetComponent<Spatula>() != null || p.GetComponent<WipeCloth>() != null).ToList();
                Assert.AreEqual(snapshot.items.Length, items.Count, "the cookware and tools at the start");
                foreach (KitchenSnapshot.ItemEntry e in snapshot.items)
                {
                    Pickup item = items.FirstOrDefault(p => p.name == e.name);
                    Assert.IsNotNull(item, e.name);
                    Near(e.position, item.transform.position, Mm, e.name);
                    Yaw(e.yaw, item.transform.eulerAngles.y, e.name);
                }
            }
            finally { LayoutScene.Close(scene); }
        }

        [Test]
        public void EverySlotAndButton_IsWhereItWas()
        {
            KitchenSnapshot snapshot = KitchenSnapshot.Load();
            Scene scene = LayoutScene.Open();
            try
            {
                // Spec 057: the goal board's rows are new slots (not in the snapshot).
                List<StockSlot> slots = All<StockSlot>(scene).Where(s => s.Station == null || s.Station.Kind != StationKind.GoalBoard).ToList();
                Assert.AreEqual(snapshot.slots.Length, slots.Count, "as many slots");
                foreach (KitchenSnapshot.PointEntry e in snapshot.slots)
                {
                    StockSlot slot = slots.Find(x => x.Station != null && x.Station.Id == e.piece && x.Option == e.option && x.name == e.name);
                    Assert.IsNotNull(slot, e.name + " of piece " + e.piece);
                    Near(e.position, slot.Anchor, Mm, e.name + " of " + slot.Station.name);
                }
                List<StationButton> buttons = All<StationButton>(scene);
                Assert.AreEqual(snapshot.buttons.Length, buttons.Count, "as many buttons");
                foreach (KitchenSnapshot.PointEntry e in snapshot.buttons)
                {
                    StationButton button = buttons.Find(b => b.Station != null && b.Station.Id == e.piece && b.Option == e.option);
                    Assert.IsNotNull(button, e.name + " of piece " + e.piece);
                    Near(e.position, button.transform.position, Mm, e.name + " of " + button.Station.name);
                }
            }
            finally { LayoutScene.Close(scene); }
        }

        [Test]
        public void TheNestedObjects_AreWhereTheyWere_InsideTheirPiece()
        {
            KitchenSnapshot snapshot = KitchenSnapshot.Load();
            Scene scene = LayoutScene.Open();
            try
            {
                var found = new List<(string Kind, Component C, Station Owner)>();
                foreach (FryerBasket x in All<FryerBasket>(scene)) found.Add(("FryerBasket", x, x.Fryer));
                foreach (GriddleSpot x in All<GriddleSpot>(scene)) found.Add(("GriddleSpot", x, x.Griddle));
                foreach (AssemblySpot x in All<AssemblySpot>(scene)) found.Add(("AssemblySpot", x, x.Board));
                foreach (SpitPart x in All<SpitPart>(scene)) found.Add(("SpitPart", x, x.Spit));
                Assert.AreEqual(snapshot.nested.Length, found.Count);
                foreach (KitchenSnapshot.NestedEntry e in snapshot.nested)
                {
                    Component c = found.Find(f => f.Kind == e.kind && f.C.name == e.name && f.Owner != null && f.Owner.Id == e.piece).C;
                    Assert.IsNotNull(c, e.kind + " " + e.name + " of piece " + e.piece);
                    Assert.AreSame(LayoutRegistry.Piece(e.piece), c.GetComponentInParent<PieceBehaviour>(), e.name + " is nested in its station's piece");
                    Near(e.position, c.transform.position, Mm, e.name);
                    Yaw(e.yaw, c.transform.eulerAngles.y, e.name);
                }
            }
            finally { LayoutScene.Close(scene); }
        }

        [Test]
        public void TheCountersBodies_CoverWhatTheyCovered()
        {
            KitchenSnapshot snapshot = KitchenSnapshot.Load();
            Scene scene = LayoutScene.Open();
            try
            {
                foreach (KitchenSnapshot.BodyEntry e in snapshot.bodies.Where(b => b.piece > 0))
                {
                    PieceBehaviour worktop = LayoutRegistry.Piece(e.piece);
                    Assert.IsNotNull(worktop, e.name);
                    Bounds b = worktop.Surfaces[0].GetComponent<BoxCollider>().bounds;
                    Near(e.center, b.center, Mm, e.name + " body centre");
                    Near(e.size, b.size, Mm, e.name + " body size");
                }
                // The builder's two front counter runs are now front counter pieces and the modules' counter bodies: the same height and
                // depth, and along the run nothing uncovered but the gaps under 20 mm the layout leaves (spec 062a notes).
                List<BoxCollider> front = All<BoxCollider>(scene).FindAll(c => c.name.StartsWith("Front counter"));
                Assert.AreEqual(9, front.Count);
                foreach (KitchenSnapshot.BodyEntry e in snapshot.bodies.Where(b => b.piece < 0))
                {
                    var run = new Bounds(e.center, e.size);
                    var covered = new List<(float Min, float Max)>();
                    foreach (BoxCollider c in front)
                    {
                        Bounds b = c.bounds;
                        if (b.max.x <= run.min.x + 0.0005f || b.min.x >= run.max.x - 0.0005f) continue;
                        Assert.AreEqual(run.min.z, b.min.z, Mm, c.name + " depth");
                        Assert.AreEqual(run.max.z, b.max.z, Mm, c.name + " depth");
                        Assert.AreEqual(run.max.y, b.max.y, Mm, c.name + " height");
                        covered.Add((Mathf.Max(b.min.x, run.min.x), Mathf.Min(b.max.x, run.max.x)));
                    }
                    covered.Sort((x, y) => x.Min.CompareTo(y.Min));
                    float at = run.min.x;
                    foreach ((float min, float max) in covered)
                    {
                        Assert.Less(min - at, 0.021f, e.name + ": a gap at x " + at.ToString("F3"));
                        at = Mathf.Max(at, max);
                    }
                    Assert.Less(run.max.x - at, 0.021f, e.name + ": a gap at its end");
                }
            }
            finally { LayoutScene.Close(scene); }
        }

        [Test]
        public void TheAnchors_DidNotMove()
        {
            KitchenSnapshot snapshot = KitchenSnapshot.Load();
            Scene scene = LayoutScene.Open();
            try
            {
                List<Transform> all = All<Transform>(scene);
                foreach (KitchenSnapshot.AnchorEntry e in snapshot.anchors)
                {
                    Transform t = all.FirstOrDefault(x => x.name == e.name);
                    Assert.IsNotNull(t, e.name);
                    Near(e.position, t.position, Mm, e.name);
                    Yaw(e.yaw, t.eulerAngles.y, e.name);
                }
            }
            finally { LayoutScene.Close(scene); }
        }
    }
}
