namespace Worktown.Game.Player
{
    /// <summary>Spec 027b: how an item's model eases to its new place (<see cref="FeelRules.TweenPosition"/>).</summary>
    public enum TweenStyle : byte
    {
        /// <summary>No tween: drawn at the goal at once.</summary>
        None,
        /// <summary>Pick up: ease-out with a lift in the middle (<see cref="FeelSettings.PickupLift"/>).</summary>
        Arc,
        /// <summary>Place or drop: ease-out that sinks a little past the spot and settles (<see cref="FeelSettings.PlaceSettle"/>).</summary>
        Settle,
        /// <summary>Into cookware: falls (ease-in), like gravity.</summary>
        Drop
    }
}
