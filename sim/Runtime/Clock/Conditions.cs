namespace Worktown.Sim.Clock
{
    /// <summary>Demand multipliers in permille (1000 = unchanged). The four demand fields match spec 064 inputs.</summary>
    public sealed record Conditions(
        int StreetPermille, int OutdoorSeatsPermille, int IndoorPermille, int DeliveryPermille, int DeliveryTimePermille);
}
