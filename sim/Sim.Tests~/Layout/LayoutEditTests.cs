using Worktown.Sim.Layout;
using Xunit;

namespace Worktown.Sim.Tests.Layout
{
    /// <summary>Spec 062b AC2: a layout edit applied and then its inverse gives the original layout.</summary>
    public class LayoutEditTests
    {
        private static LayoutPiece P(int id, string type, int x, int z, int turns = 0, int length = 0) => new LayoutPiece(id, type, x, z, turns, length, 1, 0);

        private static ShopLayout Start() => new ShopLayout(1, new[] { P(1, "cabinet", 1300, 3800), P(2, "worktop", 2500, 3800, 0, 900) }, null,
            new[] { new ItemHome("knife", 2, 0, 3) }, 3);

        private static ShopLayout Twice(ShopLayout layout, LayoutEdit edit, LayoutEditKind inverseKind)
        {
            Assert.True(edit.TryApply(layout, out ShopLayout changed, out LayoutEdit inverse));
            Assert.NotEqual(layout, changed);
            Assert.Equal(inverseKind, inverse.Kind);
            Assert.True(inverse.TryApply(changed, out ShopLayout back, out LayoutEdit again));
            Assert.Equal(edit.Kind, again.Kind);
            return back;
        }

        [Fact]
        public void Move_ThenItsInverse_GivesTheOriginal()
        {
            Assert.Equal(Start(), Twice(Start(), LayoutEdit.Move(2, 4000, 3500, 5), LayoutEditKind.Move));
            Assert.True(LayoutEdit.Move(2, 4000, 3500, 5).TryApply(Start(), out ShopLayout moved, out _));
            Assert.Equal(P(2, "worktop", 4000, 3500, 1, 900), moved.Piece(2)); // turns wrap; the length stays
            Assert.Equal(Start().Homes, moved.Homes);
        }

        [Fact]
        public void Store_ThenItsInverse_GivesTheOriginal()
        {
            Assert.Equal(Start(), Twice(Start(), LayoutEdit.Store(2), LayoutEditKind.TakeOut));
            Assert.True(LayoutEdit.Store(2).TryApply(Start(), out ShopLayout stored, out LayoutEdit inverse));
            Assert.Null(stored.Piece(2));
            Assert.Equal(new[] { 2 }, stored.Stored);
            Assert.Equal(P(2, "worktop", 2500, 3800, 0, 900), stored.StoredPiece(2)); // the record keeps its last pose
            Assert.Equal(LayoutEdit.TakeOut(2, 2500, 3800, 0), inverse);
            Assert.Equal(3, stored.NextId);
        }

        [Fact]
        public void TakeOut_ThenItsInverse_GivesTheOriginal()
        {
            Assert.True(LayoutEdit.Store(2).TryApply(Start(), out ShopLayout stored, out _));
            ShopLayout back = Twice(stored, LayoutEdit.TakeOut(2, 5000, 1000, 2), LayoutEditKind.Store);
            // Back in storage; its record now remembers the pose it was stored from (the take-out's).
            Assert.Equal(stored.Pieces, back.Pieces);
            Assert.Equal(stored.Stored, back.Stored);
            Assert.Equal(P(2, "worktop", 5000, 1000, 2, 900), back.StoredPiece(2));
            Assert.True(LayoutEdit.TakeOut(2, 5000, 1000, 2).TryApply(stored, out ShopLayout placed, out _));
            Assert.Equal(P(2, "worktop", 5000, 1000, 2, 900), placed.Piece(2));
            Assert.Empty(placed.Stored);
            Assert.Empty(placed.StoredPieces);
        }

        [Fact]
        public void AnEditThatDoesNotFit_IsRefused()
        {
            Assert.False(LayoutEdit.Move(9, 0, 0, 0).TryApply(Start(), out ShopLayout none, out LayoutEdit noInverse));
            Assert.Null(none);
            Assert.Null(noInverse);
            Assert.False(LayoutEdit.Store(9).TryApply(Start(), out _, out _));
            Assert.False(LayoutEdit.TakeOut(1, 0, 0, 0).TryApply(Start(), out _, out _)); // placed, not stored
            var olderSave = new ShopLayout(1, null, new[] { 5 }, null, 6); // a stored id without a record cannot be taken out
            Assert.False(LayoutEdit.TakeOut(5, 0, 0, 0).TryApply(olderSave, out _, out _));
            Assert.False(LayoutEdit.Move(1, 0, 0, 0).TryApply(null!, out _, out _));
        }

        [Fact]
        public void StoredRecords_FollowTheStoredIds()
        {
            var layout = new ShopLayout(1, null, new[] { 7 }, null, 1, new[] { P(4, "cabinet", 0, 0), P(4, "cabinet", 9, 9) });
            Assert.Equal(new[] { 7, 4 }, layout.Stored);
            Assert.Equal(new[] { P(4, "cabinet", 0, 0) }, layout.StoredPieces);
            Assert.Null(layout.StoredPiece(7));
            Assert.Equal(8, layout.NextId);
        }
    }
}
