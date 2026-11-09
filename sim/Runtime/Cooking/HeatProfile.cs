namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// How a heat op changes stats. Spec 029: with <see cref="Fry"/> the components' doneness follows their
    /// <see cref="FryCurve"/> instead of the dose formula (the deep fryer), and the op takes "shake" and "drain" choices.
    /// Spec 030: with <see cref="Grill"/> each of an item's two sides follows its curve and the doneness combines them
    /// (<see cref="Griddle"/>); the op takes "time" (the shorter side) and "long_side".
    /// </summary>
    public sealed record HeatProfile(
        float? FixedTemperatureC, float FatGain, float MoistureDelta,
        float CrunchGain, float UmamiGain, float Dilution, DeepFryProfile? Fry = null, GriddleProfile? Grill = null);
}
