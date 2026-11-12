using System;
using System.Collections.Generic;
using Worktown.Game.Economy;
using Worktown.Game.Npc;
using Worktown.Sim.Economy;

namespace Worktown.Game.Office
{
    /// <summary>
    /// Spec 059, pure: who changed what at the office terminal or the menu board, as packed entries (synced as OfficeTerminal.Log, the last
    /// <see cref="Kept"/>, not saved): the player slot (5 bits, the connection id; <see cref="UnknownSlot"/> for one past 30), the action
    /// (6 bits) and its argument (16 bits). Drawn as "Mira: fries $4.50", "Tobi: falafel wrap is the special"; the name comes from the lobby
    /// roster, an unknown slot reads "A cook".
    /// </summary>
    public static class ChangeLog
    {
        public const int Kept = 8;
        public const int UnknownSlot = 31;
        public const string UnknownName = "A cook";

        /// <summary>A price step: the argument is the line (5 bits) and the new price in $0.50 steps (11 bits).</summary>
        public const int PriceSet = 1;
        /// <summary>The special: the argument is the dish's line + 1 (0: no special).</summary>
        public const int SpecialSet = 2;
        /// <summary>A dish on or off the next menu: the argument is its line.</summary>
        public const int DishOn = 3;
        public const int DishOff = 4;
        /// <summary>Spec 062b: the shop's door: the argument is the new ShopDoorState.</summary>
        public const int DoorSet = 5;
        /// <summary>The highest action this build draws.</summary>
        public const int LastAction = DoorSet;

        private const int PriceBits = 11;

        /// <summary>The entry of <paramref name="action"/> by <paramref name="playerSlot"/> with <paramref name="argument"/> (each cut to its bits).</summary>
        public static uint Pack(int playerSlot, int action, int argument)
        {
            uint slot = (uint)(playerSlot < 0 || playerSlot > UnknownSlot ? UnknownSlot : playerSlot);
            return slot << 22 | (uint)(action & 0x3F) << 16 | (uint)(argument & 0xFFFF);
        }

        public static void Unpack(uint entry, out int playerSlot, out int action, out int argument)
        {
            playerSlot = (int)(entry >> 22 & 0x1F);
            action = (int)(entry >> 16 & 0x3F);
            argument = (int)(entry & 0xFFFF);
        }

        /// <summary>A price entry's argument: the line and the price in $0.50 steps.</summary>
        public static int PriceArgument(int line, long cents) =>
            (Math.Max(0, Math.Min(31, line)) << PriceBits) | (int)Math.Max(0L, Math.Min((1L << PriceBits) - 1, cents / MenuPricing.StepCents));

        public static uint Price(int playerSlot, int line, long cents) => Pack(playerSlot, PriceSet, PriceArgument(line, cents));

        public static uint Special(int playerSlot, byte dish)
        {
            int line = MenuPrices.LineOfDish(dish);
            return Pack(playerSlot, SpecialSet, line >= 0 ? line + 1 : 0);
        }

        /// <summary>A 057 recipe row (its dish, or the drinks) on or off the next menu.</summary>
        public static uint Dish(int playerSlot, int recipeRow, bool on) => Pack(playerSlot, on ? DishOn : DishOff, Math.Max(0, recipeRow));

        /// <summary>Spec 062b: the door opened, closed or set to close after the service.</summary>
        public static uint Door(int playerSlot, Worktown.Game.Layout.ShopDoorState state) => Pack(playerSlot, DoorSet, (int)state);

        /// <summary><paramref name="log"/> with <paramref name="entry"/> appended, the oldest dropped past <see cref="Kept"/>.</summary>
        public static uint[] Push(IReadOnlyList<uint> log, uint entry)
        {
            var list = new List<uint>();
            if (log != null) foreach (uint e in log) list.Add(e);
            list.Add(entry);
            while (list.Count > Kept) list.RemoveAt(0);
            return list.ToArray();
        }

        /// <summary>
        /// The words of <paramref name="entry"/>: "Mira: fries $4.50", "Tobi: falafel wrap is the special", "Lu: no special today",
        /// "Mira: falafel wrap on the menu next", "Lu: drinks off the menu next". <paramref name="nameOf"/> names a slot (null or empty: "A cook").
        /// </summary>
        public static string Text(uint entry, Func<int, string> nameOf)
        {
            Unpack(entry, out int slot, out int action, out int argument);
            string name = slot == UnknownSlot || nameOf == null ? null : nameOf(slot);
            if (string.IsNullOrEmpty(name)) name = UnknownName;
            return ResultsRules.Fit(name + ": " + What(action, argument));
        }

        private static string What(int action, int argument)
        {
            switch (action)
            {
                case PriceSet:
                {
                    int line = argument >> PriceBits;
                    long cents = (argument & ((1 << PriceBits) - 1)) * MenuPricing.StepCents;
                    return LineName(line) + " " + EconomyRules.Money(cents);
                }
                case SpecialSet:
                    return argument == 0 ? SpecialRules.NoSpecialText.ToLowerInvariant() : LineName(argument - 1) + " is the special";
                case DishOn: return RecipeName(argument) + " on the menu next";
                case DishOff: return RecipeName(argument) + " off the menu next";
                case DoorSet: // spec 062b
                    return argument == (int)Worktown.Game.Layout.ShopDoorState.Closed ? "closed the shop"
                        : argument == (int)Worktown.Game.Layout.ShopDoorState.ClosingAfterService ? "closes after this service" : "opened the shop";
                default: return "changed something";
            }
        }

        /// <summary>A recipe row's dish as the goal board names it ("falafel wrap", "drinks").</summary>
        private static string RecipeName(int row)
        {
            IReadOnlyList<Worktown.Sim.Climb.PurchaseDef> recipes = Worktown.Game.Climb.RestaurantRules.Recipes(Worktown.Game.Climb.FoodCornerRules.DefaultClimb);
            return row >= 0 && row < recipes.Count ? Worktown.Game.Climb.GoalBoardRules.DishName(recipes[row]).ToLowerInvariant() : "a dish";
        }

        private static string LineName(int line) =>
            line >= 0 && line < MenuPrices.Lines.Count ? MenuPrices.Lines[line].Name.ToLowerInvariant() : "a dish";

        /// <summary>The name of <paramref name="slot"/> in a roster (ids and names in the same order); null when it is not there.</summary>
        public static string NameIn(int slot, IReadOnlyList<int> ids, IReadOnlyList<string> names)
        {
            if (ids == null || names == null) return null;
            for (int i = 0; i < ids.Count && i < names.Count; i++)
                if ((ids[i] < 0 || ids[i] > UnknownSlot ? UnknownSlot : ids[i]) == slot) return names[i];
            return null;
        }

        /// <summary>The slot of a connection id (its id while 0 to 30, else <see cref="UnknownSlot"/>).</summary>
        public static int SlotOf(int clientId) => clientId < 0 || clientId >= UnknownSlot ? UnknownSlot : clientId;
    }
}
