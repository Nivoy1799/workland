using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Layout;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 062a AC4: a surface id is the piece id shifted by 6 bits and the local number; PlaceTurn's spot bits still fit.</summary>
    public class SurfaceIdTests
    {
        [Test]
        public void Of_PacksPieceAndLocal_AndUnpacksThem()
        {
            Assert.AreEqual((7 << 6) | 3, SurfaceId.Of(7, 3));
            Assert.IsTrue(SurfaceId.TryUnpack(SurfaceId.Of(7, 3), out int piece, out int local));
            Assert.AreEqual(7, piece);
            Assert.AreEqual(3, local);
            Assert.AreEqual(7, SurfaceId.PieceOf(SurfaceId.Of(7, 3)));
            Assert.AreEqual(3, SurfaceId.LocalOf(SurfaceId.Of(7, 3)));
            Assert.AreEqual(SurfaceId.Of(1, 0) + 1, SurfaceId.Of(1, 1), "a piece's surfaces are consecutive ids");
        }

        [Test]
        public void None_AndOutOfRange()
        {
            Assert.AreEqual(-1, SurfaceId.None);
            Assert.AreEqual(SurfaceId.None, SurfaceId.Of(0, 0), "piece ids start at 1");
            Assert.AreEqual(SurfaceId.None, SurfaceId.Of(-3, 0));
            Assert.AreEqual(SurfaceId.None, SurfaceId.Of(1, 64), "64 surfaces at most per piece");
            Assert.AreEqual(SurfaceId.None, SurfaceId.Of(1, -1));
            Assert.AreEqual(SurfaceId.None, SurfaceId.Of(SurfaceId.MaxPiece + 1, 0));
            Assert.IsFalse(SurfaceId.TryUnpack(SurfaceId.None, out _, out _));
            Assert.IsFalse(SurfaceId.TryUnpack(5, out _, out _), "piece 0");
            Assert.AreEqual(0, SurfaceId.PieceOf(SurfaceId.None));
            Assert.AreEqual(-1, SurfaceId.LocalOf(SurfaceId.None));
        }

        [Test]
        public void TheLargestId_FitsAnInt_AndIsPositive()
        {
            int top = SurfaceId.Of(SurfaceId.MaxPiece, SurfaceId.MaxLocal);
            Assert.Greater(top, 0);
            Assert.AreEqual(int.MaxValue >> 1, top, "2^30 - 1");
            Assert.AreEqual(Worktown.Sim.Layout.ShopLayout.MaxPieceId, SurfaceId.MaxPiece);
            Assert.IsTrue(SurfaceId.TryUnpack(top, out int piece, out int local));
            Assert.AreEqual(SurfaceId.MaxPiece, piece);
            Assert.AreEqual(SurfaceId.MaxLocal, local);
        }

        /// <summary>PlaceTurn packs the spot in the low 16 bits of RequestPlace's spot int: every piece prefab's surfaces have fewer spots.</summary>
        [Test]
        public void EveryPrefabSurface_HasFewerSpotsThanPlaceTurnCanCarry()
        {
            PiecePrefabs prefabs = LayoutScene.Prefabs;
            Assert.Greater(prefabs.PieceEntries.Length, 10);
            foreach (PiecePrefabs.PieceEntry e in prefabs.PieceEntries)
            {
                Assert.LessOrEqual(e.Prefab.Surfaces.Count, SurfaceId.MaxLocal + 1, e.TypeId);
                foreach (PlaceSurface s in e.Prefab.Surfaces)
                {
                    if (s == null) continue;
                    Assert.Less(s.SpotCount, 1 << 16, e.TypeId + "/" + s.name);
                    Assert.Less(s.SpotCount, PlaceTurn.SpotMask + 1, e.TypeId + "/" + s.name);
                }
            }
            // The longest counters the catalog allows still fit.
            Assert.Less(PlaceSpots.Count(new Vector2(8f, 0.35f)), 1 << 16);
            Assert.Less(PlaceSpots.Count(new Vector2(6f, 1.2f)), 1 << 16);
        }
    }
}
