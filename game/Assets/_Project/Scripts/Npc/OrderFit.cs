namespace Worktown.Game.Npc
{
    /// <summary>
    /// Spec 032: how a served dish fits a guest's order (<see cref="OrderRules.Fit"/>): the match, the steps short of the order
    /// (layers missing or prepared wrong, the close or the pack, modifiers not kept; 0 for Exact), and the order's own quality and
    /// stars (a build is scored against the order's template, so a layer the guest did not want costs nothing). Server only.
    /// </summary>
    public readonly struct OrderFit
    {
        public OrderFit(OrderMatch match, int misses, float quality, int stars)
        {
            Match = match;
            Misses = misses;
            Quality = quality;
            Stars = stars;
        }

        public OrderMatch Match { get; }
        public int Misses { get; }
        public float Quality { get; }
        public int Stars { get; }
    }
}
