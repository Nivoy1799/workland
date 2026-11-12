using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Layout;
using Worktown.Game.Npc;
using Worktown.Sim.Layout;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 062a AC7: the guests' route comes from the layout. The default layout gives today's anchors (the parity snapshot's entry, queue
    /// and pay transforms within 10 mm) and today's straight lines (no corners); the serving counter 1 m west moves the queue head 1 m west;
    /// a block in the guest area gives a path around it. The default room is the built scene's shell.
    /// </summary>
    public class NpcRoutesTests
    {
        private const float Mm10 = 0.010f;

        private static NpcRoute DefaultRoute(ShopLayout layout = null) =>
            NpcRoutes.Build(layout ?? DefaultLayout.Load(), RoomDef.CreateDefault(), PieceCatalog.CreateDefault());

        private static KitchenSnapshot.AnchorEntry Anchor(string name) => KitchenSnapshot.Load().anchors.Single(a => a.name == name);

        private static void Near(Vector3 expected, Vector3 actual, string what)
        {
            Assert.AreEqual(expected.x, actual.x, Mm10, what + " x");
            Assert.AreEqual(expected.z, actual.z, Mm10, what + " z");
            Assert.AreEqual(0f, actual.y, 0.0001f, what + " on the ground");
        }

        [Test]
        public void TheDefaultRoute_IsTodaysAnchorsAndStraightLines()
        {
            NpcRoute route = DefaultRoute();
            Assert.IsNotNull(route);
            Near(Anchor("StreetEntry").position, route.Entry, "the entry");
            Assert.AreEqual(Anchor("StreetEntry").yaw, route.EntryYaw, 0.1f, "the entry's yaw (towards the queue)");
            Near(Anchor("QueueHead").position, route.QueueHead, "the queue head");
            Near(Anchor("PayHead").position, route.PayHead, "the pay head");
            Assert.AreEqual(Vector3.left, route.QueueDirection, "the builder's _queueDirection");
            Assert.AreEqual(Vector3.forward, route.QueueFacing, "the builder's _queueFacing");
            Assert.AreEqual(Vector3.right, route.PayDirection, "the builder's _payDirection");
            Assert.IsTrue(route.Walkable);
            Assert.IsEmpty(route.ArriveVia, "straight in, as today");
            Assert.IsEmpty(route.LeaveVia, "straight out, as today");
        }

        [Test]
        public void TheServingCounter_OneMetreWest_MovesTheQueueHeadOneMetreWest()
        {
            ShopLayout standard = DefaultLayout.Load();
            LayoutPiece counter = standard.Pieces.Single(p => p.TypeId == PieceCatalog.ServingCounter);
            ShopLayout moved = standard.WithPieces(standard.Pieces.Select(p => p.Id == counter.Id ? p with { XMm = p.XMm - 1000 } : p));
            NpcRoute before = DefaultRoute(standard), after = DefaultRoute(moved);
            Near(before.QueueHead + Vector3.left, after.QueueHead, "the queue head");
            Near(before.PayHead, after.PayHead, "the register did not move");
            Assert.IsTrue(after.Walkable);
            Assert.IsEmpty(after.ArriveVia, "still a straight line along the street");
        }

        [Test]
        public void ABlockInTheGuestArea_GivesAPathAroundIt()
        {
            ShopLayout standard = DefaultLayout.Load();
            // A yard piece (the bin's size) on the guests' line, half way between the street entry and the counter.
            var block = new LayoutPiece(standard.NextId, PieceCatalog.Bin, 18000, 11100, 0, 0, 1, 0);
            ShopLayout blocked = standard.WithPieces(standard.Pieces.Concat(new[] { block }));
            NpcRoute route = DefaultRoute(blocked);
            Assert.IsTrue(route.Walkable);
            Assert.IsNotEmpty(route.ArriveVia, "corners round the block");
            Assert.IsNotEmpty(route.LeaveVia);
            Assert.IsTrue(PieceCatalog.CreateDefault().TryGet(PieceCatalog.Bin, out PieceDef def));
            RectMm rect = def.Footprint(block);
            var walk = new List<Vector3> { route.Entry };
            walk.AddRange(route.ArriveVia);
            walk.Add(route.QueueHead);
            Assert.IsTrue(walk.Any(p => p.z * 1000f < rect.MinZ - WalkGrid.ClearanceMm + 1), "the way in passes south of the block");
            for (int i = 1; i < walk.Count; i++)
                Assert.IsFalse(SegmentHitsRect(walk[i - 1], walk[i], rect), "leg " + i + " " + walk[i - 1] + " to " + walk[i] + " crosses the block");
            Assert.IsTrue(walk.All(p => p.z * 1000f <= RoomDef.CreateDefault().GuestArea.MaxZ), "guests stay in the guest area");
        }

        [Test]
        public void NoServingCounter_GivesNoRoute()
        {
            ShopLayout standard = DefaultLayout.Load();
            Assert.IsNull(DefaultRoute(standard.WithPieces(standard.Pieces.Where(p => p.TypeId != PieceCatalog.ServingCounter))));
            Assert.IsNull(NpcRoutes.Build(null, RoomDef.CreateDefault(), PieceCatalog.CreateDefault()));
        }

        [Test]
        public void InFront_AndAlong_FollowThePiecesTurns()
        {
            Assert.IsTrue(PieceCatalog.CreateDefault().TryGet(PieceCatalog.ServingCounter, out PieceDef def));
            Assert.AreEqual(new PointMm(1000, 250), NpcRoutes.InFront(new LayoutPiece(1, def.TypeId, 1000, 1000, 2, 0, 1, 0), def));
            Assert.AreEqual(new PointMm(1000, 1750), NpcRoutes.InFront(new LayoutPiece(1, def.TypeId, 1000, 1000, 0, 0, 1, 0), def));
            Assert.AreEqual(new PointMm(1750, 1000), NpcRoutes.InFront(new LayoutPiece(1, def.TypeId, 1000, 1000, 1, 0, 1, 0), def));
            Assert.AreEqual(Vector3.right, NpcRoutes.Along(0));
            Assert.AreEqual(Vector3.back, NpcRoutes.Along(1));
            Assert.AreEqual(Vector3.left, NpcRoutes.Along(2));
            Assert.AreEqual(Vector3.forward, NpcRoutes.Along(3));
            Assert.AreEqual(Vector3.forward, NpcRoutes.Along(-1));
            Assert.AreEqual(new Vector3(1.5f, 0f, -2f), NpcRoutes.World(new PointMm(1500, -2000)));
            // Along matches the piece's real rotation (PosePacking.Yaw).
            for (int t = 0; t < 4; t++)
            {
                Vector3 turned = Quaternion.Euler(0f, PosePacking.Yaw(t), 0f) * Vector3.right;
                Assert.AreEqual(0f, Vector3.Distance(turned, NpcRoutes.Along(t)), 0.0001f, "turns " + t);
                Vector3 cook = Quaternion.Euler(0f, PosePacking.Yaw(t), 0f) * Vector3.back;
                PointMm d = LayoutRules.CookDirection(t);
                Assert.AreEqual(0f, Vector3.Distance(cook, new Vector3(d.X, 0f, d.Z)), 0.0001f, "the cook side, turns " + t);
            }
        }

        [Test]
        public void Step_WalksThroughTheCornersFirst()
        {
            var via = new List<Vector3> { new Vector3(1f, 0f, 0f), new Vector3(1f, 0f, 1f) };
            Vector3 p = Customer.Step(Vector3.zero, new Vector3(2f, 0f, 1f), via, 0.5f);
            Assert.AreEqual(new Vector3(0.5f, 0f, 0f), p);
            Assert.AreEqual(2, via.Count);
            p = Customer.Step(p, new Vector3(2f, 0f, 1f), via, 1f); // the rest of the step turns the corner
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(1f, 0f, 0.5f), p), 0.0001f);
            Assert.AreEqual(1, via.Count);
            p = Customer.Step(p, new Vector3(2f, 0f, 1f), via, 10f);
            Assert.AreEqual(new Vector3(2f, 0f, 1f), p);
            Assert.AreEqual(0, via.Count);
            Assert.AreEqual(new Vector3(0.5f, 0f, 0f), Customer.Step(Vector3.zero, Vector3.right, null, 0.5f), "no corners: straight");
        }

        /// <summary>RoomDef.CreateDefault is the built scene's shell: the walls, spawn point 1 and the street entry.</summary>
        [Test]
        public void TheDefaultRoom_IsTheScenesShell()
        {
            RoomDef room = RoomDef.CreateDefault();
            Near(Anchor("StreetEntry").position, NpcRoutes.World(room.GuestEntry), "the guest entry");
            Vector3 spawn = Anchor("Spawn 1").position;
            Assert.AreEqual(spawn.x, room.StaffSpawn.X / 1000f, Mm10);
            Assert.AreEqual(spawn.z, room.StaffSpawn.Z / 1000f, Mm10);
            Scene scene = LayoutScene.Open();
            try
            {
                var walls = new List<Bounds>();
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (BoxCollider box in root.GetComponentsInChildren<BoxCollider>(true))
                        if (box.name.StartsWith("Back wall") || box.name.StartsWith("Side wall")) walls.Add(box.bounds);
                Assert.AreEqual(room.Walls.Count, walls.Count, "one rect per wall");
                foreach (RectMm w in room.Walls)
                    Assert.IsTrue(walls.Any(b => Mathf.Abs(b.min.x - w.MinX / 1000f) < Mm10 && Mathf.Abs(b.max.x - w.MaxX / 1000f) < Mm10
                        && Mathf.Abs(b.min.z - w.MinZ / 1000f) < Mm10 && Mathf.Abs(b.max.z - w.MaxZ / 1000f) < Mm10), "no scene wall at " + w);
                Assert.AreEqual(room.Floor.MinX / 1000f, walls.Min(b => b.max.x), Mm10, "the left wall's inner face is the floor's edge");
            }
            finally { LayoutScene.Close(scene); }
        }

        private static bool SegmentHitsRect(Vector3 a, Vector3 b, RectMm r)
        {
            // Sample the leg every 5 cm (the legs are axis-aligned or short, so this is exact enough for a 0.4 m clearance).
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 0.05f));
            for (int i = 0; i <= steps; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / (float)steps);
                if (r.Contains(Mathf.RoundToInt(p.x * 1000f), Mathf.RoundToInt(p.z * 1000f))) return true;
            }
            return false;
        }
    }
}
