namespace Worktown.Game.Climb
{
    /// <summary>Spec 057: what a service end changed on the stage (SaveService picks its save trigger from it).</summary>
    public enum StageEnd : byte
    {
        /// <summary>Nothing beyond the service's own numbers.</summary>
        None,
        /// <summary>The stage's goal was reached (stage 1: the rent goal; stage 2: the licence is within reach).</summary>
        GoalReached,
        /// <summary>The crew moved to the next stage (the rent was paid during the service).</summary>
        Moved
    }
}
