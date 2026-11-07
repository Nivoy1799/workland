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
        ToolKnife
    }
}
