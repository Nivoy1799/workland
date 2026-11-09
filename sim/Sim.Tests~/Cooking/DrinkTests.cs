using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Core;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;
using Xunit;
using T = Worktown.Sim.Tests.Cooking.TestItems;

namespace Worktown.Sim.Tests.Cooking
{
    /// <summary>
    /// Spec 033 AC2: the drinks station's sim: the fill buckets from the millilitres poured, the lid only on a filled cup, the drink
    /// rating (taste by balance, quality by the fill level), the content check (D27), determinism, cups and drinks staying out of
    /// every other op, the prices of a pour and the wholesaler's cap, and the order's drink line.
    /// </summary>
    public class DrinkTests
    {
        private static readonly ItemCatalog Items = new ItemCatalog(StreetFoodItems.All.Concat(StreetFoodItems.GrillVegetables)
            .Concat(Drinks.Items).Concat(new[] { T.Potato }));
        private static readonly ItemFactory Factory = new ItemFactory(Items, OperationCatalog.CreateDefault());

        private static Item Base(string id) => T.Ok(Factory.CreateBase(id));

        private static OperationResult TryFill(string drink, float ml) =>
            Factory.Apply(Drinks.FillOperationId, new[] { Base(drink) }, Drinks.FillParams(ml));

        private static Item Fill(string drink, float ml) => T.Ok(TryFill(drink, ml));

        private static OperationResult Lid(Item cup) => Factory.Apply(Drinks.LidOperationId, new[] { cup }, T.NoParams);

        private static float Quality(Item item) => ItemRater.Rate(item).Quality;

        private static string Name(Item item) => ItemNaming.FallbackName(item, NameCatalog.Empty);

        [Theory]
        [InlineData(0f, Drinks.Low)]
        [InlineData(200f, Drinks.Low)]
        [InlineData(339.9f, Drinks.Low)]
        [InlineData(340f, Drinks.Right)]
        [InlineData(360f, Drinks.Right)]
        [InlineData(379.9f, Drinks.Right)]
        [InlineData(380f, Drinks.Full)]
        [InlineData(400f, Drinks.Full)]
        [InlineData(406.7f, Drinks.Spilled)]
        [InlineData(500f, Drinks.Spilled)]
        public void TheFillBucket_FollowsTheMillilitres(float ml, string bucket)
        {
            Item cup = Fill("lemonade", ml);
            Assert.Equal(bucket, cup.Buckets[Drinks.MlParam]);
            Assert.Equal(bucket, Drinks.LevelOf(cup));
            Assert.Equal(bucket, Drinks.BucketOf(ml));
        }

        [Fact]
        public void TheDefaults_AreTheSpecsNumbers()
        {
            Assert.Equal(400f, Drinks.CupMl);
            Assert.Equal(340f, Drinks.RightFrom);
            Assert.Equal(380f, Drinks.FullFrom);
            Assert.Equal(200f, Drinks.PourRate);
            Assert.Equal(500f, Drinks.MaxMl);
            // A good hold is about 1.7 to 1.9 s.
            Assert.Equal(Drinks.Right, Drinks.BucketOf(1.7f * Drinks.PourRate));
            Assert.Equal(Drinks.Right, Drinks.BucketOf(1.85f * Drinks.PourRate));
            Assert.Equal(Drinks.Full, Drinks.BucketOf(1.9f * Drinks.PourRate));
        }

        [Fact]
        public void OnlyTheBucket_IsIdentity_TheExactMillilitresOnlyQuality()
        {
            Item a = Fill("lemonade", 345f), b = Fill("lemonade", 375f);
            Assert.Equal(a.Hash, b.Hash);
            Assert.Equal(Quality(a), Quality(b));
            Item low1 = Fill("lemonade", 100f), low2 = Fill("lemonade", 300f);
            Assert.Equal(low1.Hash, low2.Hash);
            Assert.True(Quality(low2) > Quality(low1), "a fuller low cup is better");
            Assert.NotEqual(a.Hash, Fill("iced_tea", 360f).Hash);
            Assert.NotEqual(a.Hash, Fill("lemonade", 390f).Hash);
        }

        [Fact]
        public void TheRightFill_RatesBest_FullAndSpilledDrop()
        {
            foreach (BaseItemDef drink in Drinks.All)
            {
                float right = Quality(Fill(drink.Id, 360f));
                float full = Quality(Fill(drink.Id, 395f));
                float spilled = Quality(Fill(drink.Id, 500f));
                float low = Quality(Fill(drink.Id, 200f));
                Assert.Equal(Drinks.Taste(drink.Stats), right, 5);
                Assert.True(right > full && full > spilled, drink.Id);
                Assert.True(right > low, drink.Id);
                Assert.InRange(right, 0.6f, 0.9f);
            }
            Assert.Equal(0.6f, Drinks.FillFactor(500f));
            Assert.Equal(0.9f, Drinks.FillFactor(400f));
            Assert.Equal(1f, Drinks.FillFactor(340f));
            Assert.Equal(0.3f, Drinks.FillFactor(0f));
        }

        [Fact]
        public void ALid_GoesOnlyOnAFilledCup_AndOnce_AndFinishesIt()
        {
            Item cup = Fill("soda", 360f);
            Item lidded = T.Ok(Lid(cup));
            Assert.Contains("plated", lidded.Tags);
            Assert.Contains(Drinks.DrinkTag, lidded.Tags);
            Assert.True(Drinks.IsLidded(lidded));
            Assert.Equal("soda", Drinks.DrinkOf(lidded));
            Assert.Equal(Drinks.Right, Drinks.LevelOf(lidded));
            Assert.Equal(Quality(cup), Quality(lidded));
            Assert.Equal(OperationError.StepOrder, Lid(Base(Drinks.CupId)).Error);
            Assert.Equal(OperationError.ForbiddenInput, Lid(lidded).Error);
            Assert.Equal(OperationError.StepOrder, Lid(Base("lettuce")).Error);
        }

        [Fact]
        public void AFill_PoursOnlyADrink()
        {
            Assert.Equal(OperationError.ForbiddenInput, Factory.Apply(Drinks.FillOperationId, new[] { Base("lettuce") }, Drinks.FillParams(300f)).Error);
            Assert.Equal(OperationError.ForbiddenInput, Factory.Apply(Drinks.FillOperationId, new[] { Base(Drinks.CupId) }, Drinks.FillParams(300f)).Error);
            Assert.Equal(OperationError.ForbiddenInput, Factory.Apply(Drinks.FillOperationId, new[] { Fill("soda", 100f) }, Drinks.FillParams(300f)).Error);
            Assert.Equal(OperationError.InvalidParam, Factory.Apply(Drinks.FillOperationId, new[] { Base("soda") },
                new Dictionary<string, ParamValue> { [Drinks.MlParam] = ParamValue.Number(600f) }).Error);
        }

        [Fact]
        public void CupsAndDrinks_GoIntoNoOtherOp_NoPlateMixOrBuild()
        {
            // (A drink base item, "lemonade", is never in the world: the server makes it only as a fill's input.)
            foreach (Item cup in new[] { Base(Drinks.CupId), Fill("lemonade", 360f), T.Ok(Lid(Fill("lemonade", 360f))) })
            {
                Assert.False(Factory.Apply("plate", new[] { cup }, T.NoParams).Ok, "plate " + cup.CanonicalTree);
                Assert.False(Factory.Apply("mix", new[] { cup, Base("lettuce") }, T.NoParams).Ok, "mix");
                Assert.False(Factory.Apply(Assembly.AssembleOperationId, new[] { Base("wrap"), cup }, T.NoParams).Ok, "build");
                Assert.False(Factory.Apply("cut", new[] { cup }, new Dictionary<string, ParamValue> { ["shape"] = ParamValue.Choice("slices") }).Ok, "cut");
                Assert.False(T.Heat("fry", cup, 150f, 60f).Ok || Factory.Apply("fry", new[] { cup }, new Dictionary<string, ParamValue>
                {
                    ["temp"] = ParamValue.Number(150f), ["time"] = ParamValue.Number(60f),
                }).Ok, "fry");
            }
        }

        [Fact]
        public void Names_SayTheLevel_AndTheLid()
        {
            Assert.Equal("cup of lemonade", Name(Fill("lemonade", 360f)));
            Assert.Equal("half-full cup of iced tea", Name(Fill("iced_tea", 100f)));
            Assert.Equal("brimming cup of soda", Name(Fill("soda", 390f)));
            Assert.Equal("overfilled cup of soda", Name(Fill("soda", 500f)));
            Assert.Equal("cup of lemonade to go", Name(T.Ok(Lid(Fill("lemonade", 360f)))));
            Assert.Equal("empty cup", Name(Base(Drinks.CupId)));
        }

        [Fact]
        public void TheDrinks_PassTheContentCheck()
        {
            var texts = new List<string>();
            foreach (BaseItemDef def in Drinks.Items) { texts.Add(def.Id); texts.Add(def.DisplayName); texts.AddRange(def.Tags); }
            foreach (BaseItemDef drink in Drinks.All)
                foreach (float ml in new[] { 100f, 360f, 390f, 500f })
                {
                    Item cup = Fill(drink.Id, ml);
                    texts.Add(Name(cup));
                    texts.Add(Name(T.Ok(Lid(cup))));
                }
            Assert.True(OperationCatalog.CreateDefault().TryGet(Drinks.FillOperationId, out OperationDef fill));
            foreach (BucketDef b in fill.Params[0].Buckets) { texts.Add(b.Id); texts.Add(b.Label); }
            foreach (string text in texts) Assert.Null(ContentRules.FindForbidden(text));
        }

        [Fact]
        public void Fills_AreDeterministic()
        {
            Item a = T.Ok(Lid(Fill("iced_tea", 352.5f)));
            Item b = T.Ok(Lid(Fill("iced_tea", 352.5f)));
            Assert.Equal(a.Hash, b.Hash);
            Assert.Equal(a.CanonicalTree, b.CanonicalTree);
            Assert.Equal(Quality(a), Quality(b));
            // The ops are appended: the catalog's earlier ops keep their place and the old items their hashes (AssemblyTests pins).
            Assert.Equal(16, OperationCatalog.CreateDefault().Count);
        }

        [Fact]
        public void APour_PaysTheCupOnce_AndItsShareOfTheDrink()
        {
            Assert.Equal(1, PriceRules.FillPercent(0f));
            Assert.Equal(1, PriceRules.FillPercent(float.NaN));
            Assert.Equal(85, PriceRules.FillPercent(340f));
            Assert.Equal(90, PriceRules.FillPercent(360f));
            Assert.Equal(100, PriceRules.FillPercent(400f));
            Assert.Equal(100, PriceRules.FillPercent(500f));
            Assert.Equal(35 + 54, PriceRules.FillCost(35, 60, 90, true));
            Assert.Equal(6, PriceRules.FillCost(35, 60, 10, false)); // a top-up pays no second cup

            EconomyEngine engine = Worktown.Sim.Tests.Economy.TestEconomy.Engine;
            EconomyState s = TestEconomyState(engine, 1000);
            PurchaseResult r = engine.TryBuyFill(s, "p", "lemonade", 90, true);
            Assert.True(r.Ok);
            Assert.Equal(35 + 54, r.Cost);
            Assert.Equal(1000 - 89, Worktown.Sim.Tests.Economy.TestEconomy.Acct(r.State, "p").Balance);
            Assert.Equal(EconomyError.InsufficientFunds, engine.TryBuyFill(TestEconomyState(engine, 50), "p", "lemonade", 90, true).Error);
            Assert.Equal(EconomyError.InvalidQuantity, engine.TryBuyFill(s, "p", "lemonade", 0, true).Error);
            Assert.Equal(EconomyError.UnknownBaseItem, engine.TryBuyFill(s, "p", "lettuce_juice", 50, true).Error);
        }

        private static EconomyState TestEconomyState(EconomyEngine engine, long balance) =>
            Worktown.Sim.Tests.Economy.TestEconomy.Open(engine, engine.Initial(), "p", balance);

        [Fact]
        public void TheWholesalersCap_NeverCountsMoreThanAPourCost()
        {
            EconomyCatalog catalog = EconomyCatalog.CreateDefault();
            foreach (BaseItemDef drink in Drinks.All)
            {
                Assert.True(catalog.TryGetGrocerPrice(drink.Id, out long price));
                Assert.True(catalog.TryGetGrocerPrice(Drinks.CupId, out long cupPrice));
                foreach (float ml in new[] { 6.7f, 100f, 339f, 340f, 379f, 380f, 400f, 401f, 500f })
                {
                    Item cup = T.Ok(Lid(Fill(drink.Id, ml)));
                    long counted = PriceRules.ItemCost(catalog, cup)!.Value;
                    long paidOnce = PriceRules.FillCost(cupPrice, price, PriceRules.FillPercent(ml), true);
                    Assert.True(counted <= paidOnce - cupPrice, drink.Id + " " + ml + " ml: counted " + counted + ", paid " + paidOnce);
                    Assert.True(counted >= 0);
                }
                // 033 review: a cup topped up over several pours (each charged on its own, the cup once) never counts more than the pours cost.
                foreach (float[] pours in new[] { new[] { 100f, 100f, 160f }, new[] { 6.7f, 6.7f }, new[] { 200f, 180f }, new[] { 339f, 1f, 1f }, new[] { 300f, 200f } })
                {
                    float total = Math.Min(Drinks.MaxMl, pours.Sum());
                    Item cup = T.Ok(Lid(Fill(drink.Id, total)));
                    long counted = PriceRules.ItemCost(catalog, cup)!.Value;
                    long paid = 0;
                    for (int i = 0; i < pours.Length; i++)
                        paid += PriceRules.FillCost(cupPrice, price, PriceRules.FillPercent(pours[i]), i == 0);
                    Assert.True(counted <= paid - cupPrice, drink.Id + " " + string.Join("+", pours) + " ml: counted " + counted + ", paid " + paid);
                    // Spec 034: drawn from a keg, the same pours drew at least the stock cost's share of a bought cup.
                    long stock = PriceRules.StockCost(catalog, cup)!.Value;
                    long drawnPercent = pours.Sum(ml => (long)PriceRules.FillPercent(ml));
                    Assert.True(stock * 100 <= Supplier.BulkUnitPrice(catalog, price) * drawnPercent, drink.Id + " stock " + stock);
                }
            }
        }

        [Fact]
        public void APlatedOldDish_HasExactlyItsOldTags_NoCup()
        {
            // 033 review: the plate op adds only "plated" (the cup tag is only forbidden, never added).
            Item fries = T.Ok(Factory.Apply("plate", new[] { Base("potato") }, T.NoParams));
            Assert.Equal(new[] { "plated" }, fries.Tags.Where(t => t == "plated" || t == Drinks.CupTag).ToArray());
            Assert.DoesNotContain(Drinks.CupTag, fries.Tags);
            Item wrap = T.Ok(Factory.Apply("plate", new[] { Base("wrap"), Base("lettuce") }, T.NoParams));
            Assert.DoesNotContain(Drinks.CupTag, wrap.Tags);
            Assert.Contains("plated", wrap.Tags);
            Assert.Equal(Base("potato").Tags.Concat(new[] { "plated" }).OrderBy(t => t, StringComparer.Ordinal),
                fries.Tags.OrderBy(t => t, StringComparer.Ordinal));
        }

        [Fact]
        public void TheOrdersDrinkLine_IsSeeded_HalfOrderNone()
        {
            var a = new SeededRandom(7);
            var b = new SeededRandom(7);
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < 600; i++)
            {
                string? x = Drinks.DrawOrderDrink(a);
                Assert.Equal(x, Drinks.DrawOrderDrink(b));
                string key = x ?? "none";
                counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
            }
            Assert.InRange(counts["none"], 240, 360);
            foreach (BaseItemDef drink in Drinks.All) Assert.InRange(counts[drink.Id], 60, 140);
        }
    }
}
