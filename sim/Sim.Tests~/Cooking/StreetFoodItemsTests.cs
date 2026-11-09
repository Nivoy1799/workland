using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;
using Xunit;

namespace Worktown.Sim.Tests.Cooking
{
    /// <summary>Spec 028 AC2: the street-food base items.</summary>
    public class StreetFoodItemsTests
    {
        private static readonly string[] Ids =
        {
            "flatbread", "wrap", "seitan", "falafel_mix", "lettuce", "cucumber", "red_cabbage", "lemon", "garlic_herb_sauce", "chili_sauce"
        };

        private static ItemFactory Factory() => new ItemFactory(new ItemCatalog(StreetFoodItems.All), OperationCatalog.CreateDefault());

        [Fact]
        public void TenItems_InCrateOrder_WithUniqueIds()
        {
            Assert.Equal(Ids, StreetFoodItems.All.Select(d => d.Id));
            Assert.Equal(10, StreetFoodItems.All.Select(d => d.Id).Distinct().Count());
        }

        [Fact]
        public void TheCatalogAcceptsThem_WithValidStatsAndDonenessWindows()
        {
            var catalog = new ItemCatalog(StreetFoodItems.All); // validates every stat (0..1) and window
            Assert.Equal(10, catalog.Count);
            foreach (BaseItemDef def in StreetFoodItems.All)
            {
                Assert.True(catalog.TryGet(def.Id, out _), def.Id);
                Assert.False(string.IsNullOrWhiteSpace(def.DisplayName));
                Assert.True(def.IdealDoneness.Min >= 0f && def.IdealDoneness.Min <= def.IdealDoneness.Max && def.IdealDoneness.Max <= 1f, def.Id);
            }
        }

        [Fact]
        public void EveryItem_CanBeCreated_AsARawBaseItem()
        {
            ItemFactory factory = Factory();
            foreach (string id in Ids)
            {
                OperationResult r = factory.CreateBase(id);
                Assert.True(r.Ok, id);
                Assert.Equal(0f, r.Item!.Stats.Doneness);
            }
        }

        [Theory]
        [InlineData("flatbread", "halves")]
        [InlineData("lemon", "halves")]
        [InlineData("seitan", "sticks")]
        [InlineData("lettuce", "slices")]
        [InlineData("cucumber", "slices")]
        [InlineData("red_cabbage", "slices")]
        public void CuttingGivesTheListedShape(string id, string shape)
        {
            Assert.Equal(shape, StreetFoodItems.CutShape(id));
            ItemFactory factory = Factory();
            Item whole = factory.CreateBase(id).Item!;
            OperationResult cut = factory.Apply("cut", new[] { whole },
                new Dictionary<string, ParamValue> { ["shape"] = ParamValue.Choice(shape) });
            Assert.True(cut.Ok, id);
            Assert.Equal(shape, cut.Item!.Buckets["shape"]);
        }

        [Theory]
        [InlineData("wrap")]
        [InlineData("falafel_mix")]
        [InlineData("garlic_herb_sauce")]
        [InlineData("chili_sauce")]
        [InlineData("potato")]
        [InlineData("")]
        public void ItemsThatAreNotCutHaveNoShape(string id)
        {
            Assert.Null(StreetFoodItems.CutShape(id));
        }

        [Fact]
        public void EveryItem_HasAGrocerPrice_InTheUsualRange()
        {
            EconomyCatalog economy = EconomyCatalog.CreateDefault();
            foreach (string id in Ids)
            {
                Assert.True(economy.TryGetGrocerPrice(id, out long price), id);
                Assert.InRange(price, 40L, 120L);
            }
            Assert.False(economy.TryGetGrocerPrice("cheese", out _), "the animal-product entry is gone (D27)");
        }

        [Fact]
        public void TheOldGrocerEntries_KeepTheirPrices()
        {
            EconomyCatalog economy = EconomyCatalog.CreateDefault();
            Assert.True(economy.TryGetGrocerPrice("potato", out long potato));
            Assert.Equal(60L, potato);
            Assert.True(economy.TryGetGrocerPrice("cashew", out long cashew));
            Assert.Equal(150L, cashew);
        }
    }
}
