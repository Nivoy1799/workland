namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 056a: how a menu route makes its dish. <see cref="Single"/>: each chain alone is the dish (alternatives, e.g. fries
    /// from the fryer or the pan). <see cref="Mix"/>: the chains together (one target of each, mixed or on one plate).
    /// <see cref="Build"/>: the layers of a recipe template on the assembly board. <see cref="Drink"/>: a cup of a drink.
    /// </summary>
    public enum RouteKind { Single, Mix, Build, Drink }
}
