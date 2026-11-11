using System;
using Worktown.Sim.Climb;
using Xunit;

namespace Worktown.Sim.Tests.Climb
{
    /// <summary>Spec 052b: the GAME NUMBERS rows (amounts per crew size, stage 1 pace at the game's 15 s summary).</summary>
    public class GameNumbersTests
    {
        private static readonly ClimbCatalog Game = ClimbCatalog.CreateDefault(stage1Extras: false);

        [Fact]
        public void TheReferenceCrewGetsTheCatalogsOwnAmounts()
        {
            GameNumbersRow r = GameNumbers.For(Game, 4, 1, seeds: 2);
            Assert.Equal((4, 1000, 1000, 1000), (r.Players, r.DifficultyPermille, r.DemandPermille, r.GoalPermille));
            Assert.Equal(Game.Purchase("rent_restaurant").Cost, r.GoalGame); // $300.00 (the 053 goal)
            Assert.Equal(Game.StageOf(ClimbStage.FoodCorner).StockPerService, r.StockPerService);
            Assert.Equal(500, r.PitchFeeGame);
            Assert.Equal(2500, r.StartGame);
            Assert.Equal(Game.StageOf(ClimbStage.FirstRestaurant).Upkeep, r.RentGame);
            Assert.Equal(Game.Purchase("service_license").Cost, r.LicenseGame);
            Assert.True(r.MedianServices > 0);
            Assert.True(r.TenthsOfHours > 0);
        }

        [Fact]
        public void ASoloGetsTheD40Factors()
        {
            GameNumbersRow r = GameNumbers.For(Game, 1, 1, seeds: 1);
            Assert.Equal((550, 250, 137), (r.DifficultyPermille, r.DemandPermille, r.GoalPermille));
            Assert.Equal(4100, r.GoalGame); // $300 x 0.137, whole dollars
            Assert.Equal(100, r.PitchFeeGame); // $5 x 0.25
            Assert.Equal(300, r.StartGame); // $25 x 0.137
            Assert.Equal(2, r.StockPerService);
        }

        [Fact]
        public void GameScaleMultipliesAmountsAndTheRowIsDeterministic()
        {
            GameNumbersRow one = GameNumbers.For(Game, 2, 1, seeds: 1), two = GameNumbers.For(Game, 2, 2, seeds: 1);
            Assert.Equal(2 * one.GoalGame, two.GoalGame);
            Assert.Equal(2 * one.RentGame, two.RentGame);
            Assert.Equal(one.MedianServices, two.MedianServices);
            Assert.Equal(one, GameNumbers.For(Game, 2, 1, seeds: 1));
            Assert.Equal(8, GameNumbers.ReportPlayers.Count);
            Assert.Throws<ArgumentOutOfRangeException>(() => GameNumbers.For(Game, 2, 0));
            Assert.Throws<ArgumentNullException>(() => GameNumbers.For(null!, 2, 1));
        }

        [Fact]
        public void ADnfRowSaysMinusOne()
        {
            GameNumbersRow r = GameNumbers.For(Game, 4, 1, seeds: 1, horizonSeconds: 600);
            Assert.Equal(-1, r.MedianServices);
            Assert.Equal(-1, r.TenthsOfHours);
        }
    }
}
