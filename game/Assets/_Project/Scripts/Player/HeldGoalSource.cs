using UnityEngine;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 027b: where an item held by <paramref name="holder"/> is drawn (its hand, your first-person view); false when
    /// the holder's character is not here, then <see cref="ItemLook"/> uses the hold point (the networked root).
    /// </summary>
    public delegate bool HeldGoalSource(int holder, out ItemGoalKind kind, out Pose pose);
}
