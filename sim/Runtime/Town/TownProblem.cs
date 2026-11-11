namespace Worktown.Sim.Town
{
    /// <summary>Spec 064: one failed check of <see cref="TownCheck"/>: the check id ("C1".."C12") and what failed.</summary>
    public sealed record TownProblem(string Check, string Detail);
}
