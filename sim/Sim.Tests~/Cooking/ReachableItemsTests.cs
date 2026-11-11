using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Worktown.Sim.Cooking;
using Xunit;

namespace Worktown.Sim.Tests.Cooking
{
    /// <summary>Spec 056a: the items the shipped menu can produce, and the reachable-items file.</summary>
    public class ReachableItemsTests
    {
        // Pinned: cooking data or route changes move these. See PinnedMessage.
        private const int PinnedCount = 714;
        private const string PinnedSetHash = "d88c6ba25d0dd9a1";
        private const string PinnedMessage =
            "reachable set changed: update the pin and rerun scripts/item-list.sh and bake-names.sh (spec 056)";

        private const string FriesFixture = "fry(temp=hot,time=medium;cut(shape=sticks;potato))";

        // The spec 006 request rules, copied from ai-gateway/src/Worktown.Gateway/Api/RequestValidator.cs.
        private static readonly Regex ItemHashRegex = new Regex(@"^[0-9a-f]{64}\z", RegexOptions.CultureInvariant);
        private static readonly Regex CanonicalTreeRegex = new Regex(@"^[a-z0-9_(),;=]{1,2048}\z", RegexOptions.CultureInvariant);
        private static readonly Regex FallbackNameRegex = new Regex(@"^[A-Za-z0-9 ,'-]{1,200}\z", RegexOptions.CultureInvariant);
        private static readonly Regex TagRegex = new Regex(@"^[a-z0-9_]{1,32}\z", RegexOptions.CultureInvariant);
        private const int MaxTags = 32;

        private static readonly Lazy<IReadOnlyList<ReachableItem>> Shipped = new Lazy<IReadOnlyList<ReachableItem>>(Run);

        private static ItemFactory Factory() => new ItemFactory(KitchenItems.CreateItemCatalog(), OperationCatalog.CreateDefault());

        private static IReadOnlyList<ReachableItem> Run() => ReachableItems.Enumerate(Factory(), MenuRoutes.Default, ReachLimits.Default);

        private static IReadOnlyDictionary<string, string> Target(params string[] pairs)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i + 1 < pairs.Length; i += 2) map[pairs[i]] = pairs[i + 1];
            return map;
        }

        private static RouteDef OneChain(string id, RouteKind kind, string baseId, params RouteStep[] steps) =>
            new RouteDef(id, id, kind, new[] { new RouteChain(new[] { baseId }, steps) }, null);

        [Fact]
        public void EnumerateIsDeterministic()
        {
            string first = ReachableItemsText.Format(Shipped.Value);
            string second = ReachableItemsText.Format(Run());
            Assert.Equal(first, second);
        }

        [Fact]
        public void CountAndSetHashArePinned()
        {
            IReadOnlyList<ReachableItem> items = Shipped.Value;
            Assert.True(items.Count == PinnedCount, PinnedMessage + $" (count {items.Count}, pinned {PinnedCount})");
            string set = ReachableItemsText.SetHash(items);
            Assert.True(set == PinnedSetHash, PinnedMessage + $" (set hash {set}, pinned {PinnedSetHash})");
        }

        [Fact]
        public void StaysUnderMaxItems()
        {
            Assert.InRange(Shipped.Value.Count, 1, ReachLimits.Default.MaxItems);
        }

        [Fact]
        public void ContainsTheSpec005FriesFixtureAndBurntFries()
        {
            IReadOnlyList<ReachableItem> items = Shipped.Value;
            ReachableItem fries = Assert.Single(items, i => i.CanonicalTree == FriesFixture);
            Assert.True(fries.IsTarget);
            Assert.Equal(new[] { MenuRoutes.Fries, MenuRoutes.VeggieBox }, fries.Routes);
            Assert.Equal(new[] { "crispy" }, fries.LabelWords);
            Assert.Equal("crispy fried potato sticks", fries.FallbackName);
            Assert.Contains(items, i => i.CanonicalTree == "fry(temp=hot,time=long;cut(shape=sticks;potato))" && !i.IsTarget);
            Assert.Contains(items, i => i.CanonicalTree == "deep_fry(drain=drained,shake=none,temp=hot,time=long;cut(shape=sticks;potato))");
            Assert.Contains(items, i => i.CanonicalTree == "potato"); // the base item too
        }

        [Fact]
        public void EveryRouteHasATargetItem()
        {
            foreach (RouteDef route in MenuRoutes.Default)
                Assert.True(Shipped.Value.Any(i => i.IsTarget && i.Routes.Contains(route.Id)), "no target for " + route.Id);
        }

        [Fact]
        public void ItemsAreSortedByHashWithSortedRoutes()
        {
            IReadOnlyList<ReachableItem> items = Shipped.Value;
            for (int i = 1; i < items.Count; i++) Assert.True(string.CompareOrdinal(items[i - 1].Hash, items[i].Hash) < 0);
            foreach (ReachableItem item in items)
            {
                Assert.NotEmpty(item.Routes);
                for (int r = 1; r < item.Routes.Count; r++) Assert.True(string.CompareOrdinal(item.Routes[r - 1], item.Routes[r]) < 0);
            }
        }

        [Fact]
        public void EveryItemPassesTheSpec006RequestRules()
        {
            foreach (ReachableItem item in Shipped.Value)
            {
                Assert.Matches(ItemHashRegex, item.Hash);
                Assert.Matches(CanonicalTreeRegex, item.CanonicalTree);
                int depth = 0;
                foreach (char c in item.CanonicalTree)
                {
                    if (c == '(') depth++;
                    else if (c == ')') Assert.True(--depth >= 0, item.CanonicalTree);
                }
                Assert.Equal(0, depth);
                Assert.Matches(FallbackNameRegex, item.FallbackName);
                Assert.True(item.Tags.Count <= MaxTags);
                for (int t = 0; t < item.Tags.Count; t++)
                {
                    Assert.Matches(TagRegex, item.Tags[t]);
                    if (t > 0) Assert.True(string.CompareOrdinal(item.Tags[t - 1], item.Tags[t]) < 0);
                }
                Assert.Null(ContentRules.FindForbidden(item.FallbackName));
            }
        }

        [Fact]
        public void BuildsReachEveryPrefixAndTheirCloseAndPack()
        {
            IReadOnlyList<ReachableItem> items = Shipped.Value;
            Assert.Contains(items, i => i.FallbackName == "seitan wrap in paper" && i.IsTarget);
            Assert.Contains(items, i => i.FallbackName == "seitan flatbread in a bag" && i.IsTarget);
            Assert.Contains(items, i => i.FallbackName == "veggie box" && i.IsTarget && i.CanonicalTree.StartsWith("pack(", StringComparison.Ordinal));
            Assert.Contains(items, i => i.FallbackName == "falafel wrap in paper" && i.IsTarget);
            Assert.Contains(items, i => i.CanonicalTree == "assemble(;wrap)" && !i.IsTarget); // the first prefix: an open build of one layer
            Assert.Contains(items, i => i.FallbackName.StartsWith("folded ", StringComparison.Ordinal) && !i.IsTarget);
        }

        [Fact]
        public void DrinksReachTheCupEveryFillAndEveryLid()
        {
            IReadOnlyList<ReachableItem> items = Shipped.Value;
            Assert.Contains(items, i => i.CanonicalTree == Drinks.CupId);
            foreach (string level in new[] { Drinks.Low, Drinks.Right, Drinks.Full, Drinks.Spilled })
            {
                Assert.Contains(items, i => i.CanonicalTree == $"fill(ml={level};lemonade)");
                ReachableItem lid = Assert.Single(items, i => i.CanonicalTree == $"lid(;fill(ml={level};lemonade))");
                Assert.Equal(level == Drinks.Right, lid.IsTarget);
            }
        }

        [Fact]
        public void TheFourArgumentOverloadWithTheDefaultOpsGivesTheSameItems()
        {
            IReadOnlyList<ReachableItem> items = ReachableItems.Enumerate(Factory(), OperationCatalog.CreateDefault(),
                MenuRoutes.Default, ReachLimits.Default);
            Assert.Equal(ReachableItemsText.Format(Shipped.Value), ReachableItemsText.Format(items));
        }

        [Fact]
        public void ARouteWhoseTargetFailsThrows()
        {
            var unreachable = OneChain("bad", RouteKind.Single, "potato", new RouteStep("boil", Target("time", "burnt")));
            Assert.Throws<InvalidOperationException>(() => ReachableItems.Enumerate(Factory(), new[] { unreachable }, ReachLimits.Default));
            var unknownParam = OneChain("bad", RouteKind.Single, "potato", new RouteStep("boil", Target("temp", "hot")));
            Assert.Throws<InvalidOperationException>(() => ReachableItems.Enumerate(Factory(), new[] { unknownParam }, ReachLimits.Default));
            var unknownOp = OneChain("bad", RouteKind.Single, "potato", new RouteStep("bake", Target()));
            Assert.Throws<InvalidOperationException>(() => ReachableItems.Enumerate(Factory(), new[] { unknownOp }, ReachLimits.Default));
            var unknownBase = OneChain("bad", RouteKind.Single, "pumpkin");
            Assert.Throws<InvalidOperationException>(() => ReachableItems.Enumerate(Factory(), new[] { unknownBase }, ReachLimits.Default));
            var noTemplate = new RouteDef("bad", "bad", RouteKind.Build, new[] { new RouteChain(new[] { "wrap" }, Array.Empty<RouteStep>()) }, "pizza");
            Assert.Throws<InvalidOperationException>(() => ReachableItems.Enumerate(Factory(), new[] { noTemplate }, ReachLimits.Default));
            // A build without a chain for a required layer (the seitan wrap's filling) is wrong.
            var missingLayer = new RouteDef("bad", "bad", RouteKind.Build, new[] { new RouteChain(new[] { "wrap" }, Array.Empty<RouteStep>()) },
                RecipeTemplates.SeitanWrapId);
            Assert.Throws<InvalidOperationException>(() => ReachableItems.Enumerate(Factory(), new[] { missingLayer }, ReachLimits.Default));
        }

        [Fact]
        public void MoreThanMaxItemsThrows()
        {
            Assert.Throws<InvalidOperationException>(() => ReachableItems.Enumerate(Factory(), MenuRoutes.Default, new ReachLimits(48, 10)));
        }

        [Fact]
        public void BadArgumentsThrow()
        {
            Assert.Throws<ArgumentNullException>(() => ReachableItems.Enumerate(null!, MenuRoutes.Default, ReachLimits.Default));
            Assert.Throws<ArgumentNullException>(() => ReachableItems.Enumerate(Factory(), null!, ReachLimits.Default));
            Assert.Throws<ArgumentNullException>(() => ReachableItems.Enumerate(Factory(), MenuRoutes.Default, null!));
            Assert.Throws<ArgumentNullException>(() => ReachableItems.Enumerate(Factory(), null!, MenuRoutes.Default, ReachLimits.Default));
            Assert.Throws<ArgumentException>(() => ReachableItems.Enumerate(Factory(), MenuRoutes.Default, new ReachLimits(0, 10)));
            var dup = OneChain("x", RouteKind.Single, "potato");
            Assert.Throws<ArgumentException>(() => ReachableItems.Enumerate(Factory(), new[] { dup, dup }, ReachLimits.Default));
        }

        [Fact]
        public void ASingleRouteSweepsEveryBucketAndPlatesItsTarget()
        {
            var route = OneChain("soup", RouteKind.Single, "potato", new RouteStep("boil", Target("time", "medium")));
            IReadOnlyList<ReachableItem> items = ReachableItems.Enumerate(Factory(), new[] { route }, ReachLimits.Default);
            Assert.Equal(
                new[] { "boil(time=long;potato)", "boil(time=medium;potato)", "boil(time=short;potato)", "plate(;boil(time=medium;potato))", "potato" },
                items.Select(i => i.CanonicalTree).OrderBy(t => t, StringComparer.Ordinal).ToArray());
            Assert.Equal(new[] { "boil(time=medium;potato)", "plate(;boil(time=medium;potato))" },
                items.Where(i => i.IsTarget).Select(i => i.CanonicalTree).OrderBy(t => t, StringComparer.Ordinal).ToArray());
            Assert.Equal(new[] { "soft" }, items.Single(i => i.CanonicalTree == "boil(time=long;potato)").LabelWords);
        }

        [Fact]
        public void AMixRouteCombinesDistinctBasesOnly()
        {
            var route = new RouteDef("mix", "mix", RouteKind.Mix, new[]
            {
                new RouteChain(new[] { "onion" }, Array.Empty<RouteStep>()),
                new RouteChain(new[] { "onion", "tomato" }, Array.Empty<RouteStep>()),
            }, null);
            IReadOnlyList<ReachableItem> items = ReachableItems.Enumerate(Factory(), new[] { route }, ReachLimits.Default);
            // onion, tomato, and for onion + tomato only: the mix, the mix plated, both on a plate.
            Assert.Equal(5, items.Count);
            Assert.Equal(3, items.Count(i => i.IsTarget));
            Assert.DoesNotContain(items, i => i.CanonicalTree.Contains("onion,onion"));
        }

        [Fact]
        public void MenuRoutesHaveTheMenuNamesAndDrinks()
        {
            string[] names =
            {
                "Fries", "Potato soup", "Onion soup", "Tomato salad", "Fried apple", "Veggie mix", "Falafel (4)", "Grilled veggies",
                "Toasted flatbread", "Seitan wrap", "Falafel wrap", "Seitan flatbread", "Veggie box", "Lemonade", "Iced tea", "Soda",
            };
            Assert.Equal(names, MenuRoutes.Default.Select(r => r.Name).ToArray());
            Assert.Equal(MenuRoutes.Default.Count, MenuRoutes.Default.Select(r => r.Id).Distinct().Count());
            foreach (RouteDef route in MenuRoutes.Default)
                Assert.Equal(route.Kind == RouteKind.Build, route.TemplateId != null && RecipeTemplates.ById(route.TemplateId) != null);
        }

        [Fact]
        public void ReachLimitsDefaults()
        {
            Assert.Equal(48, ReachLimits.Default.SweepSteps);
            Assert.Equal(2500, ReachLimits.Default.MaxItems);
        }

        // ---- ReachableItemsText ----

        private static ReachableItem Fake(char hex, string name = "plain potato", string tree = "potato") =>
            new ReachableItem(new string(hex, 64), tree, name, new[] { "plated" }, new[] { "fries" }, true, new[] { "crispy" });

        [Fact]
        public void FormatWritesTheSpecLayout()
        {
            string text = ReachableItemsText.Format(new[] { Fake('b'), Fake('a') });
            Assert.DoesNotContain("\r", text);
            Assert.EndsWith("}\n", text);
            Assert.StartsWith("{\n  \"format\": \"worktown-reachable-items\",\n  \"formatVersion\": 1,\n  \"hashVersion\": \"v1\",\n  \"setHash\": \"", text);
            Assert.Contains("\"setHash\": \"" + ReachableItemsText.SetHash(new[] { Fake('a'), Fake('b') }) + "\"", text);
            Assert.True(text.IndexOf(new string('a', 64), StringComparison.Ordinal) < text.IndexOf(new string('b', 64), StringComparison.Ordinal));
            Assert.Contains("{ \"hash\": \"" + new string('a', 64) + "\", \"canonicalTree\": \"potato\", \"fallbackName\": \"plain potato\", "
                + "\"tags\": [\"plated\"], \"routes\": [\"fries\"], \"target\": true, \"labelWords\": [\"crispy\"] }", text);
            Assert.All(text, c => Assert.True(c == '\n' || (c >= 0x20 && c <= 0x7E)));
        }

        [Fact]
        public void FormatOfNoItemsIsAnEmptyList()
        {
            Assert.EndsWith("\"items\": []\n}\n", ReachableItemsText.Format(Array.Empty<ReachableItem>()));
        }

        [Fact]
        public void SetHashIsTheFirst16HexOfTheSortedHashes()
        {
            string a = ReachableItemsText.SetHash(new[] { Fake('a'), Fake('b') });
            Assert.Equal(a, ReachableItemsText.SetHash(new[] { Fake('b'), Fake('a') }));
            Assert.Matches("^[0-9a-f]{16}$", a);
            Assert.NotEqual(a, ReachableItemsText.SetHash(new[] { Fake('a') }));
            using var sha = System.Security.Cryptography.SHA256.Create();
            byte[] digest = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(new string('a', 64) + "\n" + new string('b', 64)));
            Assert.Equal(string.Concat(digest.Select(b => b.ToString("x2"))).Substring(0, 16), a);
        }

        [Fact]
        public void FormatRejectsBadItems()
        {
            Assert.Throws<ArgumentException>(() => ReachableItemsText.Format(new[] { Fake('a', "pommes frites à point") }));
            Assert.Throws<ArgumentException>(() => ReachableItemsText.Format(new[] { Fake('a', "line\nbreak") }));
            Assert.Throws<ArgumentException>(() => ReachableItemsText.Format(new[] { Fake('A') }));
            Assert.Throws<ArgumentException>(() => ReachableItemsText.Format(new[] { Fake('a'), Fake('a') }));
            Assert.Throws<ArgumentNullException>(() => ReachableItemsText.Format(null!));
            Assert.Contains("\\\"", ReachableItemsText.Format(new[] { Fake('a', "say \"hi\"") }));
        }
    }
}
