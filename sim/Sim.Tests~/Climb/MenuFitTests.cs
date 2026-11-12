using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Climb;
using Xunit;
using T = Worktown.Sim.Tests.Climb.TestClimb;

namespace Worktown.Sim.Tests.Climb
{
    /// <summary>Spec 052b (a): the crew chooses its menu (AC4), on hand-built states of the default catalog.</summary>
    public class MenuFitTests
    {
        private static readonly ClimbCatalog D = ClimbCatalog.CreateDefault();
        private static readonly string[] AllDishes = { "dish_lemonade", "dish_falafel_wrap", "dish_seitan_wrap", "dish_grilled_veggies" };

        private static ClimbState Menu(IReadOnlyList<string> active, params int[] missed) =>
            T.State() with
            {
                Stage = ClimbStage.ServiceMix,
                Owned = AllDishes,
                ActiveMenu = active,
                RecentMissed = missed,
            };

        private static readonly string[] Full = { "fries", "lemonade", "falafel_wrap", "seitan_wrap", "grilled_veggies" };

        [Fact]
        public void DropsTheDishWithTheMostActionsAbove100Permille() // AC4
        {
            (IReadOnlyList<string> active, string? dropped, string? added) = MenuFit.Next(D, Menu(Full, 101));
            Assert.Equal("seitan_wrap", dropped); // 11 actions
            Assert.Null(added);
            Assert.Equal(new[] { "fries", "lemonade", "falafel_wrap", "grilled_veggies" }, active);
            Assert.Null(MenuFit.Next(D, Menu(Full, 100)).Dropped); // exactly 100 is not above
        }

        [Fact]
        public void NeverDropsTheStage1DishAndNeverEmpties() // AC4
        {
            (IReadOnlyList<string> active, string? dropped, _) = MenuFit.Next(D, Menu(new[] { "fries" }, 900));
            Assert.Null(dropped);
            Assert.Equal(new[] { "fries" }, active);
            (active, dropped, _) = MenuFit.Next(D, Menu(new[] { "fries", "lemonade" }, 900));
            Assert.Equal("lemonade", dropped); // fries has more actions but stays
            Assert.Equal(new[] { "fries" }, active);
            // The stage 1 dish is active even when the list forgets it.
            Assert.Equal("fries", MenuFit.Active(D, Menu(new[] { "lemonade" })).First().Id);
        }

        [Fact]
        public void TiesDropTheLastIdAndAddTheFirstId()
        {
            var twins = new[]
            {
                new DishDef("chips", "Chips", 4, 1, 5, 600), new DishDef("soup", "Soup", 6, 2, 8, 700), new DishDef("stew", "Stew", 6, 2, 8, 700),
            };
            var purchases = T.Purchases.Concat(new[]
            {
                new PurchaseDef("stew_recipe", "Stew Recipe", ClimbStage.FirstRestaurant, PurchaseKind.Productive, 30, 0, PurchaseEffect.None with { UnlocksDish = "stew" }),
            });
            var stages = T.Stages.Select(s => s.Stage == ClimbStage.FirstRestaurant ? s with { MaxDishes = 3 } : s);
            ClimbCatalog c = T.Build(dishes: twins, purchases: purchases, stages: stages);
            ClimbState s = T.State(ClimbStage.FirstRestaurant, 0, "soup_recipe", "stew_recipe") with { ActiveMenu = new[] { "chips", "soup", "stew" }, RecentMissed = new[] { 200 } };
            Assert.Equal("stew", MenuFit.Next(c, s).Dropped);
            ClimbState back = s with { ActiveMenu = new[] { "chips" }, RecentMissed = new[] { 0, 0, 0 } };
            Assert.Equal("soup", MenuFit.Next(c, back).Added);
        }

        [Fact]
        public void ReAddsTheFewestActionDishAfterThreeQuietServices() // AC4
        {
            string[] lean = { "fries", "falafel_wrap" };
            (IReadOnlyList<string> active, string? dropped, string? added) = MenuFit.Next(D, Menu(lean, 0, 10, 29));
            Assert.Null(dropped);
            Assert.Equal("lemonade", added); // 3 actions
            Assert.Equal(new[] { "fries", "lemonade", "falafel_wrap" }, active); // buying order of the owned menu
            Assert.Null(MenuFit.Next(D, Menu(lean, 0, 10, 30)).Added); // 30 is not under 30
            Assert.Null(MenuFit.Next(D, Menu(lean, 0, 10)).Added); // only two services seen
            Assert.Null(MenuFit.Next(D, Menu(Full, 0, 0, 0)).Added); // nothing inactive
        }

        [Fact]
        public void AtMostOneChangePerServiceAndNeverAboveMaxDishes() // AC4
        {
            ClimbState s = Menu(new[] { "fries" }, 0, 0, 0);
            (ClimbState a, string? none, string? first) = MenuFit.Apply(D, s);
            Assert.Null(none);
            Assert.Equal("lemonade", first);
            Assert.Empty(a.RecentMissed); // 052b review: a change clears the window
            (ClimbState b, string? d2, string? a2) = MenuFit.Apply(D, a);
            Assert.Null(d2);
            Assert.Null(a2); // no chained re-add without three fresh quiet services
            var seen = new List<int>();
            s = b;
            for (int k = 0; k < 4; k++)
            {
                (ClimbState next, string? dropped, string? added) = MenuFit.Apply(D, s with { RecentMissed = new[] { 0, 0, 0 } });
                Assert.False(dropped != null && added != null);
                Assert.True(next.ActiveMenu.Count <= D.StageOf(ClimbStage.ServiceMix).MaxDishes);
                Assert.True(next.ActiveMenu.Count - s.ActiveMenu.Count <= 1);
                seen.Add(next.ActiveMenu.Count);
                s = next;
            }
            Assert.Equal(new[] { 3, 4, 5, 5 }, seen);
        }

        [Fact]
        public void RecordKeepsTheLastThree()
        {
            Assert.Equal(new[] { 2, 3, 4 }, MenuFit.Record(new[] { 1, 2, 3 }, 4));
            Assert.Equal(new[] { 7 }, MenuFit.Record(new int[0], 7));
        }

        [Fact]
        public void ABoughtDishIsActiveAtOnceAndGuestsOrderOnlyActiveDishes()
        {
            ClimbState s = T.State(ClimbStage.FirstRestaurant, 200);
            var (afterBuying, _, _) = ClimbPolicy.Decide(T.Catalog, s, T.Setup());
            Assert.Contains("soup_recipe", afterBuying.Owned);
            Assert.Contains("soup", afterBuying.ActiveMenu);
            ClimbState chipsOnly = T.WithPrices(afterBuying with { ActiveMenu = new[] { "chips" } });
            Assert.Equal(new[] { "chips" }, MenuFit.Active(T.Catalog, chipsOnly).Select(d => d.Id));
            ClimbEngine e = new ClimbEngine(T.Build(rivals: new RivalDef[0], weights: T.Weights with { Outside = 0 }));
            ClimbState x = chipsOnly with { Rivals = new RivalState[0] };
            for (int k = 0; k < 3; k++)
            {
                (ClimbState n, _) = e.RunService(x with { RecentMissed = new[] { 500 } }, T.Setup(3));
                Assert.Equal(new[] { "chips" }, n.ActiveMenu); // never re-added while busy
                x = n;
            }
        }
    }
}
