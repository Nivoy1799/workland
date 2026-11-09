namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 029, pure: the fryer's controls and the basket's synced bytes, shared by the server (KitchenState) and clients
    /// (prompts, lights, the basket's arc). Station options: <see cref="PowerOption"/> switches the fryer, <see cref="LowerOption"/> and
    /// <see cref="RaiseOption"/> lower or raise basket b (RequestStation; spec 029b: an intent, not a toggle). The fryer's StationContents byte: <see cref="FlagOn"/> plus a lowered bit per
    /// basket. A basket's Vessel.Mode byte: its <see cref="BasketState"/> in the low two bits and a shake counter (mod 4) above,
    /// so every viewer sees each shake. Times, oil temperature and the shake bonus never leave the server.
    /// </summary>
    public static class FryerRules
    {
        public const int BasketsPerFryer = 2;
        /// <summary>Station option: the fryer's on/off knob.</summary>
        public const int PowerOption = 1;
        /// <summary>Spec 029 default to review: a raised basket drips this long (real seconds).</summary>
        public const float DrainSeconds = 2f;
        /// <summary>The fryer's fixed oil temperature (C; players never see it, D21).</summary>
        public const int OilTempC = 180;

        public const byte FlagOn = 1;
        private const int StateMask = 0x03;
        private const int ShakeShift = 2;
        private const int ShakeMask = 0x03;

        /// <summary>
        /// The scene option of basket <paramref name="basket"/>'s lower/raise button: 2 and 3 (its identity; what a click sends
        /// is <see cref="IntentOption"/>).
        /// </summary>
        public static int BasketOption(int basket) => LowerOption(basket);

        /// <summary>Spec 029b: the request "lower basket b" (2 and 3). Refused when it is already in the oil.</summary>
        public static int LowerOption(int basket) => 2 + basket;

        /// <summary>Spec 029b: the request "raise basket b" (4 and 5). Refused when it is not in the oil.</summary>
        public static int RaiseOption(int basket) => 2 + BasketsPerFryer + basket;

        /// <summary>
        /// Spec 029b: what a click on basket b's button asks for, from where the clicker sees the basket: up when it is in the
        /// oil, else down. An intent, not a toggle, so two players clicking at once never cancel each other.
        /// </summary>
        public static int IntentOption(int basket, bool seenLowered) => seenLowered ? RaiseOption(basket) : LowerOption(basket);

        /// <summary>Reads any basket option back (lower or raise); false for the power knob and anything else.</summary>
        public static bool TryParseBasketOption(int option, out int basket) => TryParseBasketOption(option, out basket, out _);

        /// <summary>Spec 029b: reads a basket request back: which basket, and whether it asks to lower it.</summary>
        public static bool TryParseBasketOption(int option, out int basket, out bool lower)
        {
            lower = option < 2 + BasketsPerFryer;
            basket = lower ? option - 2 : option - 2 - BasketsPerFryer;
            if (option >= 2 && basket >= 0 && basket < BasketsPerFryer) return true;
            basket = -1;
            lower = false;
            return false;
        }

        /// <summary>The fryer byte: on, and which baskets are in the oil.</summary>
        public static byte FryerFlags(bool on, bool lowered0, bool lowered1)
        {
            return (byte)((on ? FlagOn : 0) | (lowered0 ? 2 : 0) | (lowered1 ? 4 : 0));
        }

        public static bool IsOn(byte flags) => (flags & FlagOn) != 0;

        public static bool IsLowered(byte flags, int basket) =>
            basket >= 0 && basket < BasketsPerFryer && (flags & (2 << basket)) != 0;

        /// <summary>
        /// Spec 030 (029b review): the fryer byte with basket <paramref name="basket"/>'s lowered bit set to where this client
        /// sees it (its own pending arc first, BasketView.SeenLowered), so the button's prompt says what its click will ask for.
        /// </summary>
        public static byte WithLowered(byte flags, int basket, bool lowered)
        {
            if (basket < 0 || basket >= BasketsPerFryer) return flags;
            int bit = 2 << basket;
            return (byte)(lowered ? flags | bit : flags & ~bit);
        }

        /// <summary>A basket's mode byte from its state and how often it was shaken.</summary>
        public static byte Mode(BasketState state, int shakes)
        {
            return (byte)(((int)state & StateMask) | ((shakes & ShakeMask) << ShakeShift));
        }

        public static BasketState StateOf(byte mode)
        {
            int s = mode & StateMask;
            return s <= (int)BasketState.Draining ? (BasketState)s : BasketState.Raised;
        }

        public static int ShakesOf(byte mode) => (mode >> ShakeShift) & ShakeMask;

        /// <summary>Spec 029: a shake counts for the even-fry bonus only inside the good window (Good or Perfect), once per fry.</summary>
        public static bool ShakeCounts(HeatZone zone) => zone == HeatZone.Good || zone == HeatZone.Perfect;

        /// <summary>Ticks of the drain at <paramref name="tickRate"/> (at least one).</summary>
        public static uint DrainTicks(double tickRate)
        {
            if (!(tickRate > 0d)) return 1u;
            double ticks = System.Math.Ceiling(DrainSeconds * tickRate);
            return ticks < 1d ? 1u : (uint)ticks;
        }

        /// <summary>Third-person and button prompt words: "lower basket", "raise basket".</summary>
        public static string BasketButtonText(bool lowered) => lowered ? "raise the basket" : "lower the basket";
    }
}
