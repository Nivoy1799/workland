using System;
using System.Linq;
using Worktown.Sim.Climb;
using Worktown.Sim.Cooking;
using Xunit;
using T = Worktown.Sim.Tests.Climb.TestClimb;

namespace Worktown.Sim.Tests.Climb
{
    public class ClimbCatalogTests
    {
        [Fact]
        public void CreateDefaultIsValidAndVegan() // AC9, D27
        {
            ClimbCatalog c = ClimbCatalog.CreateDefault();
            Assert.Equal(5, c.Stages.Count);
            Assert.Equal("fries", c.Stage1DishId);
            var names = c.Dishes.Select(d => d.Name).Concat(c.Channels.Select(x => x.Name)).Concat(c.Rivals.Select(r => r.Name))
                .Concat(c.Purchases.Select(p => p.Name)).Concat(c.BigJobs.Select(j => j.Name));
            Assert.All(names, n => Assert.Null(ContentRules.FindForbidden(n)));
            Assert.Equal(15, ClimbStrategy.All.Count);
        }

        [Fact]
        public void MoneyScaleMultipliesEveryMoneyValue() // question 17
        {
            ClimbCatalog one = ClimbCatalog.CreateDefault();
            ClimbCatalog cents = ClimbCatalog.CreateDefault(100);
            Assert.Equal(100, cents.MoneyScale);
            Assert.Equal(one.Dish("fries").ReferencePrice * 100, cents.Dish("fries").ReferencePrice);
            Assert.Equal(one.Purchase("rent_restaurant").Cost * 100, cents.Purchase("rent_restaurant").Cost);
            Assert.Equal(one.StartingMoney * 100, cents.StartingMoney);
            Assert.Equal(one.StageOf(ClimbStage.FirstRestaurant).Upkeep * 100, cents.StageOf(ClimbStage.FirstRestaurant).Upkeep);
            Assert.Equal(one.Rival("dash_bowls").PriceOffset * 100, cents.Rival("dash_bowls").PriceOffset);
            Assert.Equal(one.BigJob("cook_off").Prize * 100, cents.BigJob("cook_off").Prize);
            Assert.Throws<ArgumentException>(() => T.Build(moneyScale: 0));
        }

        [Fact]
        public void DuplicateIdThrows() => // AC9
            Assert.Throws<ArgumentException>(() => T.Build(dishes: T.Dishes.Append(new DishDef("chips", "Chips Two", 1, 1, 2, 500))));

        [Theory]
        [InlineData("Bad-Id")]
        [InlineData("")]
        [InlineData("a_very_long_identifier_with_more_than_32")]
        public void BadIdThrows(string id) => // AC9
            Assert.Throws<ArgumentException>(() => T.Build(dishes: T.Dishes.Append(new DishDef(id, "Thing", 1, 1, 2, 500))));

        [Fact]
        public void MissingReferencesThrow() // AC9
        {
            Assert.Throws<ArgumentException>(() => T.Build(rivals: new[] { new RivalDef("r", "R", "nowhere", 500, 0, 60, 500, 1, 1) }));
            Assert.Throws<ArgumentException>(() => T.Build(purchases: T.Purchases.Append(
                new PurchaseDef("x", "X", ClimbStage.FoodCorner, PurchaseKind.Productive, 1, 0, PurchaseEffect.None with { UnlocksDish = "stew" }))));
            Assert.Throws<ArgumentException>(() => T.Build(purchases: T.Purchases.Append(
                new PurchaseDef("x", "X", ClimbStage.FoodCorner, PurchaseKind.Productive, 1, 0, PurchaseEffect.None with { SavedOn = "stew", ActionsSaved = 1 }))));
            Assert.Throws<ArgumentException>(() => T.Build(stages: T.Stages.Select(s => s.Stage == ClimbStage.FoodCorner ? s with { GatePurchaseId = "nothing" } : s)));
            Assert.Throws<ArgumentException>(() => T.Build(bigJobs: new[] { new BigJobDef("j", "J", BigJobKind.Event, 1, 1, 1000, 0, 0, "nothing", 1) }));
            Assert.Throws<ArgumentException>(() => T.Build(stage1Dish: "stew"));
        }

        [Fact]
        public void StageOneWithTwoDishesThrows() => // AC9
            Assert.Throws<ArgumentException>(() => T.Build(stages: T.Stages.Select(s => s.Stage == ClimbStage.FoodCorner ? s with { MaxDishes = 2 } : s)));

        [Fact]
        public void MissingOrDuplicateStageThrows()
        {
            Assert.Throws<ArgumentException>(() => T.Build(stages: T.Stages.Take(4)));
            Assert.Throws<ArgumentException>(() => T.Build(stages: T.Stages.Append(T.Stages[1])));
        }

        [Theory]
        [InlineData("Cheese Fries")]
        [InlineData("Egg Wrap")]
        public void ForbiddenWordInNameThrows(string name) => // AC9, D27
            Assert.Throws<ArgumentException>(() => T.Build(dishes: T.Dishes.Append(new DishDef("d27", name, 1, 1, 2, 500))));

        [Fact]
        public void ReferencePriceNotAboveCostThrows() // AC9
        {
            Assert.Throws<ArgumentException>(() => T.Build(dishes: T.Dishes.Append(new DishDef("d", "D", 1, 3, 3, 500))));
            Assert.Throws<ArgumentException>(() => T.Build(dishes: T.Dishes.Append(new DishDef("d", "D", 1, 3, 2, 500))));
        }

        [Fact]
        public void RangesAreChecked()
        {
            Assert.Throws<ArgumentException>(() => T.Build(dishes: T.Dishes.Append(new DishDef("d", "D", 0, 1, 2, 500))));
            Assert.Throws<ArgumentException>(() => T.Build(dishes: T.Dishes.Append(new DishDef("d", "D", 1, 1, 2, 5001))));
            Assert.Throws<ArgumentException>(() => T.Build(profiles: new[] { new PlayerProfile("p", 0, 500) }));
            Assert.Throws<ArgumentException>(() => T.Build(startingMoney: -1));
            Assert.Throws<ArgumentException>(() => T.Build(weights: T.Weights with { Price = -1 }));
        }

        [Fact]
        public void LookupsFindOrThrow()
        {
            ClimbCatalog c = T.Catalog;
            Assert.Equal(10, c.Profile("worker").ActionsPerMinute);
            Assert.Equal("Soup", c.Dish("soup").Name);
            Assert.Equal(20, c.Channel("counter").GuestsPerService);
            Assert.Equal("counter", c.Rival("rival_stand").ChannelId);
            Assert.Equal(50, c.Purchase("lease").Cost);
            Assert.Equal(100, c.BigJob("contest").Prize);
            Assert.Equal(300, c.StageOf(ClimbStage.FoodCorner).ReputationFloorPermille);
            Assert.Throws<ArgumentException>(() => c.Dish("stew"));
            Assert.Throws<ArgumentException>(() => c.Profile("nobody"));
        }

        [Fact]
        public void ChannelsOpenPerStageAndPurchase()
        {
            ClimbCatalog c = T.Catalog;
            Assert.True(c.IsBaseChannel("counter"));
            Assert.False(c.IsBaseChannel("booth"));
            Assert.Equal(new[] { "counter" }, c.ActiveChannels(ClimbStage.FirstRestaurant, new string[0]).Select(x => x.Id));
            Assert.Equal(new[] { "booth", "counter" }, c.ActiveChannels(ClimbStage.FirstRestaurant, new[] { "booth_kit" }).Select(x => x.Id));
            Assert.Equal(new[] { "counter" }, c.ActiveChannels(ClimbStage.FoodCorner, new[] { "booth_kit" }).Select(x => x.Id));
            Assert.Equal("counter", c.CounterOf(ClimbStage.ServiceMix)!.Id);
            Assert.Equal(1, c.ExtraChannels(ClimbStage.FirstRestaurant, new[] { "booth_kit" }));
            Assert.Equal(0, c.ExtraChannels(ClimbStage.FirstRestaurant, new string[0]));
            Assert.Equal(new[] { "rival_stand" }, c.RivalsOn("counter").Select(r => r.Id));
            Assert.Empty(c.RivalsOn("booth"));

            ClimbCatalog d = ClimbCatalog.CreateDefault();
            Assert.Equal(new[] { "corner_counter" }, d.ActiveChannels(ClimbStage.FoodCorner, new string[0]).Select(x => x.Id));
            Assert.Equal(new[] { "street_counter" }, d.ActiveChannels(ClimbStage.FirstRestaurant, new string[0]).Select(x => x.Id)); // replaces the corner
        }

        [Fact]
        public void MenuIsTheStageOneDishPlusUnlockedDishesUpToMax()
        {
            ClimbCatalog c = T.Catalog;
            Assert.Equal(new[] { "chips" }, c.Menu(ClimbStage.FoodCorner, new[] { "soup_recipe" }).Select(d => d.Id));
            Assert.Equal(new[] { "chips", "soup" }, c.Menu(ClimbStage.FirstRestaurant, new[] { "soup_recipe" }).Select(d => d.Id));
            Assert.Equal(new[] { "chips" }, c.Menu(ClimbStage.FirstRestaurant, new string[0]).Select(d => d.Id));
        }

        [Fact]
        public void EffectSumsAndUpkeep()
        {
            ClimbCatalog c = T.Catalog;
            string[] owned = { "peeler", "sign", "booth_kit", "helper", "lab" };
            Assert.Equal(2, c.ActionsSaved(owned, "chips"));
            Assert.Equal(0, c.ActionsSaved(owned, ClimbCatalog.CleaningTarget));
            Assert.Equal(50, c.QualityBonus(owned));
            Assert.Equal(500, c.CostSavedPermille(owned));
            Assert.Equal(500, c.CeilingBonusPermille(owned, "booth"));
            Assert.Equal(5, c.ReputationBonus(owned));
            Assert.Equal(new[] { "helper" }, c.Staff(owned).Select(p => p.Id));
            // stage 2 upkeep 2 + booth 2 + helper 3 + lab 1
            Assert.Equal(8, c.Upkeep(ClimbStage.FirstRestaurant, owned));
            Assert.Equal(1, c.Upkeep(ClimbStage.FoodCorner, new string[0]));
        }
    }
}
