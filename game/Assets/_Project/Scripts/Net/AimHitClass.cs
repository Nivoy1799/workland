namespace Worktown.Game.Net
{
    /// <summary>
    /// 041 review: what one hit of the camera-centre ray is, in the order <see cref="AimHitSort.Classify"/> asks (a station's button,
    /// a place surface, a fixed vessel, a station or its slot, a free item, anything else).
    /// </summary>
    public enum AimHitClass : byte
    {
        /// <summary>A knob or button on a station's front (spec 019).</summary>
        Button,
        /// <summary>A place surface's top (spec 020; spec 039 strips, spec 042 cup spots).</summary>
        Surface,
        /// <summary>A fryer basket or a griddle spot (spec 029, 030): a vessel without a Pickup.</summary>
        FixedVessel,
        /// <summary>A station's body (its slot is chosen by the caller, <see cref="AimHitSort.SlotAt"/>).</summary>
        Station,
        /// <summary>An item (a Pickup), held or not.</summary>
        Item,
        /// <summary>A wall, furniture body or anything else.</summary>
        Other,
    }
}
