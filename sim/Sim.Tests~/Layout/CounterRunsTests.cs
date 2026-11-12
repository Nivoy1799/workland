using System.Linq;
using Worktown.Sim.Layout;
using Xunit;

namespace Worktown.Sim.Tests.Layout
{
    /// <summary>Spec 062b AC2: which counter-line pieces join (touching, a gap, other turns, another line).</summary>
    public class CounterRunsTests
    {
        private static readonly PieceCatalog Catalog = new PieceCatalog(new[]
        {
            new PieceDef("cabinet", 600, 400, PieceMount.InLine, false, false, false, false, true, 0, 0),
            new PieceDef("box", 600, 600, PieceMount.Floor, false, false, true, false, true, 0, 0),
        });

        private static LayoutPiece P(int id, string type, int x, int z, int turns = 0) => new LayoutPiece(id, type, x, z, turns, 0, 1, 0);

        private static int[][] Runs(params LayoutPiece[] pieces) =>
            CounterRuns.Of(new ShopLayout(1, pieces, null, null, 1), Catalog).Select(r => r.Select(p => p.Id).ToArray()).ToArray();

        [Fact]
        public void TouchingPieces_FormOneRun_InOrderAlongTheLine()
        {
            Assert.Equal(new[] { new[] { 2, 1, 3 } }, Runs(P(1, "cabinet", 1900, 3800), P(2, "cabinet", 1300, 3800), P(3, "cabinet", 2540, 3800)));
        }

        [Fact]
        public void AGap_SplitsTheRun()
        {
            Assert.Equal(new[] { new[] { 1 }, new[] { 2 } }, Runs(P(1, "cabinet", 1300, 3800), P(2, "cabinet", 2200, 3800))); // 300 mm apart
            Assert.Single(Runs(P(1, "cabinet", 1300, 3800), P(2, "cabinet", 1950, 3800))); // 50 mm: still one run
            Assert.Equal(2, Runs(P(1, "cabinet", 1300, 3800), P(2, "cabinet", 1951, 3800)).Length);
        }

        [Fact]
        public void OtherTurnsOrAnotherLine_AreAnotherRun()
        {
            Assert.Equal(new[] { new[] { 1 }, new[] { 2 } }, Runs(P(1, "cabinet", 1300, 3800), P(2, "cabinet", 1900, 3800, 2)));
            Assert.Equal(new[] { new[] { 2 }, new[] { 1 } }, Runs(P(1, "cabinet", 1300, 3800), P(2, "cabinet", 1900, 3700))); // back edges 4000, 3900
            Assert.Empty(Runs(P(1, "box", 1300, 3800), P(2, "unknown", 1900, 3800))); // floor pieces and unknown types join nothing
        }

        [Fact]
        public void TurnedPieces_RunAlongZ()
        {
            Assert.Single(Runs(P(1, "cabinet", 200, 1300, 1), P(2, "cabinet", 200, 1900, 1)));
            Assert.Equal(400, CounterRuns.BackEdge(new RectMm(0, 1000, 400, 1600), 1));
            Assert.Equal(0, CounterRuns.BackEdge(new RectMm(0, 1000, 400, 1600), 3));
            Assert.Equal((1000, 1600), CounterRuns.Along(new RectMm(0, 1000, 400, 1600), 3));
        }

        [Fact]
        public void TheDefaultKitchen_IsTheBackLineAndTheFrontCounter()
        {
            var runs = CounterRuns.Of(DefaultLayoutFile.Load(), PieceCatalog.CreateDefault());
            Assert.Equal(2, runs.Count);
            Assert.Equal(new[] { 23, 17, 24, 11, 27, 5, 10, 25, 13, 26 }, runs[1].Select(p => p.Id)); // the front counter, west to east
            Assert.Equal(new[] { 19, 1, 2, 20, 8, 7, 3, 4, 22, 9, 6, 14, 21 }, runs[0].Select(p => p.Id)); // the back line (no turns: first)
        }
    }
}
