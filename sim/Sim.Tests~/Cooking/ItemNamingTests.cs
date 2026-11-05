using System.Collections.Generic;
using Worktown.Sim.Cooking;
using Xunit;
using static Worktown.Sim.Tests.Cooking.TestItems;

namespace Worktown.Sim.Tests.Cooking
{
    public class ItemNamingTests
    {
        private sealed class StubSource : IItemNameSource
        {
            private readonly string _name;
            public int Calls { get; private set; }
            public StubSource(string name) { _name = name; }
            public bool TryGetName(string itemHash, out string name) { Calls++; name = _name; return true; }
        }

        [Fact]
        public void AC10_FallbackName()
        {
            Assert.Equal("crispy fried potato sticks", ItemNaming.FallbackName(Fries(), NameCatalog.Empty));
            Assert.Equal("potato", ItemNaming.FallbackName(Base("potato"), NameCatalog.Empty));
        }

        [Fact]
        public void AC10_CatalogBeatsSourceAndSourceBeatsFallback()
        {
            var fries = Fries();
            var catalog = new NameCatalog(new[] { new NamedItem(fries.Hash, "fries") });
            var source = new StubSource("from source");

            Assert.Equal("fries", ItemNaming.Resolve(fries, catalog, source));
            Assert.Equal(0, source.Calls);

            Assert.Equal("from source", ItemNaming.Resolve(fries, NameCatalog.Empty, source));
            Assert.Equal(1, source.Calls);

            Assert.Equal("crispy fried potato sticks", ItemNaming.Resolve(fries, NameCatalog.Empty, null));
        }

        [Fact]
        public void FallbackName_UsesCatalogNamesOfInputsAndJoinsLists()
        {
            var fries = Fries();
            var catalog = new NameCatalog(new[] { new NamedItem(fries.Hash, "fries") });
            var two = Ok(Mix(fries, Base("apple")));
            var three = Ok(Mix(Base("salt"), Base("oil"), Base("onion")));
            // Input names follow canonical (hash) order, so derive the expectation from Inputs.
            string N(Item i) => i.BaseId ?? "fries";
            Assert.Equal($"{N(two.Inputs[0])} and {N(two.Inputs[1])} mix", ItemNaming.FallbackName(two, catalog));
            Assert.Equal($"{N(three.Inputs[0])}, {N(three.Inputs[1])} and {N(three.Inputs[2])} mix",
                ItemNaming.FallbackName(three, NameCatalog.Empty));
            Assert.Equal("plate of fries", ItemNaming.FallbackName(Ok(Plate(fries)), catalog));
        }

        [Fact]
        public void FallbackName_OtherTemplates()
        {
            var potato = Base("potato");
            Assert.Equal("lightly boiled potato", ItemNaming.FallbackName(Ok(Boil(potato, 100f)), NameCatalog.Empty));
            Assert.Equal("boiled potato", ItemNaming.FallbackName(Ok(Boil(potato, 600f)), NameCatalog.Empty));
            Assert.Equal("potato cubes", ItemNaming.FallbackName(Ok(Cut(potato, "dice")), NameCatalog.Empty));
            Assert.Equal("dark sauteed potato", ItemNaming.FallbackName(Ok(Heat("saute", potato, 150f, 300f)), NameCatalog.Empty));
        }

        [Fact]
        public void NameCatalog_RejectsDuplicatesAndEmptyNames()
        {
            Assert.Throws<System.ArgumentException>(() => new NameCatalog(new[] { new NamedItem("h", "a"), new NamedItem("h", "b") }));
            Assert.Throws<System.ArgumentException>(() => new NameCatalog(new[] { new NamedItem("h", "") }));
        }

        [Fact]
        public void Naming_DoesNotChangeRating()
        {
            var fries = Fries();
            var before = ItemRater.Rate(fries);
            ItemNaming.Resolve(fries, new NameCatalog(new[] { new NamedItem(fries.Hash, "fries") }), new StubSource("x"));
            Assert.Equal(before, ItemRater.Rate(fries));
        }
    }
}
