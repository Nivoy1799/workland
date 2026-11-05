namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Immutable flavor and texture profile, all values 0..1.
    /// Doneness: 0 raw .. 1 charred. Moisture: 0 dry .. 1 wet.
    /// </summary>
    public sealed record ItemStats(
        float Sweet, float Salty, float Sour, float Bitter, float Umami,
        float Fat, float Crunch, float HeatTolerance, float Doneness, float Moisture);
}
