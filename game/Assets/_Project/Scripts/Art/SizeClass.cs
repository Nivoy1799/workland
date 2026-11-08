namespace Worktown.Game.Art
{
    /// <summary>Spec 026: the size class of an art key (D29): real size times the class factor.</summary>
    public enum SizeClass : byte
    {
        /// <summary>People, furniture, buildings, streets: real size (x1.0).</summary>
        World,
        /// <summary>Cookware and tools: real size x1.15.</summary>
        Cookware,
        /// <summary>Small items (real size under 15 cm: food, pieces): real size x1.5.</summary>
        Small
    }
}
