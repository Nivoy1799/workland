namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 031: how a build fits a recipe template (<see cref="RecipeTemplates.Score"/>). Counts: required layers missing, layers
    /// there but prepared wrong (raw falafel), pairs of layers out of order, sauce layers with the wrong amount, the close and the
    /// pack steps short of the template (0 or 1 each). Factor = 1 minus the penalties (at least <see cref="RecipeTemplates.MinFactor"/>);
    /// Quality, Taste and DonenessFactor are rated from the layers the template claimed (extras are neutral).
    /// </summary>
    public sealed record TemplateScore(RecipeTemplate Template, float Quality, float Taste, float DonenessFactor, float Factor,
        int Missing, int WrongPrep, int OutOfOrder, int SauceOff, int CloseShort, int PackShort, int Claimed)
    {
        /// <summary>Every required layer is there (prepared right or not).</summary>
        public bool HasAllLayers => Missing == 0;
    }
}
