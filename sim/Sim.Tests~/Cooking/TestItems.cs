using System;
using System.Collections.Generic;
using Worktown.Sim.Cooking;
using Xunit;

namespace Worktown.Sim.Tests.Cooking
{
    /// <summary>Shared fixtures: the 6 spec 002 ingredients plus potato and apple.</summary>
    internal static class TestItems
    {
        public static BaseItemDef Def(string id, float sweet, float salty, float sour, float bitter, float umami, float fat,
            float crunch = 0f, float heat = 0.5f)
            => new BaseItemDef(id, id, new ItemStats(sweet, salty, sour, bitter, umami, fat, crunch, heat, 0f, 0.5f),
                new DonenessWindow(0f, 1f), Array.Empty<string>());

        public static readonly BaseItemDef Salt = Def("salt", 0, 1, 0, 0, 0, 0);
        public static readonly BaseItemDef Tomato = Def("tomato", 0.4f, 0.1f, 0.5f, 0, 0.6f, 0.1f);
        public static readonly BaseItemDef Cheese = Def("cheese", 0.1f, 0.5f, 0.2f, 0.1f, 0.7f, 0.8f);
        public static readonly BaseItemDef Basil = Def("basil", 0.2f, 0, 0.1f, 0.4f, 0.2f, 0.1f, 0.3f);
        public static readonly BaseItemDef Oil = Def("oil", 0, 0, 0, 0.1f, 0, 1f);
        public static readonly BaseItemDef Onion = Def("onion", 0.5f, 0, 0.2f, 0.3f, 0.3f, 0, 0.6f);

        public static readonly BaseItemDef Potato = new BaseItemDef("potato", "potato",
            new ItemStats(0.1f, 0f, 0f, 0.1f, 0.1f, 0f, 0.5f, 0.6f, 0f, 0.8f),
            new DonenessWindow(0.55f, 0.85f), Array.Empty<string>());

        public static readonly BaseItemDef Apple = new BaseItemDef("apple", "apple",
            new ItemStats(0.6f, 0f, 0.3f, 0.05f, 0f, 0f, 0.7f, 0.3f, 0f, 0.85f),
            new DonenessWindow(0f, 0.3f), Array.Empty<string>());

        public static readonly ItemCatalog Catalog = new ItemCatalog(new[] { Salt, Tomato, Cheese, Basil, Oil, Onion, Potato, Apple });
        public static readonly ItemFactory Factory = new ItemFactory(Catalog, OperationCatalog.CreateDefault());

        public static Item Base(string id) => Ok(Factory.CreateBase(id));

        public static Item Ok(OperationResult r)
        {
            Assert.True(r.Ok, "unexpected error " + r.Error);
            return r.Item!;
        }

        public static IReadOnlyDictionary<string, ParamValue> NoParams { get; } = new Dictionary<string, ParamValue>();

        public static OperationResult Mix(params Item[] inputs) => Factory.Apply("mix", inputs, NoParams);
        public static OperationResult Plate(params Item[] inputs) => Factory.Apply("plate", inputs, NoParams);

        public static OperationResult Cut(Item input, string shape) =>
            Factory.Apply("cut", new[] { input }, new Dictionary<string, ParamValue> { ["shape"] = ParamValue.Choice(shape) });

        public static OperationResult Heat(string op, Item input, float temp, float time) =>
            Factory.Apply(op, new[] { input }, new Dictionary<string, ParamValue>
            {
                ["temp"] = ParamValue.Number(temp),
                ["time"] = ParamValue.Number(time),
            });

        public static OperationResult Fry(Item input, float temp, float time) => Heat("fry", input, temp, time);

        public static OperationResult Boil(Item input, float time) =>
            Factory.Apply("boil", new[] { input }, new Dictionary<string, ParamValue> { ["time"] = ParamValue.Number(time) });

        public static Item Fries() => Ok(Fry(Ok(Cut(Base("potato"), "sticks")), 190f, 150f));

        public static ItemRating Rate(params BaseItemDef[] single)
        {
            var f = new ItemFactory(new ItemCatalog(single), OperationCatalog.CreateDefault());
            return ItemRater.Rate(f.CreateBase(single[0].Id).Item!);
        }

        public static IEnumerable<T[]> Permutations<T>(T[] items)
        {
            if (items.Length <= 1) { yield return items; yield break; }
            for (int i = 0; i < items.Length; i++)
            {
                var rest = new List<T>(items);
                rest.RemoveAt(i);
                foreach (var p in Permutations(rest.ToArray()))
                {
                    var r = new T[items.Length];
                    r[0] = items[i];
                    p.CopyTo(r, 1);
                    yield return r;
                }
            }
        }
    }
}
