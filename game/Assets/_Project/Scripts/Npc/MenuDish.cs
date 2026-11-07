using System.Collections.Generic;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// A dish on the spec 022 menu: what it needs from the sim (ingredients with cut and heat, mixed or not, heated or
    /// not) and the NPC market it sells to. Pure data, immutable.
    /// </summary>
    public sealed class MenuDish
    {
        public MenuDish(byte id, string name, string market, string main, IReadOnlyList<DishNeed> needs,
            bool needsMix, bool noHeat, int maxIngredients)
        {
            Id = id;
            Name = name;
            Market = market;
            Main = main;
            Needs = needs;
            NeedsMix = needsMix;
            NoHeat = noHeat;
            MaxIngredients = maxIngredients;
        }

        /// <summary>Synced to clients (1..255; 0 is "no order").</summary>
        public byte Id { get; }
        public string Name { get; }

        /// <summary>The sim market its exact version sells to (meal: 2+ components, snack: 1).</summary>
        public string Market { get; }

        /// <summary>Main ingredient: without it the dish is always Wrong. Null when any ingredient will do.</summary>
        public string Main { get; }

        /// <summary>Named ingredients first, then "any other" ones.</summary>
        public IReadOnlyList<DishNeed> Needs { get; }

        /// <summary>The parts must be mixed (bowl, or a pot with several ingredients).</summary>
        public bool NeedsMix { get; }

        /// <summary>No part may be heated.</summary>
        public bool NoHeat { get; }

        /// <summary>At most this many different ingredients; more is one step wrong.</summary>
        public int MaxIngredients { get; }
    }
}
