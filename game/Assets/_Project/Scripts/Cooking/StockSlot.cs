using UnityEngine;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 028b: one slot of a fridge or dry shelf (a static scene object, the same on server and clients). Its transform
    /// is the slot's world anchor (the middle of the items standing in it): the client aims at it like any interactable,
    /// the server measures reach to it. Clicking it sends RequestStation(station, <see cref="Option"/>); the server looks
    /// the slot up on the station again (<see cref="Station.TryGetSlot"/>) and decides. Its children are the item models
    /// shown in the slot (no colliders), so the highlight outlines exactly them. Data only, no rules.
    /// </summary>
    public sealed class StockSlot : MonoBehaviour
    {
        [SerializeField] private Station _station;
        [Tooltip("The station option that takes from this slot (its index in the station's slot list).")]
        [SerializeField] private int _option;
        [Tooltip("Index into DemoIngredients.All.")]
        [SerializeField] private int _ingredientIndex;

        public Station Station => _station;
        public int Option => _option;
        public int IngredientIndex => _ingredientIndex;
        public Vector3 Anchor => transform.position;

        /// <summary>The ingredient's id ("cucumber"), or null for an index outside <see cref="DemoIngredients.All"/>.</summary>
        public string IngredientId =>
            _ingredientIndex >= 0 && _ingredientIndex < DemoIngredients.All.Count ? DemoIngredients.All[_ingredientIndex].Id : null;
    }
}
