namespace Worktown.Game.Cooking
{
    public enum StationKind : byte
    {
        Crate,
        Board,
        Pan,
        Pot,
        Counter,
        /// <summary>Spec 013: put items on a plate, take them as one plated dish.</summary>
        PlatingTable,
        /// <summary>Spec 013: put items in a bowl, take them as one mix.</summary>
        Bowl
    }
}
