using System;
using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Economy;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// Pure rules for visible NPC customers (spec 014). Customers only show the sim's open demand;
    /// they never decide prices. Spec 022: each one orders a menu dish; the share of the sim's sale
    /// price they pay comes from <see cref="OrderRules"/>, computed on the server.
    /// </summary>
    public static class CustomerRules
    {
        /// <summary>Most customers in the queue at once (spec 014 open question 1).</summary>
        public const int MaxVisible = 6;
        public const float WalkSpeed = 2f;
        public const float SlotSpacing = 1f;

        /// <summary>Most valuable first, so the queue shows what matters.</summary>
        public static readonly IReadOnlyList<string> WishOrder = new[] { "meal", "snack", "drink" };

        /// <summary>
        /// The wishes to show, in queue order: meal orders first, then snack, then drink, each up to
        /// its open demand, all together capped at <see cref="MaxVisible"/>.
        /// </summary>
        public static IReadOnlyList<string> DesiredWishes(Func<string, int> openDemand)
        {
            var wishes = new List<string>(MaxVisible);
            foreach (string market in WishOrder)
            {
                int demand = Math.Max(0, openDemand(market));
                for (int i = 0; i < demand && wishes.Count < MaxVisible; i++) wishes.Add(market);
                if (wishes.Count >= MaxVisible) break;
            }
            return wishes;
        }

        /// <summary>
        /// Spec 022: like <see cref="DesiredWishes(Func{string, int})"/>, but only markets that have a dish on the menu
        /// (<paramref name="hasDishes"/>; nobody can order a drink yet) and nobody while the shift is closed.
        /// </summary>
        public static IReadOnlyList<string> DesiredWishes(Func<string, int> openDemand, Func<string, bool> hasDishes, bool shiftOpen)
        {
            if (!shiftOpen) return new string[0];
            return DesiredWishes(market => hasDishes == null || hasDishes(market) ? openDemand(market) : 0);
        }

        // ---- spec 022: orders ----

        /// <summary>Seed of the server's order RNG (AGENTS rule 7): same seed and events, same orders.</summary>
        public const ulong OrderSeed = 22;

        /// <summary>Mood bytes synced on the customer: waiting, served exact, close, wrong, left unserved.</summary>
        public const byte MoodWaiting = 0;
        public const byte MoodHappy = 1;
        public const byte MoodOk = 2;
        public const byte MoodUnhappy = 3;
        public const byte MoodMissed = 4;

        public const int MaxTicketMinutes = 0xFFFF;

        public static byte MoodOf(OrderMatch match)
        {
            switch (match)
            {
                case OrderMatch.Exact: return MoodHappy;
                case OrderMatch.Close: return MoodOk;
                default: return MoodUnhappy;
            }
        }

        /// <summary>The ticket above a waiting customer: the dish name.</summary>
        public static string OrderText(byte dish)
        {
            return Menu.NameOf(dish);
        }

        /// <summary>What a served customer says on the way out.</summary>
        public static string LeaveText(OrderMatch match, long pay)
        {
            string money = EconomyRules.Money(pay);
            switch (match)
            {
                case OrderMatch.Exact: return "Thanks! +" + money;
                case OrderMatch.Close: return "Close enough. +" + money;
                default: return "That's not what I ordered. +" + money;
            }
        }

        /// <summary>In-game minutes waited: one real second is one in-game minute at 60 s per hour.</summary>
        public static int WaitMinutes(float realSeconds)
        {
            if (!(realSeconds > 0f)) return 0;
            double minutes = realSeconds * 60.0 / EconomyRules.SecondsPerHour;
            return minutes >= MaxTicketMinutes ? MaxTicketMinutes : (int)minutes;
        }

        /// <summary>One rail ticket as an int for the synced list: dish id in the low byte, minutes waited above.</summary>
        public static int PackTicket(byte dish, int minutes)
        {
            minutes = Math.Max(0, Math.Min(MaxTicketMinutes, minutes));
            return dish | (minutes << 8);
        }

        public static byte TicketDish(int ticket) => (byte)(ticket & 0xFF);

        public static int TicketMinutes(int ticket) => (ticket >> 8) & MaxTicketMinutes;

        /// <summary>Card text on the ticket rail: "Fries" and "12 min".</summary>
        public static string TicketText(int ticket)
        {
            return Menu.NameOf(TicketDish(ticket)) + "\n" + TicketMinutes(ticket) + " min";
        }

        /// <summary>Queue position <paramref name="index"/>: from the head, along the queue direction.</summary>
        public static Vector3 QueueSlot(Vector3 head, Vector3 direction, int index)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector3.right;
            return head + direction.normalized * (SlotSpacing * Mathf.Max(0, index));
        }

        /// <summary>Bubble text for a wish, e.g. "Meal?".</summary>
        public static string WishText(string market)
        {
            if (string.IsNullOrEmpty(market)) return "?";
            return char.ToUpperInvariant(market[0]) + market.Substring(1) + "?";
        }
    }
}
