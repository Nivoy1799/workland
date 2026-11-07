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
        PropAwning
    }
}
