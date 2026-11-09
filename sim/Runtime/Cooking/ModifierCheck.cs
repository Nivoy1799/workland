namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 032: how a build fits an order (a recipe template with modifiers, <see cref="OrderModifiers.Check"/>): the template score
    /// against the order's template (a layer the guest said "no" to is not missing; an "extra" sauce may be lots) and the modifiers
    /// not kept (<see cref="Mistakes"/>: the removed layer is there anyway, the extra is not). <see cref="Misses"/> counts every
    /// step short of the order, as the order match does: missing layers, layers prepared wrong, the close and the pack short, the
    /// modifier mistakes. Layer order and the sauce amount only cost score, never a miss (031).
    /// </summary>
    public sealed record ModifierCheck(TemplateScore Score, int Mistakes)
    {
        public int Misses => Score.Missing + Score.WrongPrep + Score.CloseShort + Score.PackShort + Mistakes;
    }
}
