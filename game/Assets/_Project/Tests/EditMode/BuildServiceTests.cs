using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Layout;
using Worktown.Game.Net;
using Worktown.Sim.Layout;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 062b AC4: the server's build mode decisions (BuildDesk, which BuildService runs for every intent) on the default kitchen: claims
    /// by two players, a move into an overlap refused with its problem, a hot stove's claim refused, a required piece not stored, an undo
    /// blocked by another player, a disconnect's release; and in the built scene, a moved worktop's surfaces keep their ids and spots and
    /// carry what lies on them.
    /// </summary>
    public class BuildServiceTests
    {
        private const byte Host = 1, Guest = 2;

        private static BuildDesk Desk()
        {
            var desk = new BuildDesk(PieceCatalog.CreateDefault(), RoomDef.CreateDefault());
            desk.Reset(DefaultLayout.Load());
            Assert.IsTrue(LayoutRules.Validate(desk.Layout, desk.Room, desk.Catalog).IsValid, "the default kitchen keeps every rule");
            return desk;
        }

        private static int IdOf(BuildDesk desk, string type, int nth = 0) => desk.Layout.Pieces.Where(p => p.TypeId == type).ElementAt(nth).Id;

        [Test]
        public void TwoPlayers_CannotHoldTheSamePiece_AndEachHoldsOne()
        {
            BuildDesk desk = Desk();
            int fryer = IdOf(desk, PieceCatalog.Fryer), sink = IdOf(desk, PieceCatalog.Sink);
            Assert.AreEqual(BuildOutcome.Ok, desk.TryClaim(Host, fryer, false, 0, out _, out _));
            Assert.AreEqual(BuildOutcome.Claimed, desk.TryClaim(Guest, fryer, false, 0, out _, out _));
            Assert.AreEqual(BuildOutcome.Ok, desk.TryClaim(Guest, sink, false, 0, out _, out _));
            Assert.AreEqual(Host, desk.HolderOf(fryer));
            // One claim per player: picking up another lets the first go.
            int griddle = IdOf(desk, PieceCatalog.Griddle);
            Assert.AreEqual(BuildOutcome.Ok, desk.TryClaim(Host, griddle, false, 0, out int released, out bool fromStorage));
            Assert.AreEqual(fryer, released);
            Assert.IsFalse(fromStorage);
            Assert.AreEqual(0, desk.HolderOf(fryer));
            Assert.AreEqual(BuildOutcome.NotMovable, desk.TryClaim(Host, IdOf(desk, PieceCatalog.Bin), false, 0, out _, out _));
            Assert.AreEqual(BuildOutcome.Unknown, desk.TryClaim(Host, 9999, false, 0, out _, out _));
        }

        [Test]
        public void AMoveIntoAnOverlap_IsRefused_WithTheProblem()
        {
            BuildDesk desk = Desk();
            ShopLayout before = desk.Layout;
            int fryer = IdOf(desk, PieceCatalog.Fryer);
            LayoutPiece stove = desk.Layout.Piece(IdOf(desk, PieceCatalog.Stove));
            Assert.AreEqual(BuildOutcome.Ok, desk.TryClaim(Host, fryer, false, 0, out _, out _));
            BuildOutcome outcome = desk.TryMove(Host, stove.XMm, stove.ZMm, 0, 1, out LayoutEdit applied, out LayoutProblem problem);
            Assert.AreEqual(BuildOutcome.Invalid, outcome);
            Assert.IsNull(applied);
            Assert.IsNotNull(problem);
            Assert.AreEqual(LayoutProblemKind.Overlap, problem.Kind, problem.Text);
            Assert.AreEqual(before, desk.Layout, "nothing changed");
            Assert.AreEqual(Host, desk.HolderOf(fryer), "still in the hands");
            Assert.AreEqual(BuildOutcome.Invalid, BuildDesk.Unpack(BuildDesk.Pack(outcome, problem), out LayoutProblemKind? kind));
            Assert.AreEqual(LayoutProblemKind.Overlap, kind);
        }

        [Test]
        public void AHotStove_RefusesTheClaim()
        {
            BuildDesk desk = Desk();
            int stove = IdOf(desk, PieceCatalog.Stove);
            Assert.AreEqual(BuildOutcome.StationOn, desk.TryClaim(Host, stove, true, 0, out _, out _));
            Assert.AreEqual(0, desk.HolderOf(stove));
            Assert.AreEqual("Turn it off first", BuildControlRules.RefusalText(BuildOutcome.StationOn, null));
        }

        [Test]
        public void ARequiredPiece_CannotBeStored_AnOptionalOneCan_AndComesBack()
        {
            BuildDesk desk = Desk();
            int sink = IdOf(desk, PieceCatalog.Sink);
            Assert.AreEqual(BuildOutcome.Ok, desk.TryClaim(Host, sink, false, 0, out _, out _));
            Assert.AreEqual(BuildOutcome.Required, desk.TryStore(Host, 0, out LayoutEdit none, out _));
            Assert.IsNull(none);
            Assert.IsNotNull(desk.Layout.Piece(sink));
            int worktop = IdOf(desk, PieceCatalog.Worktop, 3);
            LayoutPiece was = desk.Layout.Piece(worktop);
            Assert.AreEqual(BuildOutcome.Ok, desk.TryClaim(Host, worktop, false, 0, out _, out _));
            Assert.AreEqual(BuildOutcome.Ok, desk.TryStore(Host, 0, out LayoutEdit stored, out _));
            Assert.AreEqual(LayoutEditKind.Store, stored.Kind);
            Assert.IsNull(desk.Layout.Piece(worktop));
            CollectionAssert.Contains(desk.Layout.Stored, worktop);
            // Taken out and placed where it was: the kitchen is the default again.
            Assert.AreEqual(BuildOutcome.Ok, desk.TryTakeOut(Guest, 0, 0, out int taken, out _, out _));
            Assert.AreEqual(worktop, taken);
            Assert.AreEqual(BuildOutcome.Ok, desk.TryMove(Guest, was.XMm, was.ZMm, was.Turns, 0, out LayoutEdit placed, out LayoutProblem p), p?.Text);
            Assert.AreEqual(LayoutEditKind.TakeOut, placed.Kind);
            Assert.IsNotNull(desk.Layout.Piece(worktop));
            CollectionAssert.IsEmpty(desk.Layout.Stored);
            Assert.AreEqual(BuildOutcome.NothingStored, desk.TryTakeOut(Guest, 0, 0, out _, out _, out _));
        }

        [Test]
        public void TwoWorktops_SwapThroughThePallet_AndTheMagnetKeepsTheLine()
        {
            BuildDesk desk = Desk();
            int a = IdOf(desk, PieceCatalog.Worktop, 1), b = IdOf(desk, PieceCatalog.Worktop, 3); // both 740 mm long
            LayoutPiece pa = desk.Layout.Piece(a), pb = desk.Layout.Piece(b);
            Assert.AreEqual(pa.LengthMm, pb.LengthMm);
            Assert.AreEqual(BuildOutcome.Ok, desk.TryClaim(Guest, b, false, 0, out _, out _));
            Assert.AreEqual(BuildOutcome.Ok, desk.TryStore(Guest, 0, out _, out _));
            Assert.AreEqual(BuildOutcome.Ok, desk.TryClaim(Guest, a, false, 0, out _, out _));
            Assert.AreEqual(BuildOutcome.Ok, desk.TryMove(Guest, pb.XMm, pb.ZMm, 0, 0, out LayoutEdit moved, out LayoutProblem p1), p1?.Text);
            Assert.AreEqual(BuildOutcome.Ok, desk.TryTakeOut(Guest, 0, 0, out _, out _, out _));
            Assert.AreEqual(BuildOutcome.Ok, desk.TryMove(Guest, pa.XMm, pa.ZMm, 0, 0, out _, out LayoutProblem p2), p2?.Text);
            Assert.AreEqual(pb.ZMm, desk.Layout.Piece(a).ZMm, "on the back line (the magnet)");
            Assert.AreEqual(pa.ZMm, desk.Layout.Piece(b).ZMm);
            Assert.Less(Mathf.Abs(desk.Layout.Piece(a).XMm - pb.XMm), 250, "near the other's place");
            Assert.AreEqual(3, desk.HistoryCount(Guest), "store, move, take out");
            Assert.IsTrue(LayoutRules.Validate(desk.Layout, desk.Room, desk.Catalog).IsValid);
        }

        [Test]
        public void AnUndo_AfterAnotherPlayerBlockedTheSpot_IsRefused()
        {
            BuildDesk desk = Desk();
            int worktop = IdOf(desk, PieceCatalog.Worktop, 3);
            LayoutPiece was = desk.Layout.Piece(worktop);
            Assert.AreEqual(BuildOutcome.Ok, desk.TryClaim(Host, worktop, false, 0, out _, out _));
            Assert.AreEqual(BuildOutcome.Ok, desk.TryStore(Host, 0, out _, out _));
            // The guest takes it out and puts it back where it stood: the host's undo (take it out to that place) no longer fits.
            Assert.AreEqual(BuildOutcome.Ok, desk.TryTakeOut(Guest, 0, 0, out _, out _, out _));
            Assert.AreEqual(BuildOutcome.Ok, desk.TryMove(Guest, was.XMm, was.ZMm, 0, 0, out _, out _));
            Assert.AreEqual(BuildOutcome.Blocked, desk.TryUndo(Host, 0, out LayoutEdit applied, out _, out _, out _));
            Assert.IsNull(applied);
            Assert.AreEqual(1, desk.HistoryCount(Host), "kept for a later try");
            Assert.AreEqual("Something is in the way now", BuildControlRules.RefusalText(BuildOutcome.Blocked, null));
            // The guest's own undo works (back into storage), and then the host's fits again.
            Assert.AreEqual(BuildOutcome.Ok, desk.TryUndo(Guest, 0, out LayoutEdit back, out _, out _, out _));
            Assert.AreEqual(LayoutEditKind.Store, back.Kind);
            Assert.AreEqual(BuildOutcome.Ok, desk.TryUndo(Host, 0, out LayoutEdit hostBack, out _, out _, out _));
            Assert.AreEqual(LayoutEditKind.TakeOut, hostBack.Kind);
            Assert.AreEqual(DefaultLayout.Load().Pieces, desk.Layout.Pieces, "the default kitchen again");
            Assert.AreEqual(BuildOutcome.NothingToUndo, desk.TryUndo(Host, 0, out _, out _, out _, out _));
        }

        [Test]
        public void ADisconnect_ReleasesTheClaim_AndAClaimLapsesAfter60s()
        {
            BuildDesk desk = Desk();
            int fryer = IdOf(desk, PieceCatalog.Fryer);
            Assert.AreEqual(BuildOutcome.Ok, desk.TryClaim(Guest, fryer, false, 10, out _, out _));
            Assert.AreEqual(fryer, desk.Release(Guest, out bool fromStorage)); // what BuildService does on the disconnect
            Assert.IsFalse(fromStorage);
            Assert.AreEqual(0, desk.HolderOf(fryer));
            Assert.AreEqual(BuildOutcome.Ok, desk.TryClaim(Host, fryer, false, 10, out _, out _));
            CollectionAssert.IsEmpty(desk.Lapsed(69));
            desk.Touch(Host, 30);
            CollectionAssert.IsEmpty(desk.Lapsed(89));
            CollectionAssert.AreEqual(new[] { Host }, desk.Lapsed(90));
        }

        [Test]
        public void TheGate_WantsAClosedShop_TheStage_AndEmptyHands()
        {
            Assert.AreEqual(BuildOutcome.Busy, BuildDesk.Gate(true, ShopDoorState.Closed, true, true));
            Assert.AreEqual(BuildOutcome.NotClosed, BuildDesk.Gate(false, ShopDoorState.Open, true, true));
            Assert.AreEqual(BuildOutcome.NotClosed, BuildDesk.Gate(false, ShopDoorState.ClosingAfterService, true, true));
            Assert.AreEqual(BuildOutcome.NotBuilding, BuildDesk.Gate(false, ShopDoorState.Closed, false, true));
            Assert.AreEqual(BuildOutcome.NotBuilding, BuildDesk.Gate(false, ShopDoorState.Closed, true, false));
            Assert.IsNull(BuildDesk.Gate(false, ShopDoorState.Closed, true, true));
            Assert.AreEqual(0, BuildService.SlotOf(null), "no connection: slot 0");
        }

        /// <summary>
        /// In the built scene: a worktop moved along the back line keeps its surfaces' ids and spot counts, the spots move with it, and the
        /// carry BuildService does (the item's pose in the piece's frame) puts the knife on the same spot of the moved top.
        /// </summary>
        [Test]
        public void ItemsOnAMovedWorktop_KeepTheirSurfaceIdAndSpot()
        {
            var scene = LayoutScene.Open();
            try
            {
                ShopLayout layout = DefaultLayout.Load();
                ItemHome home = layout.Homes.First(h => h.ItemKind == "knife");
                PieceBehaviour worktop = LayoutRegistry.Piece(home.PieceId);
                Assert.IsNotNull(worktop);
                PlaceSurface top = LayoutRegistry.Surface(SurfaceId.Of(home.PieceId, home.LocalSurface));
                int idBefore = top.Id, spots = top.SpotCount;
                Vector3 spotBefore = top.SpotWorldPosition(home.Spot);
                Pickup knife = Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None).First(p => p.GetComponent<Knife>() != null);
                Matrix4x4 local = worktop.transform.worldToLocalMatrix * knife.transform.localToWorldMatrix;

                var desk = new BuildDesk(PieceCatalog.CreateDefault(), RoomDef.CreateDefault());
                desk.Reset(layout);
                LayoutPiece other = desk.Layout.Pieces.Where(p => p.TypeId == PieceCatalog.Worktop).ElementAt(3);
                Assert.AreEqual(BuildOutcome.Ok, desk.TryClaim(Guest, other.Id, false, 0, out _, out _));
                Assert.AreEqual(BuildOutcome.Ok, desk.TryStore(Guest, 0, out _, out _));
                Assert.AreEqual(BuildOutcome.Ok, desk.TryClaim(Guest, home.PieceId, false, 0, out _, out _));
                Assert.AreEqual(BuildOutcome.Ok, desk.TryMove(Guest, other.XMm, other.ZMm, 0, 0, out LayoutEdit moved, out LayoutProblem p), p?.Text);
                worktop.Configure(home.PieceId, PosePacking.Pack(moved.XMm, moved.ZMm, moved.Turns, desk.Layout.Piece(home.PieceId).LengthMm), worktop.Number);
                Physics.SyncTransforms();
                Matrix4x4 world = worktop.transform.localToWorldMatrix * local;
                knife.transform.SetPositionAndRotation(world.GetColumn(3), world.rotation);

                Assert.AreEqual(idBefore, top.Id, "the same surface id");
                Assert.AreEqual(spots, top.SpotCount, "the same spots");
                Vector3 spotAfter = top.SpotWorldPosition(home.Spot);
                Assert.AreEqual(spotBefore.z, spotAfter.z, 0.05f, "along the back line");
                Assert.Greater(Vector3.Distance(spotBefore, spotAfter), 1f, "the spot moved with the worktop");
                Assert.Less(Vector3.ProjectOnPlane(knife.transform.position - spotAfter, Vector3.up).magnitude, 0.1f, "the knife lies on the same spot");
            }
            finally
            {
                LayoutScene.Close(scene);
            }
        }
    }
}
