using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Worktown.Sim.Economy
{
    /// <summary>Test/debug form of a state: versioned canonical text (integers only) and its SHA-256.</summary>
    public static class EconomySnapshot
    {
        public static string Canonical(EconomyState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var sb = new StringBuilder("v1");
            Add(sb, "tick", state.Tick);

            var markets = new List<MarketState>(state.Markets);
            markets.Sort((a, b) => string.CompareOrdinal(a.MarketId, b.MarketId));
            sb.Append("|markets=").Append(markets.Count);
            foreach (var m in markets)
            {
                sb.Append("|m:").Append(m.MarketId);
                var l = m.Last;
                Nums(sb, m.Price, m.OpenDemand, m.DemandCarry, m.DemandThisTick, m.PlayerFilledThisTick,
                    m.WholesaleThisTick, m.DemandPaidThisTick, l.Demand, l.PlayerFilled, l.Wholesale, l.NpcFilled, l.DemandPaid);
            }

            var accounts = new List<Account>(state.Accounts);
            accounts.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            sb.Append("|accounts=").Append(accounts.Count);
            foreach (var a in accounts)
            {
                sb.Append("|a:").Append(a.Id);
                Nums(sb, a.Balance);
                var goods = new List<GoodAmount>(a.Goods);
                goods.Sort((x, y) => string.CompareOrdinal(x.GoodId, y.GoodId));
                foreach (var g in goods) sb.Append(",g:").Append(g.GoodId).Append('=').Append(g.Units.ToString(CultureInfo.InvariantCulture));
            }

            var consumed = new List<ConsumedInstance>(state.RecentConsumed);
            consumed.Sort((x, y) => x.Tick != y.Tick ? x.Tick.CompareTo(y.Tick) : x.InstanceId.CompareTo(y.InstanceId));
            sb.Append("|consumed=").Append(consumed.Count);
            foreach (var c in consumed) sb.Append("|c:").Append(c.InstanceId.ToString(CultureInfo.InvariantCulture))
                .Append(',').Append(c.Tick.ToString(CultureInfo.InvariantCulture));

            var t = state.Totals;
            sb.Append("|totals");
            Nums(sb, t.PaidToPlayers, t.DebitedFromPlayers, t.NpcBusinessRevenue, t.GrocerRevenue);
            return sb.ToString();
        }

        /// <summary>Lowercase hex SHA-256 of Canonical(state).</summary>
        public static string Digest(EconomyState state)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(Canonical(state));
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(64);
                foreach (var b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        private static void Add(StringBuilder sb, string name, long value)
            => sb.Append('|').Append(name).Append('=').Append(value.ToString(CultureInfo.InvariantCulture));

        private static void Nums(StringBuilder sb, params long[] values)
        {
            foreach (var v in values) sb.Append(',').Append(v.ToString(CultureInfo.InvariantCulture));
        }
    }
}
