using Worktown.Sim.Cooking;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 029b, pure: the label of food on the heat in player words that match its look, from its 027c zone (never the
    /// sim's time bucket ids such as "short"). Fries and falafel in the basket: "pale fries", "golden fries", "dark fries";
    /// anything in a pan or pot: "potato sticks · raw", "· warm", "· cooked", "· well done", "· burnt". Off the heat the
    /// label is the sim's display name (KitchenState.NameOf), unchanged.
    /// </summary>
    public static class HeatLabelRules
    {
        /// <summary>The pan and pot word of a zone: raw, warm, cooked (good and perfect), well done, burnt.</summary>
        public static string PanWord(HeatZone zone)
        {
            switch (zone)
            {
                case HeatZone.Cooking: return "warm";
                case HeatZone.Good:
                case HeatZone.Perfect: return "cooked";
                case HeatZone.Overdone: return "well done";
                case HeatZone.Burnt: return "burnt";
                default: return "raw";
            }
        }

        /// <summary>The deep fry word of a zone, as the sim names fried food: pale, golden (good and perfect), dark.</summary>
        public static string FryWord(HeatZone zone)
        {
            switch (zone)
            {
                case HeatZone.Good:
                case HeatZone.Perfect: return "golden";
                case HeatZone.Overdone:
                case HeatZone.Burnt: return "dark";
                default: return "pale";
            }
        }

        /// <summary>
        /// The label of <paramref name="foodName"/> on the heat in <paramref name="zone"/>. Deep-fried food the basket shapes
        /// (<paramref name="shapeName"/>: "fries", "falafel balls") reads "golden fries"; other deep-fried food
        /// "potato · golden"; pan and pot food "potato sticks · cooked".
        /// </summary>
        public static string Label(string foodName, bool deepFried, string shapeName, HeatZone zone)
        {
            if (deepFried)
                return string.IsNullOrEmpty(shapeName) ? foodName + " · " + FryWord(zone) : FryWord(zone) + " " + shapeName;
            return foodName + " · " + PanWord(zone);
        }

        /// <summary>The label of <paramref name="food"/> in the heat op <paramref name="opId"/> in <paramref name="zone"/>.</summary>
        public static string Label(Item food, string foodName, string opId, HeatZone zone)
        {
            bool deepFried = opId == DeepFry.OperationId;
            string shape = deepFried && DeepFry.TryGetShape(food, out DeepFryShape s) ? s.Name : null;
            return Label(foodName, deepFried, shape, zone);
        }
    }
}
