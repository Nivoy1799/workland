namespace Worktown.Game.Npc
{
    /// <summary>
    /// Spec 032: how a served dish fits a guest's order (<see cref="OrderRules.Fit"/>): the match, the steps short of the order
    /// (layers missing or prepared wrong, the close or the pack, modifiers not kept; 0 for Exact), and the order's own quality and
    /// stars (a build is scored against the order's template, so a layer the guest did not want costs nothing). Server only.
    /// </summary>
    public readonly struct OrderFit
    {
        public OrderFit(OrderMatch match, int misses, float quality, int stars, string note = null)
        {
            Match = match;
            Misses = misses;
            Quality = quality;
            Stars = stars;
            Note = note;
        }

        /// <summary>
        /// Spec 036: what the review adds about how the dish was made (Sim.Cooking.Spit.ReviewNote: the seitan shaved off the spit or
        /// grilled strips), or null.
        /// </summary>
        public string Note { get; }

        /// <summary>The same fit with <paramref name="note"/>.</summary>
        public OrderFit WithNote(string note) => new OrderFit(Match, Misses, Quality, Stars, note);

        public OrderMatch Match { get; }
        public int Misses { get; }
        public float Quality { get; }
        public int Stars { get; }
    }
}
