using System.Linq;
using Worktown.Sim.Layout;
using Xunit;

namespace Worktown.Sim.Tests.Layout
{
    public class ShopLayoutTests
    {
        private static LayoutPiece P(int id, string type, int x = 0) => new LayoutPiece(id, type, x, 0, 0, 0, 1, 0);

        [Fact]
        public void Pieces_AreSortedById_AndTheNextIdIsAboveEveryIdInUse()
        {
            var layout = new ShopLayout(1, new[] { P(5, "stove"), P(2, "sink"), P(9, "fryer") }, new[] { 12 }, null, 3);
            Assert.Equal(new[] { 2, 5, 9 }, layout.Pieces.Select(p => p.Id));
            Assert.Equal(13, layout.NextId);
            Assert.Equal(20, new ShopLayout(1, new[] { P(1, "sink") }, null, null, 20).NextId);
            Assert.Equal(1, new ShopLayout(1, null, null, null, 0).NextId);
            Assert.Equal("fryer", layout.Piece(9)?.TypeId);
            Assert.Null(layout.Piece(4));
            Assert.Equal(1, layout.CountOf("stove"));
        }

        [Fact]
        public void NumberOf_CountsOnlyTypesPlacedMoreThanOnce()
        {
            var layout = new ShopLayout(1, new[] { P(3, "stove"), P(4, "stove"), P(1, "sink"), P(7, "stove") }, null, null, 8);
            Assert.Equal(1, layout.NumberOf(3));
            Assert.Equal(2, layout.NumberOf(4));
            Assert.Equal(3, layout.NumberOf(7));
            Assert.Equal(0, layout.NumberOf(1));
            Assert.Equal(0, layout.NumberOf(99));
        }

        [Fact]
        public void Equality_IsByValue()
        {
            ShopLayout Make() => new ShopLayout(1, new[] { P(1, "sink", 100), P(2, "stove", 200) }, new[] { 4 }, new[] { new ItemHome("pan", 2, 1, 0) }, 5);
            Assert.Equal(Make(), Make());
            Assert.Equal(Make().GetHashCode(), Make().GetHashCode());
            Assert.NotEqual(Make(), Make().WithPieces(new[] { P(1, "sink", 101), P(2, "stove", 200) }));
            Assert.NotEqual(Make(), new ShopLayout(1, Make().Pieces, new int[0], Make().Homes, 5));
            Assert.False(Make().Equals(null));
        }

        [Fact]
        public void WithPieces_KeepsStoredHomesAndTheNextId()
        {
            var layout = new ShopLayout(1, new[] { P(1, "sink") }, new[] { 6 }, new[] { new ItemHome("knife", 1, 0, 3) }, 9);
            ShopLayout moved = layout.WithPieces(new[] { P(1, "sink", 500) });
            Assert.Equal(layout.Stored, moved.Stored);
            Assert.Equal(layout.Homes, moved.Homes);
            Assert.Equal(9, moved.NextId);
            Assert.Equal(500, moved.Pieces[0].XMm);
        }

        [Fact]
        public void RectMm_TouchingIsNotOverlapping()
        {
            var a = new RectMm(0, 0, 100, 100);
            Assert.False(a.Intersects(new RectMm(100, 0, 200, 100)));
            Assert.True(a.Intersects(new RectMm(99, 0, 200, 100)));
            Assert.True(a.Contains(new RectMm(0, 0, 100, 100)));
            Assert.False(a.Contains(new RectMm(-1, 0, 100, 100)));
            Assert.Equal(new RectMm(0, 0, 100, 100), new RectMm(100, 100, 0, 0));
            Assert.Equal(RectMm.Around(50, 50, 100, 100), a);
            Assert.True(new RectMm(0, 0, 0, 10).IsEmpty);
            Assert.True(a.Contains(0, 0));
            Assert.False(a.Contains(100, 0));
        }
    }
}
