namespace Worktown.Game.Player
{
    /// <summary>Spec 027b: one possible target for <see cref="FeelRules.PickTarget"/> (client selection only).</summary>
    public readonly struct AimCandidate
    {
        public AimCandidate(float angle, bool acceptsHeld, bool direct)
        {
            Angle = angle;
            AcceptsHeld = acceptsHeld;
            Direct = direct;
        }

        /// <summary>Degrees between the view ray and the direction to the candidate's anchor (its centre or spot).</summary>
        public float Angle { get; }
        /// <summary>Cookware that takes what the hands hold (a pot or pan for a potato, a plate for a cooked dish).</summary>
        public bool AcceptsHeld { get; }
        /// <summary>The exact ray hit landed directly on it (its body, not a wide collider edge): always wins.</summary>
        public bool Direct { get; }
    }
}
