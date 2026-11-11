using System.Collections.Generic;

namespace Worktown.Sim.Town
{
    /// <summary>
    /// Spec 064: a place in a district. <see cref="Anchor"/> is its front door (for a Border place, the point on the
    /// play-area edge); <see cref="NodeId"/> is the path node it is walked to (within 30 m). <see cref="FrontagePermille"/>
    /// scales the district's footfall for this place; <see cref="RentPermille"/> is its rent factor (1000 = the reference
    /// place). <see cref="Channels"/> are the channel ids of spec 052 the place can run.
    /// </summary>
    public sealed record PlaceDef(string Id, string Name, PlaceKind Kind, string DistrictId, TownPoint Anchor, int NodeId,
        int FromStage, int AreaM2, int FrontagePermille, int RentPermille, bool OutdoorSeats, bool EventDaysOnly,
        IReadOnlyList<string> Channels);
}
