namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 025 amendment: how the walk and jog clips play together at one speed (<see cref="CharacterAnimRules.Locomotion"/>).
    /// </summary>
    public readonly struct LocomotionBlend
    {
        public LocomotionBlend(float weight, float rate, float loopLength)
        {
            Weight = weight;
            Rate = rate;
            LoopLength = loopLength;
        }

        /// <summary>0 = only the walk clip, 1 = only the jog clip.</summary>
        public float Weight { get; }

        /// <summary>Playback rate of the blended loop.</summary>
        public float Rate { get; }

        /// <summary>Seconds of the blended loop at rate 1 (both clips run on the same normalized phase).</summary>
        public float LoopLength { get; }

        /// <summary>Steps per second shown (two steps per loop).</summary>
        public float Cadence => LoopLength > 0f ? CharacterAnimRules.StepsPerLoop * Rate / LoopLength : 0f;
    }
}
