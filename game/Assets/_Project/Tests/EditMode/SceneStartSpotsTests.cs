using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 045: a server restart puts the scene plates back at their start spots (SceneStartSpots, used by KitchenService).</summary>
    public class SceneStartSpotsTests
    {
        private sealed class Plate
        {
            public Vector3 Position;
            public Quaternion Rotation = Quaternion.identity;
            public bool Gone;
        }

        private static void Move(Plate p, Vector3 position, Quaternion rotation)
        {
            p.Position = position;
            p.Rotation = rotation;
        }

        [Test]
        public void ARestart_PutsEveryScenePlateBack_WhereItStoodAtTheFirstStart()
        {
            var spots = new SceneStartSpots<Plate>();
            var a = new Plate { Position = new Vector3(1f, 0.9f, 2f) };
            var b = new Plate { Position = new Vector3(1.4f, 0.9f, 2f), Rotation = Quaternion.Euler(0f, 90f, 0f) };
            Assert.IsTrue(spots.Record(a, a.Position, a.Rotation));
            Assert.IsTrue(spots.Record(b, b.Position, b.Rotation));
            spots.Seal();

            // The session: one plate served onto the pass, the other carried to the counter.
            Vector3 aStart = a.Position, bStart = b.Position;
            Quaternion bTurn = b.Rotation;
            Move(a, new Vector3(-4f, 1f, 0f), Quaternion.Euler(0f, 30f, 0f));
            Move(b, new Vector3(6f, 1.1f, 3f), Quaternion.identity);

            Assert.AreEqual(2, spots.ResetAll(p => !p.Gone, Move), "the server stopped");
            Assert.AreEqual(aStart, a.Position);
            Assert.AreEqual(Quaternion.identity, a.Rotation);
            Assert.AreEqual(bStart, b.Position);
            Assert.AreEqual(bTurn, b.Rotation);
        }

        [Test]
        public void OnlyTheFirstStart_Records_AndAPlateKeepsItsFirstSpot()
        {
            var spots = new SceneStartSpots<Plate>();
            var a = new Plate { Position = Vector3.one };
            Assert.IsTrue(spots.Record(a, Vector3.one, Quaternion.identity));
            Assert.IsFalse(spots.Record(a, Vector3.zero, Quaternion.identity), "a second scan of the first start");
            Assert.IsFalse(spots.Record(null, Vector3.zero, Quaternion.identity));
            spots.Seal();
            var late = new Plate();
            Assert.IsFalse(spots.Record(late, new Vector3(9f, 0f, 9f), Quaternion.identity), "a restart's scan records nothing");
            Assert.AreEqual(1, spots.Count);
            a.Position = new Vector3(5f, 5f, 5f);
            spots.ResetAll(null, Move);
            Assert.AreEqual(Vector3.one, a.Position, "the scene's spot, not where the restart found it");
        }

        [Test]
        public void AGonePlate_IsSkipped_AndNoMoveDoesNothing()
        {
            var spots = new SceneStartSpots<Plate>();
            var a = new Plate { Position = Vector3.one };
            var gone = new Plate { Position = Vector3.right, Gone = true };
            spots.Record(a, a.Position, a.Rotation);
            spots.Record(gone, gone.Position, gone.Rotation);
            spots.Seal();
            var moved = new List<Plate>();
            Assert.AreEqual(1, spots.ResetAll(p => !p.Gone, (p, _, _) => moved.Add(p)));
            CollectionAssert.AreEqual(new[] { a }, moved);
            Assert.AreEqual(0, spots.ResetAll(p => true, null));
        }
    }
}
