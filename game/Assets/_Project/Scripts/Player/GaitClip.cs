namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 025 amendment: one looping locomotion clip as the animation rules see it: the m/s it covers at playback rate 1
    /// on the body (measured by the art builder) and its loop length in seconds. Every loop holds two steps.
    /// </summary>
    public readonly struct GaitClip
    {
        public GaitClip(float speed, float length)
        {
            Speed = speed;
            Length = length;
        }

        /// <summary>m/s at playback rate 1.</summary>
        public float Speed { get; }

        /// <summary>Seconds per loop at playback rate 1.</summary>
        public float Length { get; }

        /// <summary>Metres covered per loop (two steps).</summary>
        public float Stride => Speed * Length;

        /// <summary>Finite and positive speed and length.</summary>
        public bool IsValid => Speed > 0f && Length > 0f && !float.IsInfinity(Speed) && !float.IsInfinity(Length);
    }
}
