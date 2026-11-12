using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Layout;
using Xunit;

namespace Worktown.Sim.Tests.Layout
{
    /// <summary>
    /// Spec 062a AC2: each of the rules 1-7 with a passing and a failing literal layout (a small test room and catalog), the default layout
    /// (from default.json) passing in the default room, and the same input giving the same report.
    /// </summary>
    public class LayoutRulesTests
    {
        // A 6 x 4 m room: the floor (0, 0)-(6000, 4000), walls at the back and both sides, the street (guests) south of z 0.
        private static RoomDef Room(PointMm? staff = null, IEnumerable<RectMm>? keepOut = null) => new RoomDef(
            new RectMm(-2000, -3000, 8000, 6000),
            new RectMm(0, 0, 6000, 4000),
            new RectMm(0, 0, 6000, 4000),
            new RectMm(0, -3000, 8000, 0),
            new[] { new RectMm(-200, 4000, 6200, 4200), new RectMm(-200, 0, 0, 4000), new RectMm(6000, 0, 6200, 4000) },
            keepOut,
            staff ?? new PointMm(4500, 2000),
            new PointMm(7500, -1000));

        private static readonly PieceCatalog Catalog = new PieceCatalog(new[]
        {
            new PieceDef(PieceCatalog.ServingCounter, 1200, 700, PieceMount.InLine, true, true, true, true, true, 0, 0),
            new PieceDef("front", 1000, 700, PieceMount.InLine, true, true, false, false, true, 0, 0),
            new PieceDef("box", 600, 600, PieceMount.Floor, false, false, true, false, true, 0, 0),
            new PieceDef("cabinet", 600, 400, PieceMount.InLine, false, false, false, false, true, 0, 0),
            new PieceDef("panel", 800, 20, PieceMount.Wall, false, false, true, false, true, 0, 0),
            new PieceDef("sign", 600, 30, PieceMount.Overhead, false, false, true, false, true, 0, 0),
            new PieceDef("fence", 200, 3000, PieceMount.Yard, false, false, false, false, false, 0, 0),
        });

        private static LayoutPiece P(int id, string type, int x, int z, int turns = 0) => new LayoutPiece(id, type, x, z, turns, 0, 1, 0);

        /// <summary>A layout that keeps every rule: the serving counter on the street side, a box against the back, a panel on the back wall, a sign over the counter.</summary>
        private static List<LayoutPiece> Base() => new List<LayoutPiece>
        {
            P(1, PieceCatalog.ServingCounter, 3000, 350, 2),
            P(2, "box", 3000, 3300),
            P(3, "panel", 5000, 3990),
            P(4, "sign", 3000, 350, 2),
        };

        private static LayoutReport Check(IEnumerable<LayoutPiece> pieces, RoomDef? room = null) =>
            LayoutRules.Validate(new ShopLayout(1, pieces, null, null, 1), room ?? Room(), Catalog);

        private static List<LayoutPiece> With(params LayoutPiece[] more) => Base().Concat(more).ToList();

        private static string Describe(LayoutReport r) => string.Join("; ", r.Lines());

        [Fact]
        public void TheBaseLayout_KeepsEveryRule()
        {
            LayoutReport r = Check(Base());
            Assert.True(r.IsValid, Describe(r));
        }

        [Fact]
        public void TheDefaultLayout_KeepsEveryRule_InTheDefaultRoom()
        {
            ShopLayout layout = DefaultLayoutFile.Load();
            LayoutReport r = LayoutRules.Validate(layout, RoomDef.CreateDefault(), PieceCatalog.CreateDefault());
            Assert.True(r.IsValid, Describe(r));
            Assert.Equal(28, layout.Pieces.Count);
        }

        [Fact]
        public void TheSameInput_GivesTheSameReport()
        {
            List<LayoutPiece> bad = With(P(5, "box", 3200, 3300), P(6, "cabinet", 3000, 2500), P(7, "panel", 1000, 2000));
            LayoutReport a = Check(bad), b = Check(bad.AsEnumerable().Reverse());
            Assert.False(a.IsValid);
            Assert.Equal(a, b);
            Assert.Equal(a.Lines(), b.Lines());
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            ShopLayout layout = DefaultLayoutFile.Load();
            Assert.Equal(LayoutRules.Validate(layout, RoomDef.CreateDefault(), PieceCatalog.CreateDefault()),
                LayoutRules.Validate(layout, RoomDef.CreateDefault(), PieceCatalog.CreateDefault()));
        }

        [Fact]
        public void Rule1_Inside()
        {
            Assert.True(Check(With(P(5, "box", 5700, 3300))).IsValid, "against the right wall, inside");
            LayoutReport r = Check(With(P(5, "box", 5900, 3300)));
            LayoutProblem outside = Assert.Single(r.Problems, p => p.Kind == LayoutProblemKind.Outside);
            Assert.Equal(5, outside.PieceId);
            Assert.Equal("The box must stand inside the shop", outside.Text);

            Assert.True(Check(With(P(5, "fence", 5000, -2000))).IsValid, "a yard piece in the street");
            Assert.Equal(1, Check(With(P(5, "fence", 1000, 2000))).Count(LayoutProblemKind.Outside));

            Assert.True(Check(With(P(5, "panel", 1000, 3990))).IsValid, "a second panel on the back wall");
            LayoutReport off = Check(With(P(5, "panel", 1000, 2000)));
            Assert.Equal(new[] { LayoutProblemKind.OffWall }, off.Problems.Select(p => p.Kind));
        }

        [Fact]
        public void Rule2_NoOverlap_AndNoKeepOut()
        {
            Assert.True(Check(With(P(5, "box", 3600, 3300))).IsValid, "touching is allowed");
            LayoutReport r = Check(With(P(5, "box", 3500, 3300)));
            LayoutProblem overlap = Assert.Single(r.Problems);
            Assert.Equal((LayoutProblemKind.Overlap, 2, 5), (overlap.Kind, overlap.PieceId, overlap.OtherId));
            Assert.Equal((3250, 3300), (overlap.XMm, overlap.ZMm));
            Assert.Equal("The box overlaps the box", overlap.Text);

            Assert.True(Check(With(P(5, "panel", 4200, 3990))).IsValid, "two panels touching");
            Assert.Equal(1, Check(With(P(5, "panel", 4300, 3990))).Count(LayoutProblemKind.Overlap));
            Assert.True(Check(With(P(5, "sign", 3000, 3300))).IsValid, "an overhead sign over the box: another band");

            RoomDef door = Room(keepOut: new[] { new RectMm(0, 1000, 900, 2000) });
            Assert.True(Check(Base(), door).IsValid);
            LayoutReport blocked = Check(With(P(5, "box", 1000, 1500, 1)), door);
            Assert.Equal(1, blocked.Count(LayoutProblemKind.KeepOut));
            Assert.Equal("The box blocks a door", blocked.Problems.Single(p => p.Kind == LayoutProblemKind.KeepOut).Text);
        }

        [Fact]
        public void Rule3_Aisles()
        {
            Assert.True(Check(With(P(5, "cabinet", 3000, 1900))).IsValid, "a cabinet with its back to the box's aisle");
            LayoutReport r = Check(With(P(5, "cabinet", 3000, 2500)));
            LayoutProblem aisle = Assert.Single(r.Problems, p => p.Kind == LayoutProblemKind.Aisle && p.PieceId == 2);
            Assert.Equal(5, aisle.OtherId);
            Assert.Equal("Too close to the cabinet: aisles need 0.9 m", aisle.Text);

            // A counter with a guest side keeps its guest side's aisle free too.
            LayoutReport street = Check(With(P(5, "fence", 3000, -2000)));
            Assert.Contains(street.Problems, p => p.Kind == LayoutProblemKind.Aisle && p.PieceId == 1 && p.OtherId == 5);

            // A strip into a wall.
            LayoutReport wall = Check(With(P(5, "box", 500, 2000, 1)));
            Assert.Contains(wall.Problems, p => p.Kind == LayoutProblemKind.Aisle && p.PieceId == 5 && p.OtherId == 0);
        }

        [Fact]
        public void Rule4_EveryStationReachable()
        {
            Assert.True(Check(With(P(5, "box", 1000, 1500, 3))).IsValid, "a box facing east, in reach");
            LayoutReport faced = Check(With(P(5, "box", 1000, 3300, 2))); // its cook side against the back wall
            Assert.Contains(faced.Problems, p => p.Kind == LayoutProblemKind.Unreachable && p.PieceId == 5);
            Assert.Equal("Nobody can reach the box", faced.Problems.First(p => p.Kind == LayoutProblemKind.Unreachable).Text);

            // A cook who starts inside a piece reaches nothing: every station is unreachable and nothing else is wrong.
            LayoutReport stuck = Check(Base(), Room(staff: new PointMm(3000, 3300)));
            Assert.Equal(new[] { 1, 2, 3, 4 }, stuck.Problems.Select(p => p.PieceId));
            Assert.All(stuck.Problems, p => Assert.Equal(LayoutProblemKind.Unreachable, p.Kind));
        }

        [Fact]
        public void Rule5_GuestsReachACounter()
        {
            Assert.True(Check(With(P(5, "fence", 5000, -2000))).IsValid, "a fence with a way round its end");
            LayoutReport r = Check(With(P(5, "fence", 5000, -1500))); // from the floor's edge to the end of the street
            LayoutProblem none = Assert.Single(r.Problems);
            Assert.Equal(LayoutProblemKind.NoGuestCounter, none.Kind);
            Assert.Equal((7500, -1000), (none.XMm, none.ZMm));
            Assert.Equal("Guests cannot reach a serving counter", none.Text);
        }

        [Fact]
        public void Rule6_TheCounterHasAGuestSide()
        {
            Assert.True(Check(With(P(5, "front", 1000, 350, 2))).IsValid, "a front counter on the street side");
            LayoutReport r = Check(With(P(5, "front", 1000, 2000, 2))); // in the middle of the kitchen
            LayoutProblem side = Assert.Single(r.Problems);
            Assert.Equal((LayoutProblemKind.NoGuestSide, 5), (side.Kind, side.PieceId));
            Assert.Equal("The front needs the guests on one side and the kitchen on the other", side.Text);
        }

        [Fact]
        public void Rule7_RequiredPieces()
        {
            LayoutReport r = Check(Base().Where(p => p.TypeId != PieceCatalog.ServingCounter));
            LayoutProblem missing = r.Problems.Single(p => p.Kind == LayoutProblemKind.Missing);
            Assert.Equal("The shop needs a serving counter", missing.Text);
            Assert.Equal(1, r.Count(LayoutProblemKind.NoGuestCounter));

            ShopLayout standard = DefaultLayoutFile.Load();
            ShopLayout noSink = standard.WithPieces(standard.Pieces.Where(p => p.TypeId != PieceCatalog.Sink));
            LayoutReport d = LayoutRules.Validate(noSink, RoomDef.CreateDefault(), PieceCatalog.CreateDefault());
            Assert.Equal("The shop needs a sink", Assert.Single(d.Problems).Text);
        }

        [Fact]
        public void UnknownTypes_AreSkipped()
        {
            Assert.True(Check(With(P(5, "hot_tub", 3000, 3300))).IsValid, "on top of the box, but unknown: kept, not spawned, not checked");
        }

        [Fact]
        public void Problems_ComeInRuleOrder()
        {
            LayoutReport r = Check(Base().Where(p => p.TypeId != PieceCatalog.ServingCounter).Concat(new[] { P(5, "box", 5900, 3300), P(6, "panel", 1000, 2000) }));
            List<LayoutProblemKind> kinds = r.Problems.Select(p => p.Kind).ToList();
            Assert.Equal(kinds.OrderBy(k => k).ToList(), kinds);
            Assert.Equal(LayoutProblemKind.Outside, kinds.First());
            Assert.Equal(LayoutProblemKind.Missing, kinds.Last());
        }

        [Fact]
        public void Validate_RejectsMissingInputs()
        {
            Assert.Throws<ArgumentException>(() => LayoutRules.Validate(null!, Room(), Catalog));
            Assert.Throws<ArgumentException>(() => LayoutRules.Validate(new ShopLayout(1, null, null, null, 1), null!, Catalog));
            Assert.Throws<ArgumentException>(() => LayoutRules.Validate(new ShopLayout(1, null, null, null, 1), Room(), null!));
        }

        [Fact]
        public void CookDirection_FollowsTheQuarterTurns()
        {
            Assert.Equal(new PointMm(0, -1), LayoutRules.CookDirection(0));
            Assert.Equal(new PointMm(-1, 0), LayoutRules.CookDirection(1));
            Assert.Equal(new PointMm(0, 1), LayoutRules.CookDirection(2));
            Assert.Equal(new PointMm(1, 0), LayoutRules.CookDirection(3));
            Assert.Equal(LayoutRules.CookDirection(1), LayoutRules.CookDirection(-3));
            Assert.Equal(LayoutRules.CookDirection(0), LayoutRules.CookDirection(4));
        }

        [Fact]
        public void AccessStrip_LiesInFrontOfTheSide()
        {
            Assert.True(Catalog.TryGet("box", out PieceDef box));
            Assert.Equal(new RectMm(2700, 2100, 3300, 3000), LayoutRules.AccessStrip(P(1, "box", 3000, 3300), box, false));
            Assert.Equal(new RectMm(2700, 3600, 3300, 4500), LayoutRules.AccessStrip(P(1, "box", 3000, 3300), box, true));
            Assert.Equal(new RectMm(1800, 3000, 2700, 3600), LayoutRules.AccessStrip(P(1, "box", 3000, 3300, 1), box, false));
            Assert.Equal(new RectMm(3300, 3000, 4200, 3600), LayoutRules.AccessStrip(P(1, "box", 3000, 3300, 3), box, false));
            Assert.True(Catalog.TryGet("panel", out PieceDef panel));
            Assert.Equal(new RectMm(4600, 1880, 5400, 3980), LayoutRules.AccessStrip(P(1, "panel", 5000, 3990), panel, false));
            Assert.Throws<ArgumentException>(() => LayoutRules.AccessStrip(null!, box, false));
        }

        [Fact]
        public void AisleStrips_AddTheGuestSideOfACounter()
        {
            Assert.True(Catalog.TryGet("box", out PieceDef box));
            Assert.Single(LayoutRules.AisleStrips(P(1, "box", 0, 0), box));
            Assert.True(Catalog.TryGet(PieceCatalog.ServingCounter, out PieceDef counter));
            IReadOnlyList<RectMm> strips = LayoutRules.AisleStrips(P(1, PieceCatalog.ServingCounter, 3000, 350, 2), counter);
            Assert.Equal(new[] { new RectMm(2400, 700, 3600, 1600), new RectMm(2400, -900, 3600, 0) }, strips);
        }

        [Fact]
        public void BandAndNameOf()
        {
            Assert.Equal(0, LayoutRules.Band(PieceMount.InLine));
            Assert.Equal(0, LayoutRules.Band(PieceMount.Floor));
            Assert.Equal(0, LayoutRules.Band(PieceMount.Yard));
            Assert.Equal(1, LayoutRules.Band(PieceMount.Wall));
            Assert.Equal(2, LayoutRules.Band(PieceMount.Overhead));
            Assert.Equal("dish return", LayoutRules.NameOf(PieceCatalog.DishReturnModule));
            Assert.Equal("shelf dry", LayoutRules.NameOf(PieceCatalog.ShelfDry));
            Assert.Equal("fryer", LayoutRules.NameOf(PieceCatalog.Fryer));
            Assert.Equal("piece", LayoutRules.NameOf(""));
        }

        /// <summary>
        /// 062a review nit: the goal board is a station (pressed in first person), so rule 4 checks a cook reaches it across the worktop
        /// under it; it is 20 mm deep (its panel is 18 mm) and touches the back wall.
        /// </summary>
        [Fact]
        public void TheGoalBoard_IsAStationOnTheWall_ThatRule4Checks()
        {
            PieceCatalog catalog = PieceCatalog.CreateDefault();
            Assert.True(catalog.TryGet(PieceCatalog.GoalBoard, out PieceDef board));
            Assert.True(board.HasStation);
            Assert.Equal(PieceMount.Wall, board.Mount);
            Assert.Equal(20, board.DepthMm);
            ShopLayout standard = DefaultLayoutFile.Load();
            LayoutPiece placed = standard.Pieces.Single(p => p.TypeId == PieceCatalog.GoalBoard);
            // Turned to face the wall, it is still on the wall but out of every cook's reach.
            ShopLayout hidden = standard.WithPieces(standard.Pieces.Select(p => p.Id == placed.Id ? p with { Turns = 2 } : p));
            LayoutReport r = LayoutRules.Validate(hidden, RoomDef.CreateDefault(), catalog);
            Assert.Contains(r.Problems, p => p.Kind == LayoutProblemKind.Unreachable && p.PieceId == placed.Id);
        }
    }
}
