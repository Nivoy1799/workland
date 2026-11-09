namespace Worktown.Sim.Economy
{
    /// <summary>
    /// Spec 034: one paid order on its way: <see cref="Packs"/> boxes of <see cref="UnitsPerPack"/> stock units of
    /// <see cref="GoodId"/>, paid <see cref="Cost"/> by <see cref="PayerId"/> at game minute <see cref="PlacedMinute"/>, due at the back
    /// door at <see cref="DueMinute"/>.
    /// </summary>
    public sealed record SupplyOrder(int Id, string GoodId, int Packs, int UnitsPerPack, long Cost, string PayerId, long PlacedMinute,
        long DueMinute);
}
