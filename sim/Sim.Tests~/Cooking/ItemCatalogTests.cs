using System;
using System.Collections.Generic;
using Worktown.Sim.Cooking;
using Xunit;
using static Worktown.Sim.Tests.Cooking.TestItems;

namespace Worktown.Sim.Tests.Cooking
{
    public class ItemCatalogTests
    {
        private static BaseItemDef WithStats(ItemStats stats)
            => new BaseItemDef("bad", "bad", stats, new DonenessWindow(0f, 1f), Array.Empty<string>());

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("a\nb")]
        [InlineData("Upper")]
        [InlineData("has space")]
        [InlineData("waytoolongidwaytoolongidwaytoolongid")]
        public void InvalidId_Throws(string? id)
        {
            var bad = new BaseItemDef(id!, "x", Salt.Stats, Salt.IdealDoneness, Array.Empty<string>());
            Assert.Throws<ArgumentException>(() => new ItemCatalog(new[] { bad }));
        }

        [Fact]
        public void NullElement_Throws()
        {
            Assert.Throws<ArgumentException>(() => new ItemCatalog(new BaseItemDef[] { Tomato, null! }));
        }

        [Fact]
        public void NullStats_Throws()
        {
            Assert.Throws<ArgumentException>(() => new ItemCatalog(new[] { WithStats(null!) }));
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        [InlineData(1.5f)]
        [InlineData(-0.1f)]
        public void InvalidStat_Throws(float value)
        {
            var bad = WithStats(new ItemStats(0.1f, 0.1f, 0.1f, 0.1f, value, 0.1f, 0.1f, 0.1f, 0f, 0.5f));
            Assert.Throws<ArgumentException>(() => new ItemCatalog(new[] { bad }));
            var badMoisture = WithStats(new ItemStats(0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0f, value));
            Assert.Throws<ArgumentException>(() => new ItemCatalog(new[] { badMoisture }));
        }

        [Fact]
        public void DuplicateId_Throws()
        {
            Assert.Throws<ArgumentException>(() => new ItemCatalog(new[] { Salt, Salt }));
        }

        [Fact]
        public void EmptyDisplayName_Throws()
        {
            var bad = new BaseItemDef("bad", " ", Salt.Stats, Salt.IdealDoneness, Array.Empty<string>());
            Assert.Throws<ArgumentException>(() => new ItemCatalog(new[] { bad }));
        }

        [Theory]
        [InlineData(0.8f, 0.2f)]
        [InlineData(-0.1f, 0.5f)]
        [InlineData(0.5f, 1.1f)]
        [InlineData(float.NaN, 0.5f)]
        public void InvalidWindow_Throws(float min, float max)
        {
            var bad = new BaseItemDef("bad", "bad", Salt.Stats, new DonenessWindow(min, max), Array.Empty<string>());
            Assert.Throws<ArgumentException>(() => new ItemCatalog(new[] { bad }));
        }

        [Fact]
        public void NullList_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ItemCatalog(null!));
        }

        [Fact]
        public void TryGet_FindsKnownAndRejectsUnknown()
        {
            Assert.True(Catalog.TryGet("potato", out var def));
            Assert.Equal("potato", def.Id);
            Assert.False(Catalog.TryGet("nope", out _));
            Assert.False(Catalog.TryGet(null, out _));
        }

        [Fact]
        public void OperationCatalog_DefaultHasSevenOperations() // spec 029: deep_fry
        {
            Assert.Equal(7, OperationCatalog.CreateDefault().Count);
        }

        [Fact]
        public void OperationCatalog_DuplicateIdOrBadHeatDefinitionThrows()
        {
            var cut = new OperationDef("cut", "board", OperationKind.Cut, false, 1, 1,
                new[] { new ParamDef("shape", ParamKind.Choice, 0f, 1f, new[] { new BucketDef("halves", 0.2f, "halves") }) },
                null, Array.Empty<string>(), Array.Empty<string>(), "{in}");
            Assert.Throws<ArgumentException>(() => new OperationCatalog(new[] { cut, cut }));
            var heatNoProfile = new OperationDef("fry", "pan", OperationKind.Heat, false, 1, 1, Array.Empty<ParamDef>(),
                null, Array.Empty<string>(), Array.Empty<string>(), "{in}");
            Assert.Throws<ArgumentException>(() => new OperationCatalog(new[] { heatNoProfile }));
        }
    }
}
