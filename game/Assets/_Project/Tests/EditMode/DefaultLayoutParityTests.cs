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
    /// any piece placed more than once ("Worktop 3"), so only its form is compared.
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

        private static void Name(string expected, string actual, string what)
        {
            if (WorktopName.IsMatch(expected)) StringAssert.IsMatch(WorktopName.ToString(), actual, what);
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
            for (int i = 0; i < snapshot.stations.Length; i++) Assert.AreEqual(i, snapshot.stations[i].oldIndex);
            for (int i = 0; i < snapshot.surfaces.Length; i++) Assert.AreEqual(i, snapshot.surfaces[i].oldIndex);
        }

        [Test]
        public void EveryStation_IsWhereItWas()
        {
            KitchenSnapshot snapshot = KitchenSnapshot.Load();
            Scene scene = LayoutScene.Open();
            try
            {
                Assert.AreEqual(snapshot.stations.Length, LayoutRegistry.Stations.Count, "as many stations");
                Assert.AreEqual(snapshot.stations.Length, All<Station>(scene).Count, "and none outside the registry");
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
