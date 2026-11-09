namespace Worktown.Sim.Cooking
{
    /// <summary>Spec 032: what a guest changes about a recipe: leave a layer off ("no lettuce") or more of it ("extra garlic").</summary>
    public enum ModifierKind : byte
    {
        Remove = 1,
        Extra = 2,
    }
}
