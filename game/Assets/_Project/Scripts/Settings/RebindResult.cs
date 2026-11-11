namespace Worktown.Game.Settings
{
    /// <summary>Spec 048: a rebind's outcome and, for a swap, the action that had the key or button before.</summary>
    public readonly struct RebindResult
    {
        public RebindResult(RebindOutcome outcome, ControlAction? other = null)
        {
            Outcome = outcome;
            Other = other;
        }

        public RebindOutcome Outcome { get; }
        /// <summary>The conflicting action (only for <see cref="RebindOutcome.Swapped"/>).</summary>
        public ControlAction? Other { get; }
        /// <summary>The binding table changed.</summary>
        public bool Changed => Outcome == RebindOutcome.Bound || Outcome == RebindOutcome.Swapped || Outcome == RebindOutcome.Cleared;
    }
}
