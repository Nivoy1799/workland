using Worktown.Game.Cooking;

namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 020: what the local player holds, as plain data for <see cref="InteractionRules"/>. Only what clients
    /// know: the food's synced name and whether its coarse look is a finished plate; spec 021: for cookware its kind,
    /// how many things lie in it and the name of what it would give (the Vessel's synced contents).
    /// </summary>
    public readonly struct Held
    {
        public readonly bool IsHolding;
        public readonly bool IsFood;
        public readonly bool IsKnife;
        /// <summary>Food only: a finished plate (FoodLook kind plated).</summary>
        public readonly bool IsPlated;
        /// <summary>Name for prompts ("potato", "knife", "box", "pan"); empty when nothing is held.</summary>
        public readonly string Name;
        /// <summary>Spec 021: cookware (board, pan, pot, plate, bowl).</summary>
        public readonly bool IsVessel;
        public readonly VesselKind Vessel;
        /// <summary>Cookware: how many things lie in it.</summary>
        public readonly int ContentCount;
        /// <summary>Cookware: what pouring or selling it gives ("crispy fried potato sticks"), null when empty.</summary>
        public readonly string ContentName;
        /// <summary>Spec 030: the griddle's spatula (flips food on a griddle spot).</summary>
        public readonly bool IsSpatula;
        /// <summary>
        /// Spec 030 (029b review): an item still on its predicted way into the hands (a pick-up the server has not answered). It
        /// counts as held only for refusals (no take, no buy, no second pick-up): every action is None, so nothing that depends on
        /// the item (a tip, a pour, a place) is sent for something that may never arrive.
        /// </summary>
        public readonly bool IsPending;
        /// <summary>031b: food that is a build (open, folded, in paper, boxed or bagged; FoodLookRules.IsBuild): it goes back only
        /// on an empty board.</summary>
        public readonly bool IsBuild;

        private Held(bool holding, bool food, bool knife, bool plated, string name, bool vessel = false,
            VesselKind kind = default, int count = 0, string contentName = null, bool spatula = false, bool pending = false,
            bool build = false)
        {
            IsBuild = build;
            IsSpatula = spatula;
            IsPending = pending;
            IsHolding = holding;
            IsFood = food;
            IsKnife = knife;
            IsPlated = plated;
            Name = name ?? string.Empty;
            IsVessel = vessel;
            Vessel = kind;
            ContentCount = count < 0 ? 0 : count;
            ContentName = ContentCount > 0 && !string.IsNullOrEmpty(contentName) ? contentName : null;
        }

        public static Held Nothing => new Held(false, false, false, false, string.Empty);

        public static Held Food(string name, bool plated, bool build = false)
        {
            return new Held(true, true, false, plated, string.IsNullOrEmpty(name) ? "food" : name, build: build);
        }

        public static Held Knife => new Held(true, false, true, false, "knife");

        /// <summary>Spec 030: the griddle's spatula.</summary>
        public static Held Spatula => new Held(true, false, false, false, "spatula", spatula: true);

        /// <summary>Spec 030: <paramref name="item"/> on its way into the hands (a pending pick-up): held, but no action.</summary>
        public static Held Pending(Held item)
        {
            return new Held(true, item.IsFood, item.IsKnife, item.IsPlated, item.Name, item.IsVessel, item.Vessel, item.ContentCount,
                item.ContentName, item.IsSpatula, true, item.IsBuild);
        }

        /// <summary>Spec 021: a board, pan, pot, plate or bowl with <paramref name="count"/> things in it.</summary>
        public static Held Cookware(VesselKind kind, int count, string contentName)
        {
            return new Held(true, false, false, false, VesselRules.Name(kind), true, kind, count, contentName);
        }

        /// <summary>Anything else that can be carried (the spec 003 boxes).</summary>
        public static Held Item(string name)
        {
            return new Held(true, false, false, false, string.IsNullOrEmpty(name) ? "box" : name);
        }

        /// <summary>Cookware with something in it.</summary>
        public bool HasContents => IsVessel && ContentCount > 0;
    }
}
