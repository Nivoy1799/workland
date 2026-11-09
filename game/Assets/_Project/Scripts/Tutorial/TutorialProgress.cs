using System;
using System.Collections.Generic;

namespace Worktown.Game.Tutorial
{
    /// <summary>
    /// Spec 038, pure: where the player is in the steps. <see cref="Observe"/> remembers every step whose predicate holds (now or ever
    /// since the start), then moves past every remembered step, so a step the player did ahead of time completes at once when it comes.
    /// The last step is the done card (no predicate): <see cref="Finish"/> closes it. Skip ends the tutorial at any time; an optional step
    /// can be skipped on its own; <see cref="Start"/> replays from the first step.
    /// </summary>
    public sealed class TutorialProgress
    {
        private readonly IReadOnlyList<TutorialStep> _steps;
        private ulong _seen;

        public TutorialProgress(IReadOnlyList<TutorialStep> steps)
        {
            _steps = steps ?? throw new ArgumentNullException(nameof(steps));
            if (_steps.Count == 0 || _steps.Count > 64) throw new ArgumentException("1 to 64 steps", nameof(steps));
        }

        public IReadOnlyList<TutorialStep> Steps => _steps;
        public bool Running { get; private set; }
        /// <summary>The ended tutorial was skipped (not finished).</summary>
        public bool Skipped { get; private set; }
        public int Current { get; private set; }
        public int Count => _steps.Count;
        public TutorialStep CurrentStep => Running ? _steps[Current] : null;
        /// <summary>The done card is showing.</summary>
        public bool AtEnd => Running && Current == _steps.Count - 1;

        /// <summary>Starts (or replays) from the first step; nothing is remembered.</summary>
        public void Start()
        {
            Running = true;
            Skipped = false;
            Current = 0;
            _seen = 0;
        }

        /// <summary>True when the current step changed.</summary>
        public bool Observe(in TutorialSnapshot s)
        {
            if (!Running) return false;
            for (int i = Current; i < _steps.Count - 1; i++)
                if ((_seen & Bit(i)) == 0 && _steps[i].IsDone(in s)) _seen |= Bit(i);
            int before = Current;
            while (Current < _steps.Count - 1 && (_seen & Bit(Current)) != 0) Current++;
            return Current != before;
        }

        /// <summary>Whether step <paramref name="index"/> was seen done.</summary>
        public bool WasDone(int index) => index >= 0 && index < _steps.Count && (_seen & Bit(index)) != 0;

        /// <summary>An optional step is passed over (false for a required one).</summary>
        public bool SkipStep()
        {
            if (!Running || !_steps[Current].Optional || Current >= _steps.Count - 1) return false;
            _seen |= Bit(Current);
            Current++;
            return true;
        }

        /// <summary>"Skip tutorial": it ends now.</summary>
        public void SkipAll()
        {
            if (!Running) return;
            Running = false;
            Skipped = true;
        }

        /// <summary>The done card was closed (or timed out).</summary>
        public void Finish()
        {
            Running = false;
            Skipped = false;
        }

        private static ulong Bit(int i) => 1UL << i;
    }
}
