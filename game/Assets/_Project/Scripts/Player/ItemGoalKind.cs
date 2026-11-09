namespace Worktown.Game.Player
{
    /// <summary>Spec 027b: where an item's model is drawn (<see cref="FeelRules.StyleFor"/> picks the tween between two).</summary>
    public enum ItemGoalKind : byte
    {
        /// <summary>Lying somewhere (the networked transform, or a predicted spot).</summary>
        Rest,
        /// <summary>In a character's right hand (third person, and everyone else's view).</summary>
        Hand,
        /// <summary>In your own first-person view (the view anchor).</summary>
        View,
        /// <summary>Held, but the holder's character is not here yet: the server hold point.</summary>
        HoldPoint,
        /// <summary>Predicted: just above the contents of a pot, pan, board or plate it is put into.</summary>
        Into
    }
}
