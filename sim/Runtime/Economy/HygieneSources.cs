namespace Worktown.Sim.Economy
{
    /// <summary>
    /// Spec 035: what makes the shop dirty right now, as the server counts it (pure value): every dirty dish (at the dish return, in
    /// someone's hands, lying about or waiting in the sink), how many of them were left out longer than
    /// <see cref="HygieneRules.DishGraceMinutes"/>, a full trash (the can at its capacity or a full bag not taken out yet), the open
    /// spills and whether something burns on the heat (the smell).
    /// </summary>
    public readonly struct HygieneSources
    {
        public HygieneSources(int dirtyDishes, int overdueDishes, bool trashFull, int spills, bool burning)
        {
            DirtyDishes = dirtyDishes < 0 ? 0 : dirtyDishes;
            OverdueDishes = overdueDishes < 0 ? 0 : overdueDishes > DirtyDishes ? DirtyDishes : overdueDishes;
            TrashFull = trashFull;
            Spills = spills < 0 ? 0 : spills;
            Burning = burning;
        }

        /// <summary>Nothing dirty at all.</summary>
        public static HygieneSources Clean => default;

        public int DirtyDishes { get; }
        public int OverdueDishes { get; }
        public bool TrashFull { get; }
        public int Spills { get; }
        public bool Burning { get; }

        /// <summary>No source at all (not even a fresh dirty dish): only then does the score recover.</summary>
        public bool IsClean => DirtyDishes == 0 && !TrashFull && Spills == 0 && !Burning;

        public override string ToString() =>
            "dishes " + DirtyDishes + " (" + OverdueDishes + " overdue), trash " + (TrashFull ? "full" : "ok") + ", spills " + Spills
            + (Burning ? ", burning" : string.Empty);
    }
}
