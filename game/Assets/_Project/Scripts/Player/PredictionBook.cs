using System;
using System.Collections.Generic;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 027b (review), pure: the owner's predicted looks that are still waiting for the server, and the matching of
    /// the server's answers to requests. Spec 045: every request carries its number (<see cref="Sent"/>) and the server
    /// echoes it in its answer (a rate-limited request gets a TooFast answer), so an answer reaches only the prediction of
    /// that very request, and only when it is of the matching kind. Nothing is counted: an answer that comes late (a server
    /// stall over a second, a slow round trip) can never be taken for a newer request's, and a refusal applies at once, also
    /// with more requests in flight. An answer for a number this book never sent, or one already answered, is ignored. A
    /// newer prediction for the same item replaces the older one silently; predictions for other items keep going until they
    /// resolve on their own, so a quick second click never makes the first item bounce. The echo is only matched here; the
    /// server never trusts it for anything.
    /// </summary>
    public sealed class PredictionBook<T> where T : class
    {
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
        private int _lastAnswered;

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

        /// <summary>A request that the server answers goes out: returns its number, sent with it and echoed in the answer.</summary>
        public int Sent() => ++_sent;

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
        /// The answer to request <paramref name="request"/> (the number the server echoed) arrived: it reaches only that request's
        /// prediction, if the kind matches; an "ok" restarts its timeout, a refusal denies it. Answers come in order (one reliable
        /// channel), so a number this book never sent or one not newer than the last answered is ignored. Returns the request it
        /// answered (0 when ignored).
        /// </summary>
        public int Answer(int request, AnswerKind kind, bool ok, float now)
        {
            if (request <= _lastAnswered || request > _sent) return 0;
            _lastAnswered = request;
            foreach (Entry e in _entries)
                if (e.Request == request && AnswerFor(e.Prediction.Kind) == kind) e.Prediction.NoteAnswer(ok, now);
            return request;
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
