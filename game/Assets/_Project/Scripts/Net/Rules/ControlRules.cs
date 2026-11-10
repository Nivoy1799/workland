namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 041, pure: one input has one meaning everywhere (docs/feel.md 8). Left click does the obvious thing with what you aim at
    /// (<see cref="InteractionRules.ActionFor"/>); holding left does the hold actions; the right click only ever puts down what you
    /// hold (on the aimed free spot, else at your feet); the wheel turns (the food on the board with the knife, else the held item's
    /// rest rotation). Third person: left click and E act on the nearest target, the right click drops, the wheel zooms. Q is a
    /// hidden alias of the right click. The interactor routes every input through <see cref="IntentFor"/>; the server sees the same
    /// requests as before.
    /// </summary>
    public static class ControlRules
    {
        /// <summary>
        /// The meaning of <paramref name="input"/>. <paramref name="action"/> is what a left click does on the aimed target now (first
        /// person; ignored in third), <paramref name="holding"/> whether the hands hold something, <paramref name="putDownSpot"/> whether
        /// a free spot is aimed at for putting it down (first person).
        /// </summary>
        public static HandIntent IntentFor(HandInput input, bool firstPerson, InteractionAction action, bool holding, bool putDownSpot)
        {
            if (!firstPerson) return ThirdPerson(input, holding);
            switch (input)
            {
                case HandInput.LeftClick:
                    if (action == InteractionAction.None || action == InteractionAction.Cut) return HandIntent.None; // the knife drags
                    return IsHoldAction(action) ? HandIntent.Hold : HandIntent.Act; // a short press is a short squeeze
                case HandInput.HoldLeft:
                    if (action == InteractionAction.Cut) return HandIntent.Stroke;
                    return IsHoldAction(action) ? HandIntent.Hold : HandIntent.None; // a long press is no click
                case HandInput.RightClick:
                case HandInput.KeyQ:
                    return PutDown(holding, putDownSpot);
                case HandInput.Wheel:
                    if (action == InteractionAction.Cut) return HandIntent.TurnFood;
                    return holding ? HandIntent.TurnRest : HandIntent.None;
                default:
                    return HandIntent.None; // spec 020 AC7: E acts in third person only
            }
        }

        private static HandIntent ThirdPerson(HandInput input, bool holding)
        {
            switch (input)
            {
                case HandInput.LeftClick:
                case HandInput.KeyE:
                    return HandIntent.Act; // the nearest target, as E always did
                case HandInput.HoldLeft:
                    return HandIntent.Hold; // SqueezeHoldGate decides whether a hold is offered there
                case HandInput.RightClick:
                case HandInput.KeyQ:
                    return holding ? HandIntent.Drop : HandIntent.None; // third person aims at no spot
                case HandInput.Wheel:
                    return HandIntent.Zoom;
                default:
                    return HandIntent.None;
            }
        }

        private static HandIntent PutDown(bool holding, bool putDownSpot)
        {
            if (!holding) return HandIntent.None;
            return putDownSpot ? HandIntent.PutDown : HandIntent.Drop;
        }

        /// <summary>The actions done by holding the left button (a squeeze, a pour, a scrub, a wipe, a shave).</summary>
        public static bool IsHoldAction(InteractionAction action)
        {
            switch (action)
            {
                case InteractionAction.Squeeze:
                case InteractionAction.Fill:
                case InteractionAction.Scrub:
                case InteractionAction.Wipe:
                case InteractionAction.Shave:
                    return true;
                default:
                    return false;
            }
        }
    }
}
