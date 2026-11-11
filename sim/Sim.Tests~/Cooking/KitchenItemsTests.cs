using System;
using System.Collections.Generic;
using Worktown.Sim.Cooking;
using Xunit;

namespace Worktown.Sim.Tests.Cooking
{
    /// <summary>Spec 056a: the kitchen's base items equal the game's DemoIngredients field by field, in the game's order.</summary>
    public class KitchenItemsTests
    {
        // Literal copies of the game's DemoIngredients defs (game/Assets/_Project/Scripts/Cooking/DemoIngredients.cs).
        private static readonly BaseItemDef DemoPotato = new BaseItemDef("potato", "potato",
            new ItemStats(0.1f, 0f, 0f, 0.1f, 0.1f, 0f, 0.5f, 0.6f, 0f, 0.8f), new DonenessWindow(0.55f, 0.85f), Array.Empty<string>());
        private static readonly BaseItemDef DemoApple = new BaseItemDef("apple", "apple",
            new ItemStats(0.6f, 0f, 0.3f, 0.05f, 0f, 0f, 0.7f, 0.3f, 0f, 0.85f), new DonenessWindow(0f, 0.3f), Array.Empty<string>());
        private static readonly BaseItemDef DemoTomato = new BaseItemDef("tomato", "tomato",
            new ItemStats(0.4f, 0.1f, 0.5f, 0f, 0.6f, 0.1f, 0f, 0.5f, 0f, 0.5f), new DonenessWindow(0f, 1f), Array.Empty<string>());
        private static readonly BaseItemDef DemoOnion = new BaseItemDef("onion", "onion",
            new ItemStats(0.5f, 0f, 0.2f, 0.3f, 0.3f, 0f, 0.6f, 0.5f, 0f, 0.5f), new DonenessWindow(0f, 1f), Array.Empty<string>());

        private static void AssertSameDef(BaseItemDef expected, BaseItemDef actual)
        {
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.DisplayName, actual.DisplayName);
            Assert.Equal(expected.Stats, actual.Stats);
            Assert.Equal(expected.IdealDoneness, actual.IdealDoneness);
            Assert.Equal(expected.Tags, actual.Tags);
        }

        [Fact]
        public void TheFourDemoIngredientsEqualTheGamesLiteralDefs()
        {
            AssertSameDef(DemoPotato, KitchenItems.Potato);
            AssertSameDef(DemoApple, KitchenItems.Apple);
            AssertSameDef(DemoTomato, KitchenItems.Tomato);
            AssertSameDef(DemoOnion, KitchenItems.Onion);
        }

        [Fact]
        public void AllIsTheDemoFourThenStreetFoodThenGrillVegetables()
        {
            var expected = new List<BaseItemDef> { DemoPotato, DemoApple, DemoTomato, DemoOnion };
            expected.AddRange(StreetFoodItems.All);
            expected.AddRange(StreetFoodItems.GrillVegetables);
            Assert.Equal(17, KitchenItems.All.Count);
            for (int i = 0; i < expected.Count; i++) AssertSameDef(expected[i], KitchenItems.All[i]);
            Assert.Equal(4, KitchenItems.FirstStreetFood);
            Assert.Equal(StreetFoodItems.All[0].Id, KitchenItems.All[KitchenItems.FirstStreetFood].Id);
        }

        [Fact]
        public void CatalogIsAllThenTheDrinkItems()
        {
            Assert.Equal(KitchenItems.All.Count + Drinks.Items.Count, KitchenItems.Catalog.Count);
            for (int i = 0; i < KitchenItems.All.Count; i++) Assert.Same(KitchenItems.All[i], KitchenItems.Catalog[i]);
            for (int i = 0; i < Drinks.Items.Count; i++) Assert.Same(Drinks.Items[i], KitchenItems.Catalog[KitchenItems.All.Count + i]);
        }

        [Fact]
        public void CreateItemCatalogKnowsEveryItem()
        {
            ItemCatalog catalog = KitchenItems.CreateItemCatalog();
            Assert.Equal(KitchenItems.Catalog.Count, catalog.Count);
            foreach (BaseItemDef def in KitchenItems.Catalog)
            {
                Assert.True(catalog.TryGet(def.Id, out BaseItemDef found));
                AssertSameDef(def, found);
            }
            Assert.NotSame(catalog, KitchenItems.CreateItemCatalog());
        }

        [Fact]
        public void EveryKitchenItemIsVegan()
        {
            foreach (BaseItemDef def in KitchenItems.Catalog)
            {
                Assert.Null(ContentRules.FindForbidden(def.Id));
                Assert.Null(ContentRules.FindForbidden(def.DisplayName));
            }
        }
    }
}
