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
        public CookCapture(Func<string, IEnumerator> shot, Func<string, float, IEnumerator> frames, Action<float> tweenScale)
        {
            Shot = shot;
            Frames = frames;
            TweenScale = tweenScale;
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
