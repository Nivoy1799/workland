namespace Worktown.Sim.Climb
{
    /// <summary>Spec 052: a fixed price rule: reference -1, +0, +1, +2 units, or the counter rival's price -1 unit.</summary>
    public enum PricePolicy
    {
        Minus1,
        Match,
        Plus1,
        Plus2,
        Undercut,
    }
}
