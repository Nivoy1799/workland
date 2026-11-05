namespace Worktown.Sim.Cooking
{
    public sealed record HeatProfile(
        float? FixedTemperatureC, float FatGain, float MoistureDelta,
        float CrunchGain, float UmamiGain, float Dilution);
}
