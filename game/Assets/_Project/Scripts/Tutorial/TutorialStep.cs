using System;

namespace Worktown.Game.Tutorial
{
    /// <summary>
    /// Spec 038: one step of the tutorial: its instruction (short, it names the real action and the prompt the game shows), whether
    /// it may be skipped on its own, its done-predicate and its target selector, both pure over a <see cref="TutorialSnapshot"/>.
    /// </summary>
    public sealed class TutorialStep
    {
        public delegate bool Predicate(in TutorialSnapshot s);
        public delegate TutorialTarget Selector(in TutorialSnapshot s);
        /// <summary>038b: another instruction for what the player has now (a constant string), or null for <see cref="Text"/>.</summary>
        public delegate string Variant(in TutorialSnapshot s);

        public TutorialStep(TutorialStepId id, string text, bool optional, Predicate done, Selector target, Variant variant = null)
        {
            Id = id;
            Text = text ?? string.Empty;
            Optional = optional;
            _done = done ?? throw new ArgumentNullException(nameof(done));
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _variant = variant;
        }

        private readonly Predicate _done;
        private readonly Selector _target;
        private readonly Variant _variant;

        public TutorialStepId Id { get; }
        public string Text { get; }
        /// <summary>The card offers "Skip step" (the spit).</summary>
        public bool Optional { get; }

        public bool IsDone(in TutorialSnapshot s) => _done(in s);
        public TutorialTarget TargetFor(in TutorialSnapshot s) => _target(in s);

        /// <summary>038b: the instruction for <paramref name="s"/>: <see cref="Text"/>, or the step's variant for it (no allocation).</summary>
        public string TextFor(in TutorialSnapshot s) => (_variant != null ? _variant(in s) : null) ?? Text;
    }
}
