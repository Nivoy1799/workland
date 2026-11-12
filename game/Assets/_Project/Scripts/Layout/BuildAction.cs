namespace Worktown.Game.Layout
{
    /// <summary>Spec 062b: what a build mode input does (BuildControlRules.ActionFor).</summary>
    public enum BuildAction : byte
    {
        None = 0,
        /// <summary>Pick the aimed piece up (a claim).</summary>
        PickUp = 1,
        /// <summary>Take the next stored piece from the pallet.</summary>
        TakeOut = 2,
        /// <summary>Place the held piece where the ghost shows.</summary>
        Place = 3,
        /// <summary>Put the held piece on the pallet.</summary>
        Store = 4,
        /// <summary>Turn the held piece a quarter turn.</summary>
        Turn = 5,
        /// <summary>Pick which stored piece comes out next.</summary>
        NextStored = 6,
        Undo = 7,
        Leave = 8,
    }
}
