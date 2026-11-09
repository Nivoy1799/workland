using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;
using Xunit;
using T = Worktown.Sim.Tests.Cooking.TestItems;

namespace Worktown.Sim.Tests.Economy
{
    /// <summary>Pins the spec 005 invariants the anti-faucet proof (spec 008) relies on.</summary>
    public class FaucetInvariantTests
    {
        private static IEnumerable<(string Op, Item[] Inputs, Item Output)> Cases()
        {
            var potato = T.Base("potato");
            var tomato = T.Base("tomato");
            var cashew = T.Base("cashew");
            var sticks = T.Ok(T.Cut(potato, "sticks"));
            var mixed = T.Ok(T.Mix(tomato, cashew));
            var fries = T.Fries();
            foreach (var x in new[] { potato, tomato, sticks, mixed, fries })
            {
                yield return ("cut", new[] { x }, T.Ok(T.Cut(x, "sticks")));
                yield return ("cut", new[] { x }, T.Ok(T.Cut(x, "halves")));
                yield return ("fry", new[] { x }, T.Ok(T.Fry(x, 190f, 150f)));
                yield return ("saute", new[] { x }, T.Ok(T.Heat("saute", x, 150f, 100f)));
                yield return ("boil", new[] { x }, T.Ok(T.Boil(x, 200f)));
                yield return ("plate", new[] { x }, T.Ok(T.Plate(x)));
            }
            yield return ("mix", new[] { potato, tomato }, T.Ok(T.Mix(potato, tomato)));
            yield return ("mix", new[] { sticks, mixed, fries }, T.Ok(T.Mix(sticks, mixed, fries)));
        }

        [Fact]
        public void WeightNeverDecreases()
        {
            foreach (var (op, inputs, output) in Cases())
                Assert.True(output.Weight >= inputs.Max(i => i.Weight), op);
        }

        [Fact]
        public void NonMixDepthIsOnePlusInputDepth()
        {
            foreach (var (op, inputs, output) in Cases().Where(c => c.Op != "mix" && c.Inputs[0].Hash != c.Output.Hash))
                Assert.Equal(1 + inputs.Max(i => i.Depth), output.Depth);
        }

        [Fact]
        public void CutNoOpKeepsHashAndYieldsNoByproducts()
        {
            var sticks = T.Ok(T.Cut(T.Base("potato"), "sticks"));
            var again = T.Cut(sticks, "halves");
            Assert.Equal(sticks.Hash, again.Item!.Hash);
            Assert.Equal(sticks.Depth, again.Item.Depth);
            Assert.Empty(Byproducts.ForOperation(TestEconomy.Catalog, "cut", new[] { sticks }, again));
        }
    }
}
