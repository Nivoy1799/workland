namespace Worktown.Sim.Climb
{
    /// <summary>Spec 052: a repeatable stage 5 big job that replaces one normal service.</summary>
    public sealed record BigJobDef(string Id, string Name, BigJobKind Kind, long Deposit, int Guests, int PricePermille,
        int RivalScorePermille, long Prize, string? RequiresPurchase, int CooldownServices);
}
