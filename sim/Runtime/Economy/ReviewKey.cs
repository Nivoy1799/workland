namespace Worktown.Sim.Economy
{
    /// <summary>
    /// Spec 032: what a review says, as a key (the AI gateway writes the text later; until then <see cref="ReviewRules.TemplateText"/>).
    /// The first five follow the stars, the others name the main complaint. Synced as a byte, so values are only appended.
    /// </summary>
    public enum ReviewKey : byte
    {
        Great = 0,
        Good = 1,
        Fine = 2,
        Meh = 3,
        Bad = 4,
        Slow = 5,
        OrderOff = 6,
        WrongOrder = 7,
        WrongChange = 8,
        /// <summary>Spec 035: the food was fine, but the shop was dirty (the hygiene score below HygieneRules.ReviewBelow).</summary>
        Dirty = 9,
    }
}
