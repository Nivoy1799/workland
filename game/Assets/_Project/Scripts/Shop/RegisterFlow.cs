using System;
using System.Collections.Generic;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Game.Npc;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 032 (review), server, pure: a served guest from the hand-over to the street, around the register's money steps
    /// (<see cref="RegisterDesk"/>). CustomerService only routes: the presentation goes through <see cref="IGuestStage"/>, the money
    /// through <see cref="IRegisterWallet"/>, the numbers into the <see cref="Shift"/>.
    /// - <see cref="HandOver"/>: <see cref="RegisterDesk.Open"/> is the commit point. Once the guest is in the register's line it pays
    ///   there (exactly once); what is shown after it can fail without a second payment (the caller's fallback pays only a guest who
    ///   is not in line).
    /// - <see cref="Use"/>: any cook works the head payment; the money goes to the payee.
    /// - <see cref="SettleAutomatically"/>: closing, the payee leaving, the head untouched for <see cref="IdleSettleSeconds"/>, the guest's
    ///   object gone: the bill only, no tip. A refused settle still sends the guest out (no ghost guests).
    /// - Finishing: the guest leaves first, then the feed and the reward, each on its own (one failing never keeps a guest).
    /// </summary>
    public sealed class RegisterFlow
    {
        /// <summary>The head payment settles by itself (bill only) when nobody touched the register this long (s).</summary>
        public const float IdleSettleSeconds = 30f;

        private readonly RegisterDesk _desk;
        private readonly Shift _shift;
        private int _headId = -1;
        private float _headSince;

        public RegisterFlow(Shift shift, RegisterDesk desk = null)
        {
            _shift = shift ?? throw new ArgumentNullException(nameof(shift));
            _desk = desk ?? new RegisterDesk();
        }

        public RegisterDesk Desk => _desk;

        /// <summary>
        /// Spec 037: the tip's share (permille) for a guest's order at the menu board's price (MenuBoardDesk.TipPermilleOf: dearer than
        /// the reference, a smaller tip); null is 1000.
        /// </summary>
        public Func<GuestPick, int> TipShare { get; set; }

        /// <summary>Spec 035: the shop's hygiene score when a guest reviews (below 50 % the review loses a star; Sim.Economy.HygieneRules).</summary>
        public int HygienePercent { get; set; } = HygieneRules.StartPercent;

        /// <summary>
        /// 032 review: the counter pays the bill at once only when the hand-over did not happen and the guest is not in the register's
        /// line (a guest in the line pays there, exactly once, whatever failed after it joined).
        /// </summary>
        public static bool FallbackPays(bool handedOver, bool waitingInLine) => !handedOver && !waitingInLine;

        /// <summary>
        /// The guest of <paramref name="pick"/> takes its dish from <paramref name="payee"/>: the bill (<paramref name="simPayout"/>, spec 037:
        /// the menu board's price, at the pick's share), the tip (speed, accuracy, quality), the way of paying from its <paramref name="order"/>; it joins the register's
        /// line (the commit point), then the stage shows it. Returns the payment (never null).
        /// </summary>
        public RegisterPayment HandOver(GuestPick pick, int payee, long simPayout, GuestOrder order, IGuestStage stage, float now,
            int tipPermille = -1)
        {
            // Spec 037: cents; the bill and the tip are whole coins ($0.10), so the change is always exact; the tip falls when the board's
            // price is above the reference (TipShare).
            long bill = CashRules.ToCoins(EconomyRules.ApplyPermille(simPayout, pick.BillPermille));
            // 037 review: the tip share stored when the guest joined (<paramref name="tipPermille"/>), else the board's now.
            int share = tipPermille >= 0 ? tipPermille : TipShare != null ? TipShare(pick) : EconomyRules.FullPayPermille;
            long tip = CashRules.ToCoins(EconomyRules.ApplyPermille(TipRules.Tip(bill, pick.WaitedTenths, pick.PatienceTenths, pick.Fit.Misses,
                pick.Fit.Stars), share));
            order ??= new GuestOrder(Array.Empty<OrderModifier>(), GuestPatience.BaseSeconds, true, CashRules.GuestNotes[0]);
            var payment = new RegisterPayment
            {
                GuestId = pick.CustomerId,
                Payee = payee,
                Dish = pick.Dish,
                Bill = bill,
                Tip = tip,
                Card = order.PaysByCard,
                OrderStars = pick.Fit.Stars,
                Misses = pick.Fit.Misses,
                WaitedTenths = pick.WaitedTenths,
                PatienceTenths = pick.PatienceTenths,
                Note = pick.Fit.Note, // spec 036
            };
            payment.Handed = payment.Card ? payment.Due : CashRules.Handed(payment.Due, order.Note);
            _desk.Open(payment); // the commit point: from here the register pays it, exactly once
            Touch(now);
            Show(stage, s => s.Paying(payment.GuestId, CustomerRules.PayText(payment.Card, payment.Handed), _desk.Line.Count - 1));
            Show(stage, s => s.Log("[Customers] handed " + Menu.NameOf(pick.Dish) + " over: " + OrderRules.Describe(pick.Fit.Match) + " ("
                + pick.Fit.Misses + " short, " + pick.Fit.Stars + " stars), bill " + EconomyRules.Money(bill) + " + tip " + EconomyRules.Money(tip)
                + ", " + (payment.Card ? "card" : "cash " + EconomyRules.Money(payment.Handed) + ", change " + EconomyRules.Money(payment.ChangeDue))
                + "; waited " + (pick.WaitedTenths / 10f).ToString("0.0") + " of " + (pick.PatienceTenths / 10f).ToString("0.0") + " s"));
            return payment;
        }

        /// <summary>
        /// One click on the register by any cook <paramref name="player"/> (the desk decides; <paramref name="wallet"/> pays the head's
        /// payee). A settled payment finishes (review, numbers, the guest leaves).
        /// </summary>
        public CookOutcome Use(int player, int option, IRegisterWallet wallet, IGuestStage stage, float now)
        {
            CookOutcome outcome = _desk.Use(player, option, wallet, out RegisterSettlement settled);
            if (outcome == CookOutcome.Ok) Touch(now);
            if (outcome == CookOutcome.Ok && settled.Payment != null) Finish(settled, stage, true);
            return outcome;
        }

        /// <summary>
        /// Guest <paramref name="guestId"/> pays its bill by itself, no tip (<paramref name="why"/> for the log). Settled or refused, it
        /// leaves the line and (when <paramref name="leaves"/>: its object still exists) walks out. False when it was not in line.
        /// </summary>
        public bool SettleAutomatically(int guestId, IRegisterWallet wallet, IGuestStage stage, string why, bool leaves = true)
        {
            if (!_desk.IsWaiting(guestId)) return false;
            if (_desk.SettleAutomatically(guestId, wallet, out RegisterSettlement s))
            {
                Show(stage, st => st.Log("[Register] " + why + ": guest " + guestId + " settles by itself"));
                Finish(s, stage, leaves);
                return true;
            }
            _desk.Remove(guestId);
            Show(stage, st => st.Log("[Register] " + why + ": guest " + guestId + "'s payment was refused; it leaves"));
            if (leaves) Show(stage, st => st.Leave(guestId, CustomerRules.MissedText(MissReason.Gone), CustomerRules.MoodMissed));
            Show(stage, st => st.LineMoved());
            return true;
        }

        /// <summary>Closing: every waiting payment settles by itself (bill only) and its guest leaves.</summary>
        public void SettleAll(Func<int, IRegisterWallet> walletFor, IGuestStage stage)
        {
            while (_desk.Head != null)
            {
                int guest = _desk.Head.GuestId;
                SettleAutomatically(guest, walletFor?.Invoke(guest), stage, "the kitchen closed");
                if (_desk.IsWaiting(guest)) _desk.Remove(guest); // never loops
            }
        }

        /// <summary>
        /// Once a frame: a head payment nobody touched for <see cref="IdleSettleSeconds"/> settles by itself (one cook away never
        /// blocks the till). True when it did.
        /// </summary>
        public bool Tick(float now, Func<int, IRegisterWallet> walletFor, IGuestStage stage)
        {
            RegisterPayment head = _desk.Head;
            if (head == null)
            {
                _headId = -1;
                return false;
            }
            if (head.GuestId != _headId)
            {
                _headId = head.GuestId;
                _headSince = now;
                return false;
            }
            if (now - _headSince < IdleSettleSeconds) return false;
            return SettleAutomatically(head.GuestId, walletFor?.Invoke(head.GuestId), stage, "nobody took the payment");
        }

        /// <summary>A guest's object is gone while it waited to pay: it settles by itself (the money is not lost), it cannot walk out.</summary>
        public bool Exited(int guestId, IRegisterWallet wallet, IGuestStage stage) =>
            SettleAutomatically(guestId, wallet, stage, "the guest is gone", leaves: false);

        private void Touch(float now)
        {
            RegisterPayment head = _desk.Head;
            _headId = head != null ? head.GuestId : -1;
            _headSince = now;
        }

        /// <summary>The payment went through: the guest leaves first (its review), then the numbers, the feed and the reward.</summary>
        private void Finish(RegisterSettlement s, IGuestStage stage, bool leaves)
        {
            RegisterPayment p = s.Payment;
            GuestReview review = ReviewRules.Review(p.OrderStars, p.WaitedTenths, p.PatienceTenths, p.Misses, s.WrongChange, HygienePercent);
            if (leaves) Show(stage, st => st.Leave(p.GuestId, CustomerRules.ReviewText(review, p.Note), CustomerRules.MoodOf(review)));
            Show(stage, st => st.LineMoved());
            long tip = s.TipPaid;
            _shift.RecordSale(p.OrderStars, s.Paid, tip, review);
            string how = s.Automatic ? "settled by the register, no tip" : p.Card ? "card" : s.WrongChange ? "cash, wrong change ("
                + EconomyRules.Money(s.ChangeGiven) + " for " + EconomyRules.Money(p.ChangeDue) + ")" : "cash";
            Show(stage, st => st.Feed("Guest paid " + EconomyRules.Money(s.Paid) + " (tip " + EconomyRules.Money(tip) + ", " + how + ") · "
                + Menu.NameOf(p.Dish) + " · " + review.Stars + "★ \"" + CustomerRules.ReviewWords(review, p.Note) + "\""));
            Show(stage, st => st.Reward(review.Stars, s.Paid, tip));
            Show(stage, st => st.Log("[Register] guest " + p.GuestId + " paid " + EconomyRules.Money(s.Paid) + " of " + EconomyRules.Money(p.Due)
                + " (tip " + EconomyRules.Money(tip) + ", " + (p.Card ? "card" : "change " + EconomyRules.Money(s.ChangeGiven) + " of "
                + EconomyRules.Money(p.ChangeDue)) + (s.Automatic ? ", settled by the register" : string.Empty) + "); review " + review.Stars
                + " stars, " + ReviewRules.KeyId(review.Key)));
        }

        /// <summary>One presentation step; a failure is logged through the stage when it can be, never thrown (money is done).</summary>
        private static void Show(IGuestStage stage, Action<IGuestStage> step)
        {
            if (stage == null) return;
            try { step(stage); }
            catch (Exception e)
            {
                try { stage.Log("[Register] a presentation step failed: " + e.GetType().Name + ": " + e.Message); }
                catch (Exception) { /* nothing more to do */ }
            }
        }
    }
}
