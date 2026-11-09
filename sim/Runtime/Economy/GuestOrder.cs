using System.Collections.Generic;
using Worktown.Sim.Cooking;

namespace Worktown.Sim.Economy
{
    /// <summary>
    /// Spec 032: what a guest brings besides the dish (the game draws the dish): its modifiers, its own patience base (s), whether it
    /// pays by card and, in cash, the note size it rounds up to. Immutable.
    /// </summary>
    public sealed record GuestOrder(IReadOnlyList<OrderModifier> Modifiers, int PatienceBaseSeconds, bool PaysByCard, int Note);
}
