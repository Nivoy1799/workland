namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 029: what the fryer's basket shapes an input into: potato sticks become "fries", one falafel mix portion becomes
    /// four "falafel balls". <see cref="CutShape"/> null means any cut (or none).
    /// </summary>
    public sealed record DeepFryShape(string BaseId, string? CutShape, string Name, int Pieces);
}
