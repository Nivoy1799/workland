using UnityEngine;

namespace Worktown.Game.Player
{
    /// <summary>Spec 027b: where an item is drawn this frame (<see cref="ItemLook.Resolve"/>) and which tween starts now.</summary>
    public readonly struct ItemFrame
    {
        public ItemFrame(ItemGoalKind kind, int holder, Pose goal, TweenStyle begin)
        {
            Kind = kind;
            Holder = holder;
            Goal = goal;
            Begin = begin;
        }

        public ItemGoalKind Kind { get; }
        /// <summary>The holder drawn (a predicted one counts), or -1.</summary>
        public int Holder { get; }
        public Pose Goal { get; }
        /// <summary>The tween to start this frame (None: keep the running one, if any).</summary>
        public TweenStyle Begin { get; }
    }
}
