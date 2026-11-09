namespace Worktown.Game.Art
{
    /// <summary>
    /// Visual slots of the swap layer (spec 015). The first seven values are the spec 015 station models;
    /// <c>ArtCatalogRules.StationKey</c> maps each station kind to one explicitly (spec 021: the stove uses
    /// StationPan, the plate shelf StationPlatingTable). New values are appended.
    /// </summary>
    public enum ArtKey : byte
    {
        StationCrate,
        StationBoard,
        StationPan,
        StationPot,
        StationCounter,
        StationPlatingTable,
        StationBowl,
        Ground,
        Road,
        RoadCrossroad,
        Building,
        BuildingShop,
        BuildingHouseA,
        BuildingHouseC,
        BuildingHouseF,
        BuildingHouseK,
        Prop,
        PropTreeSmall,
        PropTreeLarge,
        PropPlanter,
        PropLamp,
        PropDumpster,
        PropTrashcan,
        PropFence,
        PropAwning,

        // Spec 017: food models (whole, per-ingredient cuts, generic tinted pieces) and containers.
        FoodPotato,
        FoodApple,
        FoodTomato,
        FoodOnion,
        FoodAppleHalf,
        FoodOnionHalf,
        FoodTomatoSlice,
        FoodFries,
        FoodPieceHalf,
        FoodPieceSlice,
        FoodPieceStick,
        FoodPieceCube,
        FoodPlate,
        FoodBowl,
        FoodPan,
        FoodPot,

        // Spec 019: the cutting board on the board station and the knife (Food Kit).
        ToolCuttingBoard,
        ToolKnife,

        // Spec 023: the kitchen cabinet fitted to a 1 x 1 x 1 m box (the scene builder stretches it to the worktop size).
        Worktop,

        // Spec 024, spec 025: people at 1.75 m (Kenney Animated Characters body, Humanoid). Players use the two Player
        // keys, customers the two Customer keys (see ArtCatalogRules.PlayerCharacterKeys / CustomerCharacterKeys).
        CharacterPlayerMale,
        CharacterPlayerFemale,
        CharacterCustomerMale,
        CharacterCustomerFemale,
        // Spec 027d: more customer looks (same body and skins, recoloured shirt, trousers and hair; see CustomerLooks).
        CharacterCustomerMale2,
        CharacterCustomerFemale2,
        CharacterCustomerMale3,
        CharacterCustomerFemale3,

        // Spec 028: the street-food stock (Kenney Food Kit models, tinted where the kit has no exact one).
        FoodFlatbread,
        FoodWrap,
        FoodSeitan,
        FoodFalafelMix,
        FoodLettuce,
        FoodCucumber,
        FoodRedCabbage,
        FoodLemon,
        FoodLemonHalf,
        FoodSauceGarlicHerb,
        FoodSauceChili,

        // Spec 028b: the stock stations (graybox: the Furniture Kit in the repo has no fridge and no bookcase).
        StationFridge,
        StationShelf,

        // Spec 029: the deep fryer (graybox: steel body, oil wells), its wire basket, a falafel ball and the empty fries box.
        StationFryer,
        ToolFryerBasket,
        FoodFalafelBall,
        PropFriesBox,

        // Spec 030: the griddle (graybox: a steel plate on a cabinet), its spatula (Food Kit cooking-spatula) and the grill vegetables.
        StationGriddle,
        ToolSpatula,
        FoodPaprika,
        FoodEggplant,
        FoodMushroom,

        // Spec 031: the assembly board (graybox), a flat layer (a bread base or a sauce), the folded wrap and the paper-wrapped one
        // (tinted primitives), and the Kenney takeaway box (styrofoam) and bag.
        StationAssembly,
        FoodLayerDisc,
        FoodWrapFolded,
        FoodWrapPaper,
        PropTakeawayBox,
        PropPaperBag,
        /// <summary>Spec 032: the register on the counter (graybox, RegisterArtRules).</summary>
        StationRegister,
        /// <summary>Spec 033: the drink dispenser on the front counter (graybox, DrinkArtRules) and the takeaway cup (Kenney cup).</summary>
        StationDrinkDispenser,
        PropCup,
        /// <summary>Spec 034: the ordering terminal on the back wall (graybox, TerminalArtRules) and the delivery box (the Kenney
        /// cardboard box of the Furniture Kit).</summary>
        StationTerminal,
        PropDeliveryBox
    }
}
