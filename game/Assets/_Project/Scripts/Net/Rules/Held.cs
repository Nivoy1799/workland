namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 020: what the local player holds, as plain data for <see cref="InteractionRules"/>. Only what clients
    /// know: the food's synced name and whether its coarse look is a finished plate.
    /// </summary>
    public readonly struct Held
    {
        public readonly bool IsHolding;
        public readonly bool IsFood;
        public readonly bool IsKnife;
        /// <summary>Food only: a finished plate (FoodLook kind plated).</summary>
        public readonly bool IsPlated;
        /// <summary>Name for prompts ("potato", "knife", "box"); empty when nothing is held.</summary>
        public readonly string Name;

        private Held(bool holding, bool food, bool knife, bool plated, string name)
        {
            IsHolding = holding;
            IsFood = food;
            IsKnife = knife;
            IsPlated = plated;
            Name = name ?? string.Empty;
        }

        public static Held Nothing => new Held(false, false, false, false, string.Empty);

        public static Held Food(string name, bool plated)
        {
            return new Held(true, true, false, plated, string.IsNullOrEmpty(name) ? "food" : name);
        }

        public static Held Knife => new Held(true, false, true, false, "knife");

        /// <summary>Anything else that can be carried (the spec 003 boxes).</summary>
        public static Held Item(string name)
        {
            return new Held(true, false, false, false, string.IsNullOrEmpty(name) ? "box" : name);
        }
    }
}
