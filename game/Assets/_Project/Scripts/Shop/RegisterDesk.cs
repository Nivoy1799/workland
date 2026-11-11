using System.Collections.Generic;
using Worktown.Game.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 032, server, pure: the register's money steps (AGENTS rule 6), the 031b <see cref="AssemblyDesk"/> pattern: every step
    /// checks first, then pays once through <see cref="IRegisterWallet"/>, then commits; a refusal changes nothing and pays nothing.
    /// Guests who were handed their food wait in line (<see cref="Open"/>); the register works the first one.
    /// - Cash: each click on a tray slot adds that coin or note to the change on the server (<see cref="Use"/>); "take back" empties
    ///   the tray; "hand over" settles: the payee gets the due less the difference of a wrong change (<see cref="CashRules.CashPayout"/>).
    /// - Card: one click on the terminal settles the due.
    /// - 032 review (orchestrator): any cook works the head payment (co-op: one cook away never blocks the till); the money still goes
    ///   to the payee (the player who handed the food over).
    /// - <see cref="SettleAutomatically"/>: the kitchen closed, the payee left or nobody took the payment in time: the guest pays the
    ///   bill by itself, without a tip, so skipping the register is never better than counting the change.
    /// A settled payment leaves the line, so it can never pay twice.
    /// </summary>
    public sealed class RegisterDesk
    {
        private readonly List<RegisterPayment> _line = new List<RegisterPayment>();

        public IReadOnlyList<RegisterPayment> Line => _line;

        /// <summary>Spec 053: false at the food corner (cash only): the card terminal refuses with NotOpenYet and changes nothing.</summary>
        public bool AcceptsCard { get; set; } = true;
        public RegisterPayment Head => _line.Count > 0 ? _line[0] : null;

        /// <summary>A guest who was handed its food joins the line (once per guest).</summary>
        public void Open(RegisterPayment payment)
        {
            if (payment == null || IndexOf(payment.GuestId) >= 0) return;
            _line.Add(payment);
        }

        public bool IsWaiting(int guestId) => IndexOf(guestId) >= 0;

        /// <summary>
        /// One click on register option <paramref name="option"/> by any cook <paramref name="player"/>: a tray slot adds its value,
        /// take back empties the tray, hand over or the terminal settles (<paramref name="settled"/>). Refusals: NoPayment, PaysByCard /
        /// PaysCash (the other way of paying), TrayFull, TrayEmpty, InvalidOption, and the wallet's own; each changes nothing and pays
        /// nothing.
        /// </summary>
        public CookOutcome Use(int player, int option, IRegisterWallet wallet, out RegisterSettlement settled)
        {
            settled = default;
            if (option < 0 || option >= RegisterRules.SlotCount) return CookOutcome.InvalidOption;
            if (option == RegisterRules.CardOption && !AcceptsCard) return CookOutcome.NotOpenYet; // spec 053
            RegisterPayment head = Head;
            if (head == null) return CookOutcome.NoPayment;
            if (option == RegisterRules.CardOption)
                return head.Card ? Settle(head, head.Due, 0, false, wallet, out settled) : CookOutcome.PaysCash;
            if (head.Card) return CookOutcome.PaysByCard;
            if (RegisterRules.TryParseTray(option, out int value))
            {
                if (head.Tray + value > CashRules.MaxTray) return CookOutcome.TrayFull;
                head.Tray += value;
                return CookOutcome.Ok;
            }
            if (option == RegisterRules.TakeBackOption)
            {
                if (head.Tray <= 0) return CookOutcome.TrayEmpty;
                head.Tray = 0;
                return CookOutcome.Ok;
            }
            // Hand over the change.
            long paid = CashRules.CashPayout(head.Due, head.ChangeDue, head.Tray);
            return Settle(head, paid, head.Tray, false, wallet, out settled);
        }

        /// <summary>
        /// The kitchen closed, the payee left or nobody took the payment in time: guest <paramref name="guestId"/> pays its bill by
        /// itself, no tip (032 review), no tray, no mistake. False when it is not in line or the wallet refused (then it stays in line;
        /// the caller decides).
        /// </summary>
        public bool SettleAutomatically(int guestId, IRegisterWallet wallet, out RegisterSettlement settled)
        {
            settled = default;
            int index = IndexOf(guestId);
            if (index < 0) return false;
            RegisterPayment p = _line[index];
            return Settle(p, p.Bill, p.ChangeDue, true, wallet, out settled) == CookOutcome.Ok;
        }

        /// <summary>Forgets a guest without paying (its object is gone); false when it was not in line.</summary>
        public bool Remove(int guestId)
        {
            int index = IndexOf(guestId);
            if (index < 0) return false;
            _line.RemoveAt(index);
            return true;
        }

        public void Clear() => _line.Clear();

        private CookOutcome Settle(RegisterPayment p, long amount, long changeGiven, bool automatic, IRegisterWallet wallet,
            out RegisterSettlement settled)
        {
            settled = default;
            long paid = amount;
            if (wallet != null)
            {
                CookOutcome ok = wallet.Pay(p.Payee, amount, out paid);
                if (ok != CookOutcome.Ok) return ok;
            }
            _line.Remove(p);
            settled = new RegisterSettlement(p, paid, changeGiven, automatic);
            return CookOutcome.Ok;
        }

        private int IndexOf(int guestId)
        {
            for (int i = 0; i < _line.Count; i++)
                if (_line[i].GuestId == guestId) return i;
            return -1;
        }
    }
}
