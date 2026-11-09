using Worktown.Game.Net.Rules;
using Worktown.Game.Shop;

namespace Worktown.Game.Tutorial
{
    /// <summary>
    /// Spec 038, pure: turns what the local player saw into <see cref="TutorialEvents"/>. It remembers the last action the crosshair
    /// offered (with its slot) and when; a new server answer that is "ok" soon after counts as that action done (a sale, a payment
    /// handed over or by card, an order). A hold shows in a station's byte: the sink's basin going down after a scrub, the spit's tray
    /// going up after a shave. Nothing is sent and nothing is decided here: it only reads answers the server gave the player anyway.
    /// </summary>
    public sealed class TutorialWatch
    {
        /// <summary>A server answer counts for the action seen at most this long before it.</summary>
        public const float ActionMemorySeconds = 1.5f;
        /// <summary>A station byte change counts for a hold seen at most this long before it (the scrub takes 1.5 s, a shave up to 3 s).</summary>
        public const float HoldMemorySeconds = 4f;

        private InteractionAction _action;
        private int _option;
        private float _actionAt = float.NegativeInfinity;
        private int _resultVersion = -1;
        private int _basin = -1;
        private int _tray = -1;

        public void Reset()
        {
            _action = InteractionAction.None;
            _option = 0;
            _actionAt = float.NegativeInfinity;
            _resultVersion = -1;
            _basin = -1;
            _tray = -1;
        }

        /// <summary>Every frame: what a click would do now (None is ignored, so the last real action is kept).</summary>
        public void SawAction(InteractionAction action, int option, float now)
        {
            if (action == InteractionAction.None) return;
            _action = action;
            _option = option;
            _actionAt = now;
        }

        /// <summary>The events since the last call, from the interactor's answer counter and the sink's and spit tray's synced counts.</summary>
        public TutorialEvents Observe(int resultVersion, bool lastOk, int sinkBasin, int trayCount, float now)
        {
            TutorialEvents events = TutorialEvents.None;
            float age = now - _actionAt;
            if (_resultVersion >= 0 && resultVersion != _resultVersion && lastOk && age <= ActionMemorySeconds)
                events |= EventOf(_action, _option);
            if (_basin >= 0 && sinkBasin < _basin && _action == InteractionAction.Scrub && age <= HoldMemorySeconds)
                events |= TutorialEvents.Scrubbed;
            if (_tray >= 0 && trayCount > _tray && _action == InteractionAction.Shave && age <= HoldMemorySeconds)
                events |= TutorialEvents.Shaved;
            _resultVersion = resultVersion;
            _basin = sinkBasin;
            _tray = trayCount;
            return events;
        }

        /// <summary>The event an "ok" answer to <paramref name="action"/> on slot <paramref name="option"/> means (None for most).</summary>
        public static TutorialEvents EventOf(InteractionAction action, int option)
        {
            switch (action)
            {
                case InteractionAction.Sell: return TutorialEvents.Served;
                case InteractionAction.Pay:
                    return option == RegisterRules.GiveChangeOption || option == RegisterRules.CardOption ? TutorialEvents.Paid : TutorialEvents.None;
                case InteractionAction.Order: return TutorialEvents.Ordered;
                default: return TutorialEvents.None;
            }
        }
    }
}
