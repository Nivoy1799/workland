using System;
using System.Linq;
using Worktown.Sim.Layout;
using Xunit;

namespace Worktown.Sim.Tests.Layout
{
    public class PieceCatalogTests
    {
        [Fact]
        public void CreateDefault_HasTodaysKitchenAndTheReservedRows()
        {
            PieceCatalog c = PieceCatalog.CreateDefault();
            string[] today =
            {
                PieceCatalog.ShelfDry, PieceCatalog.Fridge, PieceCatalog.Stove, PieceCatalog.Fryer, PieceCatalog.Griddle, PieceCatalog.Assembly,
                PieceCatalog.PlateShelf, PieceCatalog.Sink, PieceCatalog.Worktop, PieceCatalog.FrontCounter, PieceCatalog.ServingCounter,
                PieceCatalog.RegisterModule, PieceCatalog.DispenserModule, PieceCatalog.SpitModule, PieceCatalog.DishReturnModule, PieceCatalog.Trash,
                PieceCatalog.Terminal, PieceCatalog.MenuBoard, PieceCatalog.Bin,
                PieceCatalog.StoragePallet, // spec 062b: the pallet where stored pieces wait
            };
            foreach (string t in today.Concat(PieceCatalog.ReservedTypes)) Assert.True(c.Contains(t), t);
            Assert.Equal(today.Length + PieceCatalog.ReservedTypes.Count, c.Rows.Count);
            Assert.Equal(c.Rows.Select(r => r.TypeId).OrderBy(t => t, StringComparer.Ordinal), c.Rows.Select(r => r.TypeId));
        }

        [Fact]
        public void CreateDefault_RequiredMountsAndLengthsAreAsSpecified()
        {
            PieceCatalog c = PieceCatalog.CreateDefault();
            string[] required = c.Rows.Where(r => r.Required).Select(r => r.TypeId).ToArray();
            Assert.Equal(new[] { PieceCatalog.Fridge, PieceCatalog.PlateShelf, PieceCatalog.RegisterModule, PieceCatalog.ServingCounter,
                PieceCatalog.ShelfDry, PieceCatalog.Sink, PieceCatalog.Trash }.OrderBy(t => t, StringComparer.Ordinal), required);
            Assert.True(c.TryGet(PieceCatalog.Terminal, out PieceDef terminal));
            Assert.Equal(PieceMount.Wall, terminal.Mount);
            Assert.True(c.TryGet(PieceCatalog.MenuBoard, out PieceDef board));
            Assert.Equal(PieceMount.Overhead, board.Mount);
            Assert.True(c.TryGet(PieceCatalog.Bin, out PieceDef bin));
            Assert.Equal(PieceMount.Yard, bin.Mount);
            Assert.False(bin.Movable);
            Assert.True(c.TryGet(PieceCatalog.Worktop, out PieceDef worktop));
            Assert.True(worktop.HasLength && worktop.IsCounter);
            Assert.True(c.TryGet(PieceCatalog.FrontCounter, out PieceDef front));
            Assert.True(front.HasLength && front.HasGuestSide);
            Assert.All(c.Rows.Where(r => r.TypeId != PieceCatalog.Worktop && r.TypeId != PieceCatalog.FrontCounter), r => Assert.False(r.HasLength, r.TypeId));
            // Spec 062b: the pallet stays in the yard; the door sign is a station; only the dry shelf and the fridge are tall.
            Assert.True(c.TryGet(PieceCatalog.StoragePallet, out PieceDef pallet));
            Assert.Equal(PieceMount.Yard, pallet.Mount);
            Assert.False(pallet.Movable || pallet.Required);
            Assert.True(c.TryGet(PieceCatalog.DoorSign, out PieceDef sign));
            Assert.True(sign.HasStation);
            Assert.Equal(new[] { PieceCatalog.Fridge, PieceCatalog.ShelfDry }, c.Rows.Where(r => r.Tall).Select(r => r.TypeId));
            Assert.False(c.TryGet("sofa", out _));
            Assert.False(c.Contains("sofa"));
        }

        [Fact]
        public void Constructor_RejectsBadRows()
        {
            PieceDef Ok(string id) => new PieceDef(id, 100, 100, PieceMount.InLine, false, false, false, false, true, 0, 0);
            Assert.Throws<ArgumentException>(() => new PieceCatalog(new[] { Ok("a"), Ok("a") }));
            Assert.Throws<ArgumentException>(() => new PieceCatalog(new[] { Ok("Bad Id") }));
            Assert.Throws<ArgumentException>(() => new PieceCatalog(new[] { Ok("a") with { WidthMm = 0 } }));
            Assert.Throws<ArgumentException>(() => new PieceCatalog(new[] { Ok("a") with { MaxLengthMm = 500, MinLengthMm = 50 } })); // a length, no counter
            Assert.Throws<ArgumentException>(() => new PieceCatalog(new[] { Ok("a") with { IsCounter = true, MinLengthMm = 200, MaxLengthMm = 500 } })); // width below min
            Assert.Throws<ArgumentException>(() => new PieceCatalog(new[] { Ok("a") with { MinLengthMm = 10 } }));
            Assert.Single(new PieceCatalog(new[] { Ok("a") }).Rows);
        }

        [Fact]
        public void WidthOf_ClampsALengthToTheRow_AndFixedPiecesKeepTheirWidth()
        {
            PieceCatalog c = PieceCatalog.CreateDefault();
            c.TryGet(PieceCatalog.Worktop, out PieceDef worktop);
            c.TryGet(PieceCatalog.Stove, out PieceDef stove);
            LayoutPiece P(int length) => new LayoutPiece(1, PieceCatalog.Worktop, 0, 0, 0, length, 1, 0);
            Assert.Equal(740, worktop.WidthOf(P(740)));
            Assert.Equal(worktop.MinLengthMm, worktop.WidthOf(P(10)));
            Assert.Equal(worktop.MaxLengthMm, worktop.WidthOf(P(99999)));
            Assert.Equal(1200, stove.WidthOf(P(740)));
        }

        [Fact]
        public void Footprint_IsCentredAndSwapsOnAQuarterTurn()
        {
            PieceCatalog.CreateDefault().TryGet(PieceCatalog.Fryer, out PieceDef fryer); // 800 x 1200
            RectMm straight = fryer.Footprint(new LayoutPiece(1, PieceCatalog.Fryer, 1000, 2000, 0, 0, 1, 0));
            Assert.Equal(new RectMm(600, 1400, 1400, 2600), straight);
            RectMm half = fryer.Footprint(new LayoutPiece(1, PieceCatalog.Fryer, 1000, 2000, 2, 0, 1, 0));
            Assert.Equal(straight, half);
            RectMm quarter = fryer.Footprint(new LayoutPiece(1, PieceCatalog.Fryer, 1000, 2000, 3, 0, 1, 0));
            Assert.Equal(new RectMm(400, 1600, 1600, 2400), quarter);
        }
    }
}
