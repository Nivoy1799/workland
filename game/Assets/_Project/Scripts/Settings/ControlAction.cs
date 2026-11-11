namespace Worktown.Game.Settings
{
    /// <summary>
    /// Spec 048: the inputs a player can rebind (one keyboard key and one gamepad button each, ControlBindings). The mouse buttons
    /// and the wheel (left click does it, right click puts down, the wheel turns: spec 041), the gamepad's triggers (RT = left click,
    /// LT = right click), its sticks' directions and its d-pad (quick chat) stay fixed. Stored by name, so the order is free.
    /// </summary>
    public enum ControlAction
    {
        MoveForward,
        MoveBack,
        MoveLeft,
        MoveRight,
        Sprint,
        Crouch,
        Jump,
        /// <summary>First / third person (V).</summary>
        ToggleView,
        /// <summary>Third person: the same as the left click on the nearest thing (E).</summary>
        Use,
        /// <summary>A hidden alias of the right click: put down what you hold (Q; gamepad X).</summary>
        PutDown,
        /// <summary>One turn step, like a wheel notch (no key by default; gamepad Y).</summary>
        Turn,
        /// <summary>Hold for the quick chat list, then a digit (T).</summary>
        QuickChat,
        /// <summary>Hold for the emote wheel (G; gamepad LB).</summary>
        EmoteWheel,
        /// <summary>Ping where you look (the middle mouse button stays as well; gamepad RB).</summary>
        Ping,
        /// <summary>Who is in the shop (Tab).</summary>
        Players
    }
}
