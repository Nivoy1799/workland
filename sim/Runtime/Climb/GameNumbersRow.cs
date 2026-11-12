namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052b: the stage 1 and 2 numbers a /game spec applies for a crew of <see cref="Players"/> (from
    /// <see cref="GameNumbers.For"/>). Amounts are game money (the catalog's money times the game scale; spec 037: cents, scale 1).
    /// The D40 factors (permille of the 4-player crew): h(n), guest demand, goal. Goal, pitch fee, start, stage 2 rent and the
    /// stage 2 licence are this crew's real amounts; <see cref="StockPerService"/> is the supplier's drop (0 = no cap).
    /// <see cref="MedianServices"/> and <see cref="TenthsOfHours"/> are the reference runs' stage 1 at the game's pace
    /// (-1 when stage 1 was not completed).
    /// </summary>
    public sealed record GameNumbersRow(int Players, int DifficultyPermille, int DemandPermille, int GoalPermille, long GoalGame,
        int StockPerService, long PitchFeeGame, long StartGame, long RentGame, long LicenseGame, int MedianServices, int TenthsOfHours);
}
