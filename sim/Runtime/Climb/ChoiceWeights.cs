namespace Worktown.Sim.Climb
{
    /// <summary>Spec 052: the weights of a guest's choice between shops (see <see cref="GuestChoice.Attraction"/>).</summary>
    public sealed record ChoiceWeights(int Quality, int Reputation, int Price, int Wait, int Variety, int Outside, int Min);
}
