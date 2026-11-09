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
        Bowl,
        /// <summary>Spec 029: a fryer basket. Fixed to its fryer (no Pickup): lowered into the oil and raised by the fryer's button,
        /// shaken with a click, holds one portion (<see cref="FryerBasket"/>, <see cref="FryerRules"/>).</summary>
        Basket,
        /// <summary>Spec 030: one spot on a griddle plate. Fixed to its griddle (no Pickup): food goes on, the spatula flips it, empty
        /// hands take it off; holds one item, each side cooks on its own (<see cref="GriddleSpot"/>, <see cref="GriddleRules"/>).</summary>
        Griddle,
        /// <summary>Spec 031: the build spot of an assembly board. Fixed to its board (no Pickup): food goes on as the next layer, empty
        /// hands fold a wrap or take the finished build (<see cref="AssemblySpot"/>, <see cref="BuildRules"/>).</summary>
        Assembly,
        /// <summary>Spec 036: the seitan cone on its spit. Fixed (no Pickup, no collider: the station's cone slot is aimed at): its heat
        /// bytes are the outer layer, its mode the portions left (<see cref="SpitPart"/>, <see cref="SpitRules"/>).</summary>
        SpitCone,
        /// <summary>Spec 036: the spit's catch tray. Fixed (no Pickup): holds up to <see cref="SpitRules.TrayCapacity"/> portions of shaved
        /// seitan; empty hands scoop one, a build in the hands gets one as its next layer.</summary>
        SpitTray
    }
}
