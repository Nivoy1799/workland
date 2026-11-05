namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Number params: Value is the inclusive lower bound (the upper bound is the next bucket's Value,
    /// the last bucket runs up to ParamDef.Max). Choice params: Value is kind-specific (Cut: surface).
    /// Only the Id enters the item hash; Label is for display names.
    /// </summary>
    public sealed record BucketDef(string Id, float Value, string Label);
}
