using System;
using System.Collections.Generic;
using Worktown.Sim.Core;

namespace Worktown.Sim.Economy
{
    /// <summary>
    /// Spec 032: paying at the register, pure. Half the guests pay by card, half in cash (drawn from the injected seeded RNG). A
    /// cash guest rounds the bill up to whole notes of its own size (drawn at the order); the register's tray has the coins and
    /// notes of <see cref="Tray"/> (the option order). Change is what was handed minus the bill. Wrong change costs the difference
    /// (either way: too much is lost, too little the guest gets back and it costs the same again), so a wrong click never pays.
    /// Spec 037 (D39 revises D23): money is integer cents. Coins of $0.10, $0.20, $0.50, $1 and $2, notes of $5, $10 and $20;
    /// what a guest owes is a whole number of the smallest coin (<see cref="ToCoins"/>), so the change is always exact in coins.
    /// </summary>
    public static class CashRules
    {
        /// <summary>Spec 037: the smallest coin ($0.10, in cents); every bill and tip a guest pays is a multiple of it.</summary>
        public const int CoinUnit = 10;
        /// <summary>Spec 037: the tray in cents, in option order: coins $0.10, $0.20, $0.50, $1, $2, notes $5, $10, $20.</summary>
        public static readonly IReadOnlyList<int> Tray = Array.AsReadOnly(new[] { 10, 20, 50, 100, 200, 500, 1000, 2000 });
        /// <summary>Spec 037: the note sizes a cash guest rounds up to, in cents ($5, $10, $20).</summary>
        public static readonly IReadOnlyList<int> GuestNotes = Array.AsReadOnly(new[] { 500, 1000, 2000 });
        /// <summary>The share of guests who pay by card (%).</summary>
        public const int CardPercent = 50;
        /// <summary>
        /// The most change the tray holds at once, in cents ($100; spec 037: it is synced as a ushort, and the change due is always
        /// below the largest note, $20).
        /// </summary>
        public const int MaxTray = 10000;

        /// <summary>Spec 037: <paramref name="amount"/> rounded down to whole <see cref="CoinUnit"/>s (never below 0).</summary>
        public static long ToCoins(long amount) => amount <= 0 ? 0 : amount / CoinUnit * CoinUnit;

        /// <summary>One draw: true for a card payment.</summary>
        public static bool PaysByCard(IRandom random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            return random.NextInt(0, 100) < CardPercent;
        }

        /// <summary>One draw: the note size a cash guest rounds up to.</summary>
        public static int DrawNote(IRandom random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            return GuestNotes[random.NextInt(0, GuestNotes.Count)];
        }

        /// <summary>What a cash guest hands for <paramref name="due"/>: the bill rounded up to whole <paramref name="note"/>s (at least one note).</summary>
        public static long Handed(long due, int note)
        {
            if (note <= 0) note = GuestNotes[0];
            if (due <= 0) return note;
            return (due + note - 1) / note * note;
        }

        public static long ChangeDue(long handed, long due) => Math.Max(0L, handed - due);

        /// <summary>The value of tray slot <paramref name="slot"/>, or 0 for none.</summary>
        public static int ValueOf(int slot) => slot >= 0 && slot < Tray.Count ? Tray[slot] : 0;

        /// <summary>The fewest coins and notes for <paramref name="amount"/>, largest first (how the tray and the tour count it out).</summary>
        public static IReadOnlyList<int> Coins(long amount)
        {
            var list = new List<int>();
            for (int i = Tray.Count - 1; i >= 0 && amount > 0; i--)
                while (amount >= Tray[i])
                {
                    list.Add(Tray[i]);
                    amount -= Tray[i];
                }
            return list;
        }

        /// <summary>What the register pays its player for a cash sale: the bill less the difference of a wrong change, never below 0.</summary>
        public static long CashPayout(long due, long changeDue, long changeGiven) => Math.Max(0L, due - Math.Abs(changeGiven - changeDue));
    }
}
