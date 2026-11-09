using UnityEngine;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 029: marks a basket <see cref="Vessel"/> as basket <see cref="Basket"/> of the fryer <see cref="Fryer"/>. Data only (set
    /// by the scene builder); the server keys the basket's state by the vessel's object id (KitchenState), the client finds the
    /// basket a fryer button moves. The basket object itself never moves: its model is tweened (Art/BasketView).
    /// </summary>
    public sealed class FryerBasket : MonoBehaviour
    {
        [SerializeField] private Station _fryer;
        [SerializeField] private int _basket;

        public Station Fryer => _fryer;
        public int Basket => _basket;
    }
}
