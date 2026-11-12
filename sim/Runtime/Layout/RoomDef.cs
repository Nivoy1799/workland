using System;
using System.Collections.Generic;
using System.Linq;

namespace Worktown.Sim.Layout
{
    /// <summary>
    /// Spec 062a: the fixed shell a layout stands in (world millimetres). <see cref="Site"/>: the walk grid's extent (the room, the yard
    /// and the street in front); <see cref="Floor"/>: the room's floor, where every piece but a yard piece stands; <see cref="CookArea"/>
    /// and <see cref="GuestArea"/>: where cooks and guests stand (guests walk only in the guest area); <see cref="Walls"/>: the wall
    /// bodies (they block walking like a floor piece); <see cref="KeepOut"/>: rects no piece may cover (the staff door on both sides);
    /// <see cref="StaffSpawn"/>: where the cooks start; <see cref="GuestEntry"/>: where guests come from and leave to. Immutable.
    /// <see cref="Walls"/> and <see cref="Site"/> are not in the spec's sketch of the record: the walk grid needs them (Part B note).
    /// </summary>
    public sealed class RoomDef
    {
        public RoomDef(RectMm site, RectMm floor, RectMm cookArea, RectMm guestArea, IEnumerable<RectMm>? walls, IEnumerable<RectMm>? keepOut,
            PointMm staffSpawn, PointMm guestEntry)
        {
            if (site.IsEmpty || floor.IsEmpty) throw new ArgumentException("the site and the floor must have an area");
            if (!site.Contains(floor)) throw new ArgumentException("the floor must lie inside the site");
            Site = site;
            Floor = floor;
            CookArea = cookArea;
            GuestArea = guestArea;
            Walls = (walls ?? Array.Empty<RectMm>()).ToList().AsReadOnly();
            KeepOut = (keepOut ?? Array.Empty<RectMm>()).ToList().AsReadOnly();
            StaffSpawn = staffSpawn;
            GuestEntry = guestEntry;
        }

        public RectMm Site { get; }
        public RectMm Floor { get; }
        public RectMm CookArea { get; }
        public RectMm GuestArea { get; }
        public IReadOnlyList<RectMm> Walls { get; }
        public IReadOnlyList<RectMm> KeepOut { get; }
        public PointMm StaffSpawn { get; }
        public PointMm GuestEntry { get; }

        /// <summary>
        /// Spec 062a: today's shell (GrayboxSceneBuilder's room; NpcRoutesTests compares it with the built scene's walls and anchors). The
        /// floor runs from the left wall's inner face (x 0.46) to the right wall's (14.80) and from the front counter's street side (z 11.50)
        /// to the back wall's face (14.92). The walls are 0.2 m thick; the left one leaves the staff door between z 12.20 and 13.70 (its
        /// keep-out reaches 0.9 m into the room and into the yard). The guests' area is the street side in front of the counter (z 9.0 to
        /// 11.5: the sidewalk and the near half of the road; there is no traffic) from x 0 to the street entry at x 24. The cooks start at
        /// spawn point 1 (1.2, 12.95); the site reaches 3 m west of the room for the yard's bin.
        /// </summary>
        public static RoomDef CreateDefault() => new RoomDef(
            site: new RectMm(-3000, 9000, 25000, 15200),
            floor: new RectMm(460, 11500, 14800, 14920),
            cookArea: new RectMm(460, 11500, 14800, 14920),
            guestArea: new RectMm(0, 9000, 25000, 11500),
            walls: new[]
            {
                new RectMm(260, 14920, 15000, 15120),  // the back wall
                new RectMm(260, 13700, 460, 14920),    // the left side wall behind the staff door
                new RectMm(260, 11500, 460, 12200),    // the left side wall in front of it
                new RectMm(14800, 11500, 15000, 14920), // the right side wall
            },
            keepOut: new[] { new RectMm(-640, 12200, 1360, 13700) }, // the staff door, 0.9 m either side of the wall
            staffSpawn: new PointMm(1200, 12950),
            guestEntry: new PointMm(24000, 11100));
    }
}
