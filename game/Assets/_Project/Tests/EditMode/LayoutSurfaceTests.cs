using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Worktown.Game.Net;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 062a AC9: the layout adds no RPC and no broadcast to the wire, only the piece's three synced values (its id, its packed pose,
    /// its number); RequestPlace and RequestStation keep their int parameters (their meaning changed: Revision 5).
    /// </summary>
    public class LayoutSurfaceTests
    {
        [Test]
        public void TheSurface_GrowsOnlyByThePiecesThreeSyncVars()
        {
            List<string> lines = ProtocolSurface.Lines(typeof(NetBootstrap).Assembly);
            List<string> layout = lines.Where(l => l.Contains("Worktown.Game.Layout.")).ToList();
            CollectionAssert.AreEquivalent(new[]
            {
                "sync Worktown.Game.Layout.PieceBehaviour._pieceId:FishNet.Object.Synchronizing.SyncVar`1<System.Int32>",
                "sync Worktown.Game.Layout.PieceBehaviour._posePacked:FishNet.Object.Synchronizing.SyncVar`1<System.Int64>",
                "sync Worktown.Game.Layout.PieceBehaviour._number:FishNet.Object.Synchronizing.SyncVar`1<System.Byte>",
            }, layout, "no RPC, no broadcast: three synced fields");
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
            Assert.Contains("revision 5", lines);
        }
    }
}
