using Worktown.Game.Economy;
using Worktown.Sim.Economy;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 032, pure: the register on the counter. Its station options are its slots (like the assembly board's tools): the tray's
    /// coins and notes (<see cref="CashRules.Tray"/>, options 0-7: spec 037's $0.10 to $20), "take the change back" (8), "hand over the
    /// change" (9) and the card terminal (10). Clients send only the option (RequestStation); the server builds the change and decides.
    /// What clients see of the payment is one short (<see cref="PackPayment"/>: card, or cash and the change due in cents) and the
    /// tray's sum in cents as a ushort (spec 037: a byte held whole dollars).
    /// </summary>
    public static class RegisterRules
    {
        public const int TakeBackOption = 8;
        public const int GiveChangeOption = 9;
        public const int CardOption = 10;
        public const int SlotCount = 11;

        public const ushort NoPayer = 0;
        public const ushort CardBit = 0x8000;
        public const ushort CashBit = 0x4000;
        public const int MaxChange = 0x3FFF;

        public static bool IsTraySlot(int option) => option >= 0 && option < CashRules.Tray.Count;

        /// <summary>The value of tray option <paramref name="option"/>; false for any other option.</summary>
        public static bool TryParseTray(int option, out int value)
        {
            value = CashRules.ValueOf(option);
            return IsTraySlot(option);
        }

        /// <summary>The head payer for clients: nobody (0), card, or cash with its change due (capped at <see cref="MaxChange"/>).</summary>
        public static ushort PackPayment(bool anyPayer, bool card, long changeDue)
        {
            if (!anyPayer) return NoPayer;
            if (card) return CardBit;
            long change = changeDue < 0 ? 0 : changeDue > MaxChange ? MaxChange : changeDue;
            return (ushort)(CashBit | change);
        }

        public static bool HasPayer(ushort payment) => (payment & (CardBit | CashBit)) != 0;
        public static bool IsCard(ushort payment) => (payment & CardBit) != 0;
        public static bool IsCash(ushort payment) => (payment & CashBit) != 0 && !IsCard(payment);
        public static int ChangeDue(ushort payment) => IsCash(payment) ? payment & MaxChange : 0;

        /// <summary>The register's display: "CARD", "CHANGE $0.20 / $0.50" (given / due), or a dash with nobody to pay.</summary>
        public static string DisplayText(ushort payment, int tray)
        {
            if (IsCard(payment)) return "CARD";
            if (!IsCash(payment)) return "-";
            return "CHANGE " + EconomyRules.Money(tray) + " / " + EconomyRules.Money(ChangeDue(payment));
        }

        /// <summary>Spec 037: the short label on a tray compartment: "10c", "20c", "50c", "$1", "$2", "$5", "$10", "$20".</summary>
        public static string TrayLabel(int cents) =>
            cents < (int)EconomyRules.CentsPerDollar ? cents + "c"
            : cents % (int)EconomyRules.CentsPerDollar == 0 ? "$" + cents / (int)EconomyRules.CentsPerDollar : EconomyRules.Money(cents);

        /// <summary>The slot's name: "$5.00", "take back", "hand over", "card".</summary>
        public static string SlotName(int option)
        {
            if (TryParseTray(option, out int value)) return EconomyRules.Money(value);
            switch (option)
            {
                case TakeBackOption: return "take back";
                case GiveChangeOption: return "hand over";
                case CardOption: return "card terminal";
                default: return "register";
            }
        }

        /// <summary>
        /// Why a slot does nothing now (shown grey), or null when it acts: nobody pays, the guest pays the other way, the tray is
        /// full or empty. "Hand over" with an empty tray is fine (the guest gave the exact amount, or the change is wrong).
        /// </summary>
        public static string Reason(int option, ushort payment, int tray)
        {
            if (option < 0 || option >= SlotCount) return null; // the register's body: nothing to say
            if (!HasPayer(payment)) return "Nobody is paying";
            if (option == CardOption) return IsCard(payment) ? null : "They pay cash";
            if (IsCard(payment)) return "They pay by card";
            if (TryParseTray(option, out int value)) return tray + value > CashRules.MaxTray ? "The tray is full" : null;
            if (option == TakeBackOption) return tray > 0 ? null : "The tray is empty";
            return null;
        }

        /// <summary>The prompt's words: "give $0.50", "take the change back", "hand over $1.80", "card payment".</summary>
        public static string ActionWords(int option, int tray)
        {
            if (TryParseTray(option, out int value)) return "give " + EconomyRules.Money(value);
            switch (option)
            {
                case TakeBackOption: return "take the change back";
                case GiveChangeOption: return "hand over " + EconomyRules.Money(tray);
                case CardOption: return "card payment";
                default: return "use the register";
            }
        }
    }
}
