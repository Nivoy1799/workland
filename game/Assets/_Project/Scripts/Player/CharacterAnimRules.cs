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
        /// every speed: standing still it is <see cref="CharacterAnimState.Crouch"/>, moving (same start / stop speeds as
        /// walking) <see cref="CharacterAnimState.CrouchWalk"/>. NaN, infinite or negative speeds count as standing still.
        /// </summary>
        public static CharacterAnimState StateFor(float speed, bool crouched, CharacterAnimState previous)
        {
            if (float.IsNaN(speed) || float.IsInfinity(speed) || speed < 0f) speed = 0f;
            bool wasMoving = previous == CharacterAnimState.Walk || previous == CharacterAnimState.Run
                             || previous == CharacterAnimState.CrouchWalk;
            bool moving = speed >= (wasMoving ? WalkStopSpeed : WalkStartSpeed);
            if (crouched) return moving ? CharacterAnimState.CrouchWalk : CharacterAnimState.Crouch;
            if (!moving) return CharacterAnimState.Idle;
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

        /// <summary>Spec 025 amendment: every locomotion loop holds two steps (left, right).</summary>
        public const float StepsPerLoop = 2f;
        /// <summary>
        /// Spec 025 amendment: target cadence (steps per second) at three speeds, linear in between (a calm walk, the
        /// player walk, the sprint). Below the first point the first slope continues (1.37 steps/s at 0 m/s); above the
        /// last one the cadence stays.
        /// </summary>
        private static readonly float[] CadenceSpeeds = { 1.0f, 2.5f, 5.0f };
        private static readonly float[] CadenceSteps = { 1.9f, 2.7f, 3.0f };
        /// <summary>
        /// Spec 025 amendment: playback rate limits of the walk / jog blend. Below 0.3 a step looks frozen, above 2.0 the legs
        /// look frantic; inside them the feet stay planted by construction. They only bite below about 0.3 m/s (just before
        /// the walk stops) or if the clips change.
        /// </summary>
        public const float LocomotionMinRate = 0.3f;
        public const float LocomotionMaxRate = 2.0f;
        /// <summary>
        /// Spec 025 amendment: playback rate limits of the crouch walk. 2.5 covers sneaking at 1.25 m/s with the library's
        /// crouch walk (about 0.55 m/s at rate 1); slower than 0.5 looks frozen, so feet slide there.
        /// </summary>
        public const float CrouchMinRate = 0.5f;
        public const float CrouchMaxRate = 2.5f;

        /// <summary>Spec 025 amendment: target steps per second at a speed (bad speeds count as 0).</summary>
        public static float TargetCadence(float speed)
        {
            if (float.IsNaN(speed) || float.IsInfinity(speed) || speed < 0f) speed = 0f;
            if (speed >= CadenceSpeeds[CadenceSpeeds.Length - 1]) return CadenceSteps[CadenceSteps.Length - 1];
            int i = 0;
            while (i < CadenceSpeeds.Length - 2 && speed > CadenceSpeeds[i + 1]) i++;
            float t = (speed - CadenceSpeeds[i]) / (CadenceSpeeds[i + 1] - CadenceSpeeds[i]);
            return CadenceSteps[i] + (CadenceSteps[i + 1] - CadenceSteps[i]) * t;
        }

        /// <summary>
        /// Spec 025 amendment: the walk / jog blend for a speed. The two clips play on one normalized phase; the blended
        /// stride (distance per loop) and loop length are lerps of the clips' by the weight. The weight is the one whose
        /// stride gives the target cadence (stride = speed / cadence x 2 steps), clamped to 0..1; the rate then makes the
        /// blended loop cover the speed (rate = speed x loop length / stride), so the feet stay planted, clamped to
        /// <see cref="LocomotionMinRate"/>..<see cref="LocomotionMaxRate"/>. Bad speeds count as 0. Without a valid walk
        /// clip: weight 0, rate 1. Without a valid jog clip (or one that does not step further): the walk alone.
        /// </summary>
        public static LocomotionBlend Locomotion(float speed, GaitClip walk, GaitClip jog)
        {
            if (float.IsNaN(speed) || float.IsInfinity(speed) || speed < 0f) speed = 0f;
            if (!walk.IsValid) return new LocomotionBlend(0f, 1f, walk.Length > 0f && !float.IsInfinity(walk.Length) ? walk.Length : 1f);
            float weight = 0f;
            if (jog.IsValid && jog.Stride > walk.Stride)
            {
                float stride = speed / TargetCadence(speed) * StepsPerLoop;
                weight = Clamp((stride - walk.Stride) / (jog.Stride - walk.Stride), 0f, 1f);
            }
            float length = walk.Length + ((jog.IsValid ? jog.Length : walk.Length) - walk.Length) * weight;
            float blendedStride = walk.Stride + ((jog.IsValid ? jog.Stride : walk.Stride) - walk.Stride) * weight;
            float rate = Clamp(speed * length / blendedStride, LocomotionMinRate, LocomotionMaxRate);
            return new LocomotionBlend(weight, rate, length);
        }

        /// <summary>Spec 025 amendment: the m/s the feet of a blend cover over the ground (rate x blended stride / loop length).</summary>
        public static float GroundSpeed(LocomotionBlend blend, GaitClip walk, GaitClip jog)
        {
            if (!walk.IsValid || blend.LoopLength <= 0f) return 0f;
            float jogStride = jog.IsValid ? jog.Stride : walk.Stride;
            float stride = walk.Stride + (jogStride - walk.Stride) * blend.Weight;
            return blend.Rate * stride / blend.LoopLength;
        }

        /// <summary>Spec 025 amendment: playback rate of the crouch walk (1 for an unknown clip speed).</summary>
        public static float CrouchRate(float speed, float clipSpeed)
        {
            if (float.IsNaN(clipSpeed) || float.IsInfinity(clipSpeed) || clipSpeed <= 0f) return 1f;
            if (float.IsNaN(speed) || float.IsInfinity(speed) || speed < 0f) speed = 0f;
            return Clamp(speed / clipSpeed, CrouchMinRate, CrouchMaxRate);
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
