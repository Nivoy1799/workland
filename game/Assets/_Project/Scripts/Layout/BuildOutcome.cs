namespace Worktown.Game.Layout
{
    /// <summary>Spec 062b: the server's answer to a build intent (BuildAnswer; BuildDesk.Pack puts a refused move's problem kind beside it).</summary>
    public enum BuildOutcome : byte
    {
        Ok = 0,
        /// <summary>The shop is not closed.</summary>
        NotClosed = 1,
        /// <summary>Build mode is not open here (the stage, or the hands hold something).</summary>
        NotBuilding = 2,
        /// <summary>Another player holds that piece.</summary>
        Claimed = 3,
        /// <summary>The place breaks a layout rule (the problem's kind comes with it).</summary>
        Invalid = 4,
        /// <summary>Too many requests too fast.</summary>
        Busy = 5,
        /// <summary>Nothing of this player's to undo.</summary>
        NothingToUndo = 6,
        /// <summary>The piece is fixed (the bin, the pallet).</summary>
        NotMovable = 7,
        /// <summary>Its station is on (a burner, the fryer, the spit): turn it off first.</summary>
        StationOn = 8,
        /// <summary>A required piece cannot be stored.</summary>
        Required = 9,
        /// <summary>The pallet is empty.</summary>
        NothingStored = 10,
        /// <summary>The undo does not fit any more (something is in the way now).</summary>
        Blocked = 11,
        /// <summary>The player holds no piece.</summary>
        NotHeld = 12,
        /// <summary>No such piece.</summary>
        Unknown = 13,
    }
}
