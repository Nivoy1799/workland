using System;
using System.Collections.Generic;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 027b (review), pure: the owner's predicted looks that are still waiting for the server, and the matching of
    /// the server's answers to requests. Every request that gets an answer is numbered when it is sent; the server answers
    /// in order (one reliable channel), so the n-th answer belongs to the n-th request. An answer only reaches a prediction
    /// when it is that prediction's request and of the matching kind, so an older refusal never denies a newer click.
    /// The server drops requests it rate-limits without an answer; after <see cref="LedgerResetSeconds"/> without a request
    /// every older one counts as answered, so the numbering catches up. A newer prediction for the same item replaces the
    /// older one silently; predictions for other items keep going until they resolve on their own, so a quick second click
    /// never makes the first item bounce.
    /// </summary>
    public sealed class PredictionBook<T> where T : class
    {
        /// <summary>s: no request for this long and nothing older is still waiting for its answer.</summary>
        public const float LedgerResetSeconds = 1f;

        /// <summary>A prediction that was confirmed or denied (<see cref="Update"/>).</summary>
        public readonly struct Resolved
        {
            public Resolved(T value, FeelPrediction prediction)
            {
                Value = value;
                Prediction = prediction;
            }

            public T Value { get; }
            public FeelPrediction Prediction { get; }
        }

        private sealed class Entry
        {
            public T Value;
            public object Key;
            public FeelPrediction Prediction;
            public int Request;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private int _sent;
        private int _answered;
        private float _lastSend = float.NegativeInfinity;

        public int Count => _entries.Count;

        /// <summary>The expected answer for a prediction's request.</summary>
        public static AnswerKind AnswerFor(PredictionKind kind)
        {
            switch (kind)
            {
                case PredictionKind.PickUp: return AnswerKind.Pickup;
                case PredictionKind.Drop: return AnswerKind.Drop;
                case PredictionKind.Place: return AnswerKind.Place;
                default: return AnswerKind.Cook;
            }
        }

        /// <summary>
        /// A request that the server answers went out at <paramref name="now"/>; returns its number. After a quiet spell of
        /// <see cref="LedgerResetSeconds"/> (or three round trips, when <paramref name="roundTrip"/> is longer) every older
        /// request counts as answered, so one that was never answered cannot shift the numbering for good.
        /// </summary>
        public int Sent(float now, float roundTrip = 0f)
        {
            float quiet = Math.Max(LedgerResetSeconds, 3f * (float.IsNaN(roundTrip) ? 0f : Math.Max(0f, roundTrip)));
            if (now - _lastSend > quiet) _answered = _sent;
            _lastSend = now;
            return ++_sent;
        }

        /// <summary>
        /// Tracks <paramref name="prediction"/> for request <paramref name="request"/> (from <see cref="Sent"/>). An older
        /// prediction with the same <paramref name="key"/> (the same item) is dropped without a verdict.
        /// </summary>
        public void Add(T value, object key, FeelPrediction prediction, int request)
        {
            if (prediction == null) throw new ArgumentNullException(nameof(prediction));
            for (int i = _entries.Count - 1; i >= 0; i--)
                if (Equals(_entries[i].Key, key)) _entries.RemoveAt(i);
            _entries.Add(new Entry { Value = value, Key = key, Prediction = prediction, Request = request });
        }

        /// <summary>
        /// The next answer arrived: it reaches only the prediction of that request, if the kind matches. A refusal is applied
        /// only when that request was the only one in flight; with more in flight (an older one may have gone unanswered)
        /// it could belong to another request, so it is ignored and the prediction waits for the synced state or its
        /// timeout. An "ok" only restarts a timeout, so it is always applied. An answer with nothing in flight (late, after
        /// a quiet-spell reset) is ignored.
        /// </summary>
        public void Answer(AnswerKind kind, bool ok, float now)
        {
            if (_answered >= _sent) return;
            bool alone = _sent - _answered == 1;
            _answered++;
            foreach (Entry e in _entries)
            {
                if (e.Request != _answered || AnswerFor(e.Prediction.Kind) != kind) continue;
                if (ok || alone) e.Prediction.NoteAnswer(ok, now);
            }
        }

        /// <summary>Reads the synced state of every waiting prediction; adds those that resolved to <paramref name="resolved"/>.</summary>
        public void Update(float now, Func<T, ItemSnapshot> state, List<Resolved> resolved)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (e.Prediction.Observe(state(e.Value), now) == PredictionPhase.Pending) continue;
                resolved?.Add(new Resolved(e.Value, e.Prediction));
                _entries.RemoveAt(i);
                i--;
            }
        }

        /// <summary>Ends everything (the player left): adds every waiting value to <paramref name="pending"/>.</summary>
        public void Clear(List<T> pending)
        {
            foreach (Entry e in _entries) pending?.Add(e.Value);
            _entries.Clear();
        }
    }
}
