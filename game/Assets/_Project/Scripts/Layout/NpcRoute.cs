using System.Collections.Generic;
using UnityEngine;

namespace Worktown.Game.Layout
{
    /// <summary>
    /// Spec 062a: where the guests walk for one layout (world metres, on the ground): they come in at <see cref="Entry"/> (facing
    /// <see cref="EntryYaw"/>), walk the <see cref="ArriveVia"/> corners and then to their queue slot (from <see cref="QueueHead"/> along
    /// <see cref="QueueDirection"/>, facing <see cref="QueueFacing"/>); paying guests stand from <see cref="PayHead"/> along
    /// <see cref="PayDirection"/>; leaving guests walk the <see cref="LeaveVia"/> corners and then to the entry. The corners are the grid
    /// path's turns between the two ends (none for a straight line, as in the default layout). <see cref="Walkable"/>: both paths were found
    /// (else the via lists are empty and the guests walk straight). Immutable; built by <see cref="NpcRoutes.Build"/>.
    /// </summary>
    public sealed class NpcRoute
    {
        public NpcRoute(Vector3 entry, float entryYaw, Vector3 queueHead, Vector3 queueDirection, Vector3 queueFacing, Vector3 payHead,
            Vector3 payDirection, IEnumerable<Vector3> arriveVia, IEnumerable<Vector3> leaveVia, bool walkable)
        {
            Entry = entry;
            EntryYaw = entryYaw;
            QueueHead = queueHead;
            QueueDirection = queueDirection;
            QueueFacing = queueFacing;
            PayHead = payHead;
            PayDirection = payDirection;
            ArriveVia = new List<Vector3>(arriveVia ?? new Vector3[0]).AsReadOnly();
            LeaveVia = new List<Vector3>(leaveVia ?? new Vector3[0]).AsReadOnly();
            Walkable = walkable;
        }

        public Vector3 Entry { get; }
        public float EntryYaw { get; }
        public Vector3 QueueHead { get; }
        public Vector3 QueueDirection { get; }
        public Vector3 QueueFacing { get; }
        public Vector3 PayHead { get; }
        public Vector3 PayDirection { get; }
        public IReadOnlyList<Vector3> ArriveVia { get; }
        public IReadOnlyList<Vector3> LeaveVia { get; }
        public bool Walkable { get; }
    }
}
