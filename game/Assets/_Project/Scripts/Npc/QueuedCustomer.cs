namespace Worktown.Game.Npc
{
    /// <summary>One customer in the queue (spec 014): a network object id, what it wants and (spec 022) its order.</summary>
    public readonly struct QueuedCustomer
    {
        public QueuedCustomer(int id, string wish, byte dish = Menu.None)
        {
            Id = id;
            Wish = wish;
            Dish = dish;
        }

        public int Id { get; }
        /// <summary>Market id: meal, snack or drink.</summary>
        public string Wish { get; }
        /// <summary>Spec 022: the ordered menu dish id (<see cref="Menu.None"/> if none).</summary>
        public byte Dish { get; }
    }
}
