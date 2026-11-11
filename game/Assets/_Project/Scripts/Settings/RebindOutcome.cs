namespace Worktown.Game.Settings
{
    /// <summary>Spec 048: what a rebind did (ControlBindings.Rebind).</summary>
    public enum RebindOutcome
    {
        /// <summary>The action now uses the new key or button.</summary>
        Bound,
        /// <summary>The key or button was another action's: the two actions swapped (<see cref="RebindResult.Other"/>).</summary>
        Swapped,
        /// <summary>The action has no key or button now.</summary>
        Cleared,
        /// <summary>It already used it: nothing changed.</summary>
        Unchanged,
        /// <summary>A fixed key (Esc, Enter, F1-F12, Alt, the arrows, Backspace) or button: refused, nothing changed.</summary>
        Reserved
    }
}
