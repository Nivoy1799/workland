namespace Worktown.Game.Art
{
    /// <summary>Spec 026: which dimension the target size of an art key measures.</summary>
    public enum SizeAxis : byte
    {
        /// <summary>The largest dimension (food, cookware, tools, props).</summary>
        Largest,
        /// <summary>The height (buildings, trees, people, furniture).</summary>
        Height
    }
}
