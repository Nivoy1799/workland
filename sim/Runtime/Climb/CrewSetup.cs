namespace Worktown.Sim.Climb
{
    /// <summary>Spec 052: one simulated crew: size, profile (every player the same), strategy, seed and service timing.</summary>
    public sealed record CrewSetup(int Players, string ProfileId, ClimbStrategy Strategy, ulong Seed, int ServiceSeconds = 360,
        int BetweenSeconds = 60)
    {
        /// <summary>"4p average balanced+match seed 1".</summary>
        public string Label => Players + "p " + ProfileId + " " + Strategy.Id + " seed " + Seed;
    }
}
