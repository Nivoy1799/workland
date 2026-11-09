using System;
using System.Collections;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 027 / 027b: what the cook tour records while <see cref="CookSequence"/> runs: a shot after each step, and (027b)
    /// a frame sequence plus a shot of the aimed state right before some clicks (the pan's arc into the hand, the ghost on
    /// the burner, the highlight and prompt on the pot, the potato dropping in), optionally in slow motion. The
    /// -demo-cookware run passes none.
    /// </summary>
    public sealed class CookCapture
    {
        public CookCapture(Func<string, IEnumerator> shot, Func<string, float, IEnumerator> frames, Action<float> tweenScale,
            Func<bool> framesBusy = null)
        {
            Shot = shot;
            Frames = frames;
            TweenScale = tweenScale;
            FramesBusy = framesBusy;
        }

        /// <summary>Spec 031b: true while a frame sequence is still being captured (null: never waits).</summary>
        public Func<bool> FramesBusy { get; }

        /// <summary>Spec 031b: waits until every running frame sequence has ended, so the next frames are a separate capture.</summary>
        public IEnumerator WaitForFrames(float limitSeconds = 20f)
        {
            float until = UnityEngine.Time.unscaledTime + limitSeconds;
            while (FramesBusy != null && FramesBusy() && UnityEngine.Time.unscaledTime < until) yield return null;
        }

        /// <summary>Takes one named shot (waits for the end of the frame).</summary>
        public Func<string, IEnumerator> Shot { get; }
        /// <summary>Starts a named frame sequence of that many seconds; returns at once.</summary>
        public Func<string, float, IEnumerator> Frames { get; }
        /// <summary>Makes every tween this many times longer (1 = normal).</summary>
        public Action<float> TweenScale { get; }

        /// <summary>A shot of the aimed state, then the frames: for <see cref="TourActor.Use"/>'s beforeClick.</summary>
        public Func<IEnumerator> Before(string shot, string frames, float seconds)
        {
            return () => Run(shot, frames, seconds);
        }

        private IEnumerator Run(string shot, string frames, float seconds)
        {
            if (shot != null && Shot != null) yield return Shot(shot);
            if (frames != null && Frames != null) yield return Frames(frames, seconds);
        }
    }
}
