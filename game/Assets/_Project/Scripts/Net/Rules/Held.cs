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
        /// <summary>Spec 033: a cup from the drink dispenser (empty, filled or lidded; FoodLookRules.IsCup).</summary>
        public readonly bool IsCup;
        /// <summary>Spec 033, a cup: it has its lid (finished: it goes only to the counter).</summary>
        public readonly bool CupLidded;
        /// <summary>Spec 033, a cup: the tap of its drink (DrinkRules), -1 for an empty cup.</summary>
        public readonly int CupTap;
        /// <summary>Spec 033, a cup: its level (DrinkRules.LevelEmpty .. LevelSpilled), as this client sees it.</summary>
        public readonly byte CupLevel;
        /// <summary>Spec 034: a delivery box: the stock line it holds (Shop.StockCatalog), -1 for no box.</summary>
        public readonly int BoxLine;
        /// <summary>Spec 034, a delivery box: the units in it.</summary>
        public readonly int BoxUnits;
        /// <summary>Spec 035: a dirty dish from the dish return (it goes into the sink only).</summary>
        public readonly bool IsDirtyDish;
        /// <summary>Spec 035: a trash bag taken out of the can (it goes into the bin outside).</summary>
        public readonly bool IsTrashBag;
        /// <summary>Spec 035: the wipe cloth (held on a spill, it wipes it).</summary>
        public readonly bool IsCloth;

        private Held(bool holding, bool food, bool knife, bool plated, string name, bool vessel = false,
            VesselKind kind = default, int count = 0, string contentName = null, bool spatula = false, bool pending = false,
            bool build = false, bool cup = false, bool lidded = false, int cupTap = -1, byte cupLevel = 0, int boxLine = -1, int boxUnits = 0,
            bool dirty = false, bool bag = false, bool cloth = false)
        {
            IsDirtyDish = dirty;
            IsTrashBag = bag;
            IsCloth = cloth;
            BoxLine = boxLine < 0 ? -1 : boxLine;
            BoxUnits = BoxLine >= 0 ? boxUnits : 0;
            IsCup = cup;
            CupLidded = cup && lidded;
            CupTap = cup ? cupTap : -1;
            CupLevel = cup ? cupLevel : (byte)0;
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
                item.ContentName, item.IsSpatula, true, item.IsBuild, item.IsCup, item.CupLidded, item.CupTap, item.CupLevel, item.BoxLine,
                item.BoxUnits, item.IsDirtyDish, item.IsTrashBag, item.IsCloth);
        }

        /// <summary>Spec 035: a dirty dish from the dish return.</summary>
        public static Held DirtyDish => new Held(true, false, false, false, "dirty plate", dirty: true);

        /// <summary>Spec 035: a trash bag.</summary>
        public static Held TrashBag => new Held(true, false, false, false, "trash bag", bag: true);

        /// <summary>Spec 035: the wipe cloth.</summary>
        public static Held Cloth => new Held(true, false, false, false, "cloth", cloth: true);

        /// <summary>Spec 035: what the trash takes from the hands: food (a cup too) or cookware with food in it.</summary>
        public bool IsDiscardable => IsFood || HasContents;

        /// <summary>Spec 034: a delivery box of <paramref name="units"/> units of stock line <paramref name="line"/>, named for the prompt.</summary>
        public static Held Box(string name, int line, int units)
        {
            return new Held(true, false, false, false, string.IsNullOrEmpty(name) ? "box" : name, boxLine: line, boxUnits: units);
        }

        /// <summary>Spec 034: the hands carry a delivery box.</summary>
        public bool IsBox => BoxLine >= 0;

        /// <summary>
        /// Spec 033: a cup from the drink dispenser: its drink's tap (-1 empty), level and lid, from its coarse look. A lidded cup is
        /// finished (<see cref="IsPlated"/>): it goes only to the counter.
        /// </summary>
        public static Held Cup(string name, int tap, byte level, bool lidded)
        {
            return new Held(true, true, false, lidded, string.IsNullOrEmpty(name) ? "cup" : name, cup: true, lidded: lidded,
                cupTap: tap, cupLevel: level);
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
