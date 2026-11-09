namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// 031b, pure: when a client's cached list of scene objects (fryer baskets, griddle spots, build spots) is looked up again. A
    /// scene NetworkObject stays inactive on a client until the server spawns it, so an empty first look must not stick: the list
    /// is refilled when it was never filled, when a member was destroyed, and while it is empty (at most every
    /// <see cref="EmptyRetrySeconds"/>).
    /// </summary>
    public static class CacheRules
    {
        /// <summary>s between two looks while the list is empty.</summary>
        public const float EmptyRetrySeconds = 1f;

        /// <summary>Whether to look the list up again now.</summary>
        public static bool ShouldRefill(bool neverFilled, bool memberDestroyed, int count, float now, float nextEmptyLookup) =>
            neverFilled || memberDestroyed || (count == 0 && now >= nextEmptyLookup);
    }
}
