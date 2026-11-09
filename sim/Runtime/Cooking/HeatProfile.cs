namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// How a heat op changes stats. Spec 029: with <see cref="Fry"/> the components' doneness follows their
    /// <see cref="FryCurve"/> instead of the dose formula (the deep fryer), and the op takes "shake" and "drain" choices.
    /// </summary>
    public sealed record HeatProfile(
        float? FixedTemperatureC, float FatGain, float MoistureDelta,
        float CrunchGain, float UmamiGain, float Dilution, DeepFryProfile? Fry = null);
}
