using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Npc;

namespace Worktown.Game.UI
{
    /// <summary>
    /// Spec 022: the ticket rail board above the counter, a global scene object. The server writes the open orders
    /// (dish id and minutes waited, packed by <see cref="CustomerRules.PackTicket"/>) in queue order and the shift
    /// numbers; every client draws the cards and the HUD and the summary panel read the values. No RPCs; only dish ids,
    /// coarse minutes and whole numbers leave the server (D21).
    /// </summary>
    public sealed class TicketRailView : NetworkBehaviour
    {
        private static readonly Color TitleOpen = Palette.Mustard; // spec 026
        private static readonly Color TitleClosed = Palette.Steel;

        public readonly SyncList<int> Tickets = new SyncList<int>();
        public readonly SyncVar<bool> ShiftOpen = new SyncVar<bool>(false);
        public readonly SyncVar<bool> SummaryOpen = new SyncVar<bool>(false);
        public readonly SyncVar<int> Day = new SyncVar<int>(0);
        // The open shift so far.
        public readonly SyncVar<int> LiveServed = new SyncVar<int>(0);
        public readonly SyncVar<int> LiveEarned = new SyncVar<int>(0);
        public readonly SyncVar<int> LiveStarsTenths = new SyncVar<int>(0);
        public readonly SyncVar<int> LiveMissed = new SyncVar<int>(0);
        // The last finished shift (the summary); LastDay 0 = none yet.
        public readonly SyncVar<int> LastDay = new SyncVar<int>(0);
        public readonly SyncVar<int> LastServed = new SyncVar<int>(0);
        public readonly SyncVar<int> LastEarned = new SyncVar<int>(0);
        public readonly SyncVar<int> LastStarsTenths = new SyncVar<int>(0);
        public readonly SyncVar<int> LastMissed = new SyncVar<int>(0);

        [SerializeField] private TextMesh _title;
        [Tooltip("One text per card, in rail order.")]
        [SerializeField] private TextMesh[] _cards = new TextMesh[0];
        [Tooltip("The card backs, same order as the texts.")]
        [SerializeField] private Renderer[] _cardBacks = new Renderer[0];

        private void Awake()
        {
            Tickets.OnChange += (_, _, _, _, _) => Refresh();
            ShiftOpen.OnChange += (_, _, _) => Refresh();
        }

        public override void OnStartClient()
        {
            Refresh();
        }

        /// <summary>Server: the open orders in queue order; only changed entries are sent.</summary>
        internal void ServerSetTickets(IReadOnlyList<int> tickets)
        {
            for (int i = 0; i < tickets.Count; i++)
            {
                if (i < Tickets.Count)
                {
                    if (Tickets[i] != tickets[i]) Tickets[i] = tickets[i];
                }
                else
                {
                    Tickets.Add(tickets[i]);
                }
            }
            while (Tickets.Count > tickets.Count) Tickets.RemoveAt(Tickets.Count - 1);
            Refresh();
        }

        /// <summary>Server: the shift state and its numbers (only changed values are sent).</summary>
        internal void ServerSetShift(Shift shift)
        {
            ShiftOpen.Value = shift.IsOpen;
            SummaryOpen.Value = shift.SummaryShowing;
            Day.Value = (int)shift.Day;
            LiveServed.Value = shift.Live.Served;
            LiveEarned.Value = Clamp(shift.Live.Earned);
            LiveStarsTenths.Value = shift.Live.AverageStarsTenths;
            LiveMissed.Value = shift.Live.Missed;
            LastDay.Value = (int)shift.LastDay;
            LastServed.Value = shift.Last.Served;
            LastEarned.Value = Clamp(shift.Last.Earned);
            LastStarsTenths.Value = shift.Last.AverageStarsTenths;
            LastMissed.Value = shift.Last.Missed;
            Refresh();
        }

        private static int Clamp(long value) => value > int.MaxValue ? int.MaxValue : (int)System.Math.Max(0L, value);

        private void Refresh()
        {
            if (_title != null)
            {
                _title.text = ShiftOpen.Value ? "ORDERS" : "KITCHEN CLOSED";
                _title.color = ShiftOpen.Value ? TitleOpen : TitleClosed;
            }
            for (int i = 0; i < _cards.Length; i++)
            {
                bool used = i < Tickets.Count;
                if (_cards[i] != null)
                {
                    _cards[i].gameObject.SetActive(used);
                    if (used) _cards[i].text = CustomerRules.TicketText(Tickets[i]);
                }
                if (i < _cardBacks.Length && _cardBacks[i] != null) _cardBacks[i].gameObject.SetActive(used);
            }
        }
    }
}
