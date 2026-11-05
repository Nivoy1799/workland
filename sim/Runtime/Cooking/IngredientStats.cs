namespace Worktown.Sim.Cooking
{
    /// <summary>Immutable flavor and texture profile, all values 0..1.</summary>
    public sealed record IngredientStats(
        float Sweet, float Salty, float Sour, float Bitter, float Umami,
        float Fat, float Crunch, float HeatTolerance);
}
