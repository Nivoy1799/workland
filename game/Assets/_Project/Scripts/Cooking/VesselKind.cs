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
        Griddle
    }
}
