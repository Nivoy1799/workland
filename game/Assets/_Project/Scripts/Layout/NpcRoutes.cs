using System.Collections.Generic;
using UnityEngine;
using Worktown.Sim.Layout;

namespace Worktown.Game.Layout
{
    /// <summary>
    /// Spec 062a, pure: the guests' <see cref="NpcRoute"/> for a layout. The queue head stands <see cref="QueueGapMm"/> in front of the
    /// first serving counter's guest side (by piece id), the pay head as far in front of the first register's; the queue runs along the
    /// counter towards its own +x turned with it (today: west, away from the street), the payers' line the other way along the register,
    /// and waiting guests face the counter (its cook side). These are today's builder values (GrayboxSceneBuilder's QueueGap, the queue's
    /// Vector3.left, the payers' Vector3.right, the facing Vector3.forward for the default's half-turned front counter). The entry is the
    /// room's. The ways in (entry to queue head) and out (pay head to entry) are <see cref="GridPath"/>s on the guests' grid (the room's
    /// <see cref="WalkGrid"/> within its guest area), reduced to their corners; the ends are the exact anchors, so a straight path has no
    /// corners in between. Null when the layout has no serving counter.
    /// </summary>
    public static class NpcRoutes
    {
        /// <summary>GrayboxSceneBuilder's QueueGap: the queue line is this far in front of the counter's street side.</summary>
        public const int QueueGapMm = 400;

        public static NpcRoute Build(ShopLayout layout, RoomDef room, PieceCatalog catalog)
        {
            if (layout == null || room == null || catalog == null) return null;
            LayoutPiece counter = FirstOf(layout, PieceCatalog.ServingCounter);
            if (counter == null || !catalog.TryGet(counter.TypeId, out PieceDef counterDef)) return null;
            LayoutPiece register = FirstOf(layout, PieceCatalog.RegisterModule);
            PieceDef registerDef = null;
            if (register != null && !catalog.TryGet(register.TypeId, out registerDef)) register = null;

            PointMm head = InFront(counter, counterDef);
            PointMm pay = register != null ? InFront(register, registerDef) : head;
            Vector3 queueDirection = Along(counter.Turns);
            Vector3 payDirection = register != null ? -Along(register.Turns) : -queueDirection;
            PointMm cook = LayoutRules.CookDirection(counter.Turns);
            var queueFacing = new Vector3(cook.X, 0f, cook.Z);

            WalkGrid guests = WalkGrid.ForLayout(layout, room, catalog).Within(room.GuestArea);
            GridPath inWay = GridPath.Find(guests, room.GuestEntry, head);
            GridPath outWay = GridPath.Find(guests, pay, room.GuestEntry);
            List<Vector3> arrive = Between(inWay), leave = Between(outWay);

            Vector3 entry = World(room.GuestEntry);
            Vector3 firstTarget = arrive.Count > 0 ? arrive[0] : World(head);
            Vector3 look = firstTarget - entry;
            float yaw = look.sqrMagnitude > 0.000001f ? Mathf.Repeat(Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg, 360f) : 0f;
            // Spec 062b (062a Part B review): a guest leaving from elsewhere (its queue slot) gets its own path from where it stands.
            PointMm exit = room.GuestEntry;
            System.Func<Vector3, IReadOnlyList<Vector3>> leaveFrom = start =>
            {
                GridPath path = GridPath.Find(guests, new PointMm(Mathf.RoundToInt(start.x * 1000f), Mathf.RoundToInt(start.z * 1000f)), exit);
                return path.Found ? Between(path) : leave;
            };
            return new NpcRoute(entry, yaw, World(head), queueDirection, queueFacing, World(pay), payDirection, arrive, leave,
                inWay.Found && outWay.Found, leaveFrom);
        }

        /// <summary>The point <see cref="QueueGapMm"/> in front of a piece's guest side, in the middle of its width.</summary>
        public static PointMm InFront(LayoutPiece piece, PieceDef def)
        {
            PointMm guest = LayoutRules.CookDirection(piece.Turns + 2);
            int reach = def.DepthMm / 2 + QueueGapMm;
            return new PointMm(piece.XMm + guest.X * reach, piece.ZMm + guest.Z * reach);
        }

        /// <summary>A piece's own +x after <paramref name="turns"/> quarter turns (Unity's yaw): 0 +x, 1 -z, 2 -x, 3 +z.</summary>
        public static Vector3 Along(int turns)
        {
            switch (((turns % 4) + 4) % 4)
            {
                case 1: return Vector3.back;
                case 2: return Vector3.left;
                case 3: return Vector3.forward;
                default: return Vector3.right;
            }
        }

        public static Vector3 World(PointMm p) => new Vector3(p.X / 1000f, 0f, p.Z / 1000f);

        private static LayoutPiece FirstOf(ShopLayout layout, string type)
        {
            foreach (LayoutPiece p in layout.Pieces) // ascending id
                if (p.TypeId == type) return p;
            return null;
        }

        private static List<Vector3> Between(GridPath path)
        {
            var points = new List<Vector3>();
            for (int i = 1; i + 1 < path.Corners.Count; i++) points.Add(World(path.Corners[i]));
            return points;
        }
    }
}
