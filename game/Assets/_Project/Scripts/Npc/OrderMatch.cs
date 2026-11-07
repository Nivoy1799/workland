namespace Worktown.Game.Npc
{
    /// <summary>How well a served dish fits a customer's order (spec 022).</summary>
    public enum OrderMatch
    {
        /// <summary>Everything the dish needs is there: full price plus a tip for stars.</summary>
        Exact = 0,
        /// <summary>Right main ingredient, one step wrong or missing: 60 %.</summary>
        Close = 1,
        /// <summary>Anything else: 25 %, the customer leaves unhappy.</summary>
        Wrong = 2,
    }
}
