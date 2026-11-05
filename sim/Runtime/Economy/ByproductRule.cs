namespace Worktown.Sim.Economy
{
    /// <summary>SourceId: operation id (Operation), market id (Sale), "" (Discard).</summary>
    public sealed record ByproductRule(ByproductTrigger Trigger, string SourceId, string GoodId,
        int FixedUnits, int UnitsPerComponent);
}
