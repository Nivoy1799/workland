using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Worktown.Game.Net;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 062a AC9: the layout added no RPC and no broadcast to the wire, only the piece's three synced values (its id, its packed pose,
    /// its number); RequestPlace and RequestStation keep their int parameters (their meaning changed: Revision 5). Spec 062b AC7: build mode
    /// adds exactly one ServerRpc (RequestBuild), one TargetRpc (BuildAnswer), the door's state and the piece's claim; Revision 9 (spec 059
    /// took 8).
    /// </summary>
    public class LayoutSurfaceTests
    {
        [Test]
        public void TheSurface_GrowsOnlyByThePieceSyncVars_AndBuildModesLines()
        {
            List<string> lines = ProtocolSurface.Lines(typeof(NetBootstrap).Assembly);
            List<string> layout = lines.Where(l => l.Contains("Worktown.Game.Layout.")).ToList();
            CollectionAssert.AreEquivalent(new[]
            {
                "sync Worktown.Game.Layout.PieceBehaviour._pieceId:FishNet.Object.Synchronizing.SyncVar`1<System.Int32>",
                "sync Worktown.Game.Layout.PieceBehaviour._posePacked:FishNet.Object.Synchronizing.SyncVar`1<System.Int64>",
                "sync Worktown.Game.Layout.PieceBehaviour._number:FishNet.Object.Synchronizing.SyncVar`1<System.Byte>",
                // Spec 062b:
                "sync Worktown.Game.Layout.PieceBehaviour._claimedBySlot:FishNet.Object.Synchronizing.SyncVar`1<System.Byte>",
                "sync Worktown.Game.Layout.ShopDoor.State:FishNet.Object.Synchronizing.SyncVar`1<System.Byte>",
                "ServerRpc Worktown.Game.Layout.BuildInteractor.RequestBuild(Worktown.Game.Layout.BuildIntent,System.Int32)",
                "TargetRpc Worktown.Game.Layout.BuildInteractor.BuildAnswer(FishNet.Connection.NetworkConnection,System.Int32,System.Byte)",
            }, layout, string.Join("\n", layout));
            Assert.AreEqual(3, lines.Count(l => l.StartsWith("broadcast ", StringComparison.Ordinal)), "still the three connection messages");
        }

        [Test]
        public void PlaceAndStationRequests_KeepTheirIntParameters()
        {
            List<string> lines = ProtocolSurface.Lines(typeof(NetBootstrap).Assembly);
            Assert.AreEqual(1, lines.Count(l => l.EndsWith("Worktown.Game.Net.PickupInteractor.RequestPlace(System.Int32,System.Int32,System.Int32)", StringComparison.Ordinal)),
                string.Join("\n", lines.Where(l => l.Contains("RequestPlace"))));
            Assert.AreEqual(1, lines.Count(l => l.EndsWith("Worktown.Game.Net.PickupInteractor.RequestStation(System.Int32,System.Int32,System.Int32)", StringComparison.Ordinal)),
                string.Join("\n", lines.Where(l => l.Contains("RequestStation"))));
            Assert.Contains("revision 9", lines); // 062a made it 5, spec 054b 6, spec 057 7, spec 059 8, spec 062b 9
        }
    }
}
