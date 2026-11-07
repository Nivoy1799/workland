namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 021: cookware you can carry. Each one is a normal pickup with a <see cref="Vessel"/> component;
    /// the server keys what lies in it by the vessel's object id (KitchenState).
    /// </summary>
    public enum VesselKind : byte
    {
        Board,
        Pan,
        Pot,
        Plate,
        Bowl
    }
}
