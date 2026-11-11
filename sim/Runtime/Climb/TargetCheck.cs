namespace Worktown.Sim.Climb
{
    /// <summary>Spec 052: one target line of the report (T1..T8).</summary>
    public sealed record TargetCheck(string Id, bool Passed, string Detail)
    {
        /// <summary>"PASS T1 4p average S2 1.1 h in 0.75-1.5".</summary>
        public string Line => (Passed ? "PASS " : "FAIL ") + Id + " " + Detail;
    }
}
