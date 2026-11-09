namespace Worktown.Game.Npc
{
    /// <summary>One customer in the queue (spec 014): a network object id, what it wants and (spec 022) its order; spec 032: the
    /// order's modifiers, packed (OrderModifiers.Pack for the dish's recipe template; 0 = none); spec 033: the order's drink line
    /// (Cooking.DrinkRules.DrinkByte: 0 = none).</summary>
    public readonly struct QueuedCustomer
    {
        public QueuedCustomer(int id, string wish, byte dish = Menu.None, byte modifiers = 0, byte drink = 0)
        {
            Id = id;
            Wish = wish;
            Dish = dish;
            Modifiers = modifiers;
            Drink = drink;
        }

        /// <summary>Spec 032: the packed modifiers of the order (0 = none).</summary>
        public byte Modifiers { get; }

        /// <summary>Spec 033: the drink the guest ordered with its dish (DrinkRules.DrinkByte; 0 = none).</summary>
        public byte Drink { get; }

        public int Id { get; }
        /// <summary>Market id: meal, snack or drink.</summary>
        public string Wish { get; }
        /// <summary>Spec 022: the ordered menu dish id (<see cref="Menu.None"/> if none).</summary>
        public byte Dish { get; }
    }
}
