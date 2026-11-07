namespace Worktown.Game.Npc
{
    /// <summary>One customer in the queue (spec 014): a network object id and what it wants.</summary>
    public readonly struct QueuedCustomer
    {
        public QueuedCustomer(int id, string wish)
        {
            Id = id;
            Wish = wish;
        }

        public int Id { get; }
        /// <summary>Market id: meal, snack or drink.</summary>
        public string Wish { get; }
    }
}
