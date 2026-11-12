using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Layout;
using Worktown.Sim.Layout;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 062a: each piece's catalog footprint is its prefab's: the bodies' colliders (a station's "Body", a worktop, a front counter run
    /// or a module's counter body) cover the row's width and depth around the piece's centre, turned with the piece, within 10 mm.
    /// </summary>
    public class PieceFootprintTests
    {
        private const float Tolerance = 0.010f;

        private static bool IsBody(BoxCollider box) =>
            box.name == "Body" || box.name.StartsWith("Worktop") || box.name.StartsWith("Front counter");

        [Test]
        public void EveryPiece_CoversItsCatalogFootprint()
        {
            PieceCatalog catalog = PieceCatalog.CreateDefault();
            ShopLayout layout = DefaultLayout.Load();
            Scene scene = LayoutScene.Open(layout);
            try
            {
                foreach (LayoutPiece p in layout.Pieces)
                {
                    Assert.IsTrue(catalog.TryGet(p.TypeId, out PieceDef def), p.TypeId);
                    PieceBehaviour piece = LayoutRegistry.Piece(p.Id);
                    BoxCollider[] bodies = piece.GetComponentsInChildren<BoxCollider>(true).Where(IsBody).ToArray();
                    Assert.IsNotEmpty(bodies, p.TypeId + ": a body collider");
                    Bounds b = bodies[0].bounds;
                    foreach (BoxCollider box in bodies) b.Encapsulate(box.bounds);
                    RectMm rect = def.Footprint(p);
                    string what = p.TypeId + " " + p.Id;
                    Assert.AreEqual(rect.MinX * 0.001f, b.min.x, Tolerance, what + " min x");
                    Assert.AreEqual(rect.MaxX * 0.001f, b.max.x, Tolerance, what + " max x");
                    Assert.AreEqual(rect.MinZ * 0.001f, b.min.z, Tolerance, what + " min z");
                    Assert.AreEqual(rect.MaxZ * 0.001f, b.max.z, Tolerance, what + " max z");
                }
            }
            finally { LayoutScene.Close(scene); }
        }

        [Test]
        public void EveryCatalogTypeOfTheDefault_HasAPrefab_AndTheReservedOnesAreNotPlaced()
        {
            PiecePrefabs prefabs = LayoutScene.Prefabs;
            ShopLayout layout = DefaultLayout.Load();
            foreach (LayoutPiece p in layout.Pieces) Assert.IsNotNull(prefabs.Piece(p.TypeId), p.TypeId);
            foreach (string reserved in PieceCatalog.ReservedTypes) Assert.AreEqual(0, layout.CountOf(reserved), reserved);
            foreach (PiecePrefabs.PieceEntry e in prefabs.PieceEntries)
            {
                Assert.AreEqual(e.TypeId, e.Prefab.TypeId, e.TypeId);
                Assert.IsTrue(e.Prefab.GetComponents<Component>().Any(c => c.GetType().Name == "NetworkObject"), e.TypeId + ": a NetworkObject root");
            }
            foreach (string kind in new[] { "board", "knife", "spatula", "pan", "pot", "plate", "bowl", "cloth" })
                Assert.IsNotNull(prefabs.Item(kind), kind);
        }
    }
}
