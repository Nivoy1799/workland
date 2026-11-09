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
        FoodSauceChili
    }
}
