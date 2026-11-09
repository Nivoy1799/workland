using UnityEngine;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 035: marks a pickup as the wipe cloth (a free tool like the knife: it lies at its home spot and comes back there when lost).
    /// Held on a spill (the drink dispenser's puddle) it wipes it: the server times the hold (Shop.CleaningRules.WipeSeconds) and clears
    /// the spill. Marker only, no rules.
    /// </summary>
    public sealed class WipeCloth : MonoBehaviour
    {
    }
}
