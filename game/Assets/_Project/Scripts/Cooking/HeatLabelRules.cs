namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 029b, pure: the label of food on the heat. Orchestrator decision in spec 030 (from the 029b review): the label never
    /// contradicts the item's name. It is the name the item would get if it were taken out now (KitchenState previews the take:
    /// "golden fries", "greasy golden fries" while it drains, "crispy fried potato sticks"); raw, never-heated food shows its sim
    /// name ("potato sticks"). So tipping or taking out never renames the item. The ring keeps showing the zone. On the
    /// griddle the preview name gets the side that is down ("half-grilled seitan strips · side 2 down").
    /// </summary>
    public static class HeatLabelRules
    {
        /// <summary>The label of food on the heat: exactly its preview name (what a take would give now).</summary>
        public static string Label(string previewName) => previewName ?? string.Empty;

        /// <summary>
        /// Spec 030: the label of food on a griddle spot: its preview name and which side is down ("grilled seitan strips ·
        /// side 1 down"); <paramref name="downSide"/> 0 is side 1.
        /// </summary>
        public static string GriddleLabel(string previewName, int downSide) =>
            Label(previewName) + " · " + GriddleRules.SideName(downSide) + " down";
    }
}
