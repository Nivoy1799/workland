namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 032: one change a guest asks for on a recipe template: <see cref="Kind"/> on the template layer
    /// <see cref="LayerIndex"/> (an index into <see cref="RecipeTemplate.Layers"/>), shown as <see cref="Word"/> ("lettuce",
    /// "garlic"). Only the entries of <see cref="OrderModifiers.Allowed"/> exist.
    /// </summary>
    public sealed record OrderModifier(ModifierKind Kind, int LayerIndex, string Word)
    {
        /// <summary>Plain words for the ticket and the bubble: "no lettuce", "extra garlic".</summary>
        public string Text => (Kind == ModifierKind.Remove ? "no " : "extra ") + Word;
    }
}
