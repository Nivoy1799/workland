namespace Worktown.Game.Net.Rules
{
    /// <summary>Spec 020: what a first-person click does, from <see cref="InteractionRules.ActionFor"/>.</summary>
    public enum InteractionAction : byte
    {
        None = 0,
        /// <summary>Press a stove knob or button (switch, fry/saute).</summary>
        Press,
        /// <summary>Knife over a board with food: drag down to cut, right click to turn (no click action).</summary>
        Cut,
        /// <summary>Spec 021: put the held food onto the board or plate, into the pan, pot or bowl.</summary>
        PutInto,
        /// <summary>Spec 021: holding a pan, pot or bowl with food, pour it onto a plate or into a bowl.</summary>
        Pour,
        Sell,
        /// <summary>Put what you hold on a free spot of a place surface (a worktop, the stove top, a burner).</summary>
        Place,
        PickUp,
        /// <summary>Take an ingredient from a crate (it costs money).</summary>
        Buy,
        /// <summary>Take the food from a board, pan or bowl; take a plate from the shelf.</summary>
        Take
    }
}
