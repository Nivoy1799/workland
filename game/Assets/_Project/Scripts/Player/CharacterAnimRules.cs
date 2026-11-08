using System;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 024: pure rules for the character animation, from data every client already has (the horizontal speed of
    /// the smoothed body and the crouch flag). Visual only: nothing here feeds movement or the server.
    /// </summary>
    public static class CharacterAnimRules
    {
        /// <summary>m/s: from idle the character starts walking at this speed...</summary>
        public const float WalkStartSpeed = 0.5f;
        /// <summary>...and a walking one stops below this one (hysteresis, no flicker near the threshold).</summary>
        public const float WalkStopSpeed = 0.25f;
        /// <summary>m/s: running starts here (spec 025: between walk 2.5 and sprint 5) and stops below <see cref="RunStopSpeed"/>.</summary>
        public const float RunStartSpeed = 3.75f;
        public const float RunStopSpeed = 3.25f;

        /// <summary>
        /// Spec 025: limits of the playback rate. Inside them the feet stay planted; the clip speeds at rate 1 are measured
        /// from the clips (<see cref="StrideRules"/>) by the art builder and passed in.
        /// </summary>
        public const float MinRate = 0.6f;
        public const float MaxRate = 1.6f;

        /// <summary>Seconds a clip takes to fade in or out.</summary>
        public const float FadeSeconds = 0.15f;
        /// <summary>1/s: how fast the measured speed follows the real one (smooths network jitter).</summary>
        public const float SpeedSmoothing = 12f;

        /// <summary>The state for a speed and crouch flag, starting from idle.</summary>
        public static CharacterAnimState StateFor(float speed, bool crouched) =>
            StateFor(speed, crouched, CharacterAnimState.Idle);

        /// <summary>
        /// The state for a speed (m/s, horizontal) and the crouch flag, given the state shown last frame. Crouch wins over
        /// every speed (one crouch pose). NaN, infinite or negative speeds count as standing still.
        /// </summary>
        public static CharacterAnimState StateFor(float speed, bool crouched, CharacterAnimState previous)
        {
            if (crouched) return CharacterAnimState.Crouch;
            if (float.IsNaN(speed) || float.IsInfinity(speed) || speed < 0f) speed = 0f;
            bool wasMoving = previous == CharacterAnimState.Walk || previous == CharacterAnimState.Run;
            if (speed < (wasMoving ? WalkStopSpeed : WalkStartSpeed)) return CharacterAnimState.Idle;
            float runAt = previous == CharacterAnimState.Run ? RunStopSpeed : RunStartSpeed;
            return speed >= runAt ? CharacterAnimState.Run : CharacterAnimState.Walk;
        }

        /// <summary>
        /// Spec 025: playback rate of the state's clip, so the feet keep up with the ground: speed / clip speed (the m/s the
        /// clip covers at rate 1), clamped to <see cref="MinRate"/>..<see cref="MaxRate"/>. 1 for idle and crouch, and for
        /// an unknown clip speed (0, negative, NaN). A bad speed counts as standing still.
        /// </summary>
        public static float PlaybackRate(CharacterAnimState state, float speed, float clipSpeed)
        {
            if (state != CharacterAnimState.Walk && state != CharacterAnimState.Run) return 1f;
            if (float.IsNaN(clipSpeed) || float.IsInfinity(clipSpeed) || clipSpeed <= 0f) return 1f;
            if (float.IsNaN(speed) || float.IsInfinity(speed) || speed < 0f) speed = 0f;
            return Clamp(speed / clipSpeed, MinRate, MaxRate);
        }

        /// <summary>Spec 025: true when <paramref name="speed"/> plays the clip without hitting the rate limits (feet planted).</summary>
        public static bool FitsWithoutClamp(float speed, float clipSpeed)
        {
            if (float.IsNaN(clipSpeed) || float.IsInfinity(clipSpeed) || clipSpeed <= 0f) return false;
            float rate = speed / clipSpeed;
            return rate > MinRate && rate < MaxRate;
        }

        /// <summary>Exponential smoothing of the measured speed; a bad sample (NaN, infinite, negative) is ignored.</summary>
        public static float SmoothSpeed(float current, float measured, float deltaTime)
        {
            if (float.IsNaN(measured) || float.IsInfinity(measured) || measured < 0f) return current;
            if (deltaTime <= 0f || float.IsNaN(deltaTime)) return current;
            float t = 1f - (float)Math.Exp(-SpeedSmoothing * deltaTime);
            return current + (measured - current) * t;
        }

        /// <summary>One fade step of a clip weight towards 1 (shown) or 0, over <see cref="FadeSeconds"/>.</summary>
        public static float FadeWeight(float weight, bool shown, float deltaTime)
        {
            float target = shown ? 1f : 0f;
            float step = deltaTime > 0f ? deltaTime / FadeSeconds : 0f;
            if (weight < target) return Math.Min(target, weight + step);
            return Math.Max(target, weight - step);
        }

        /// <summary>Clip time for a phase that grows with time x rate (the clips loop).</summary>
        public static float LoopTime(double phaseSeconds, float clipLength)
        {
            if (clipLength <= 0f || double.IsNaN(phaseSeconds) || double.IsInfinity(phaseSeconds)) return 0f;
            double t = phaseSeconds % clipLength;
            if (t < 0) t += clipLength;
            return (float)t;
        }

        private static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
    }
}
