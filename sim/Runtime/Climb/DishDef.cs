namespace Worktown.Sim.Climb
{
    /// <summary>Spec 052: a dish of the climb model: actions to make it, ingredient cost, reference price, base quality.</summary>
    public sealed record DishDef(string Id, string Name, int Actions, long IngredientCost, long ReferencePrice, int BaseQualityPermille);
}
