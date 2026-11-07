namespace Worktown.Game.Art
{
    /// <summary>
    /// Visual slots of the swap layer (spec 015). The first seven values mirror
    /// <c>StationKind</c> in the same order (a test checks this); new values are appended.
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
        FoodPot
    }
}
