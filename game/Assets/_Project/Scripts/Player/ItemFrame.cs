using UnityEngine;

namespace Worktown.Game.Player
{
    /// <summary>Spec 027b: where an item is drawn this frame (<see cref="ItemLook.Resolve"/>) and which tween starts now.</summary>
    public readonly struct ItemFrame
    {
        public ItemFrame(ItemGoalKind kind, int holder, Pose goal, TweenStyle begin, int landedBy = -1)
        {
            Kind = kind;
            Holder = holder;
            Goal = goal;
            Begin = begin;
            LandedBy = landedBy;
        }

        public ItemGoalKind Kind { get; }
        /// <summary>The holder drawn (a predicted one counts), or -1.</summary>
        public int Holder { get; }
        public Pose Goal { get; }
        /// <summary>The tween to start this frame (None: keep the running one, if any).</summary>
        public TweenStyle Begin { get; }
        /// <summary>Spec 044: this frame a put-down lands on its networked spot (the settle starts): who put it down; -1 otherwise. Other
        /// viewers hear the put-down's "tock" then (the one who put it down heard it at the click).</summary>
        public int LandedBy { get; }
    }
}
