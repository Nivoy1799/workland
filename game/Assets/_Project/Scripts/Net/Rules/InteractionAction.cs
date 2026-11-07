namespace Worktown.Game.Net.Rules
{
    /// <summary>Spec 020: what a first-person click does, from <see cref="InteractionRules.ActionFor"/>.</summary>
    public enum InteractionAction : byte
    {
        None = 0,
        /// <summary>Press a station button (switch, fry/saute).</summary>
        Press,
        /// <summary>Knife over a board with food: drag down to cut, right click to turn (no click action).</summary>
        Cut,
        PutOnBoard,
        /// <summary>Into the pan or pot, onto the plate, into the bowl.</summary>
        PutInto,
        Sell,
        /// <summary>Put what you hold on a free spot of a place surface.</summary>
        Place,
        PickUp,
        /// <summary>Take an ingredient from a crate (it costs money).</summary>
        Buy,
        /// <summary>Take the food from a pan, pot, board, plate or bowl.</summary>
        Take
    }
}
