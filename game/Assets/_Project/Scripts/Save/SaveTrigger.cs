namespace Worktown.Game.Save
{
    /// <summary>Spec 053: the moments the host's game is saved (<see cref="AutosaveRules.ShouldSave"/>).</summary>
    public enum SaveTrigger : byte
    {
        /// <summary>New shop: its file is written at once, so Continue exists even before the first service ends.</summary>
        Created,
        /// <summary>A service (shift) closed, after its summary numbers, the pitch fee and the goal check.</summary>
        ServiceEnd,
        /// <summary>The rent goal was reached (at a service end).</summary>
        GoalReached,
        /// <summary>The host closes the shop (spec 047 "Close the shop"), before the guests' notice.</summary>
        HostClosing,
        /// <summary>The game quits (best effort).</summary>
        AppQuit,
        /// <summary>Every <see cref="AutosaveRules.PeriodicSeconds"/> while a service is open.</summary>
        Periodic,
        /// <summary>Spec 057: the stage moved (the crew rented the restaurant and moved in), at once between services or at a service end.</summary>
        StageChanged
    }
}
