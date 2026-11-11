using System;
using System.Collections.Generic;
using System.Linq;

namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052: what a crew decides before a service (no draws): buy per its <see cref="SpendPolicy"/>, pass a gate whose
    /// conditions hold, set prices per its <see cref="PricePolicy"/>. While the balance is below 0 nothing is bought.
    /// </summary>
    public static class ClimbPolicy
    {
        /// <summary>The price of <paramref name="dish"/>: reference + {-1, 0, +1, +2} units, or Undercut (the counter rival's
        /// price - 1 unit; Match without a rival); never below its ingredient cost + 1 unit.</summary>
        public static long PriceOf(ClimbCatalog catalog, ClimbState state, PricePolicy policy, DishDef dish)
        {
            long unit = catalog.MoneyScale;
            long price;
            switch (policy)
            {
                case PricePolicy.Minus1: price = dish.ReferencePrice - unit; break;
                case PricePolicy.Plus1: price = dish.ReferencePrice + unit; break;
                case PricePolicy.Plus2: price = dish.ReferencePrice + 2 * unit; break;
                case PricePolicy.Undercut:
                    RivalDef? rival = CounterRival(catalog, state.Stage);
                    price = rival == null ? dish.ReferencePrice
                        : dish.ReferencePrice + state.RivalOf(rival.Id).OffsetOf(rival, unit) - unit;
                    break;
                default: price = dish.ReferencePrice; break;
            }
            return Math.Max(dish.IngredientCost + unit, price);
        }

        /// <summary>Prices of the current menu.</summary>
        public static IReadOnlyDictionary<string, long> Prices(ClimbCatalog catalog, ClimbState state, PricePolicy policy)
        {
            var prices = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (DishDef d in catalog.Menu(state.Stage, state.Owned)) prices[d.Id] = PriceOf(catalog, state, policy, d);
            return prices;
        }

        /// <summary>The first rival (id order) on the counter channel of <paramref name="stage"/>, or null.</summary>
        public static RivalDef? CounterRival(ClimbCatalog catalog, ClimbStage stage)
        {
            ChannelDef? counter = catalog.CounterOf(stage);
            if (counter == null) return null;
            IReadOnlyList<RivalDef> rivals = catalog.RivalsOn(counter.Id);
            return rivals.Count > 0 ? rivals[0] : null;
        }

        /// <summary>
        /// Unowned Productive purchases of stages up to the current one, in (stage, cost, id) order. A dish is no candidate when
        /// the menu is full.
        /// </summary>
        public static IReadOnlyList<PurchaseDef> Candidates(ClimbCatalog catalog, ClimbState state) =>
            Unowned(catalog, state, PurchaseKind.Productive);

        /// <summary>Unowned Status purchases of stages up to the current one, in (stage, cost, id) order.</summary>
        public static IReadOnlyList<PurchaseDef> StatusCandidates(ClimbCatalog catalog, ClimbState state) =>
            Unowned(catalog, state, PurchaseKind.Status);

        /// <summary>The gate purchase of the current stage when not owned yet, or null.</summary>
        public static PurchaseDef? GatePurchase(ClimbCatalog catalog, ClimbState state)
        {
            string? id = catalog.StageOf(state.Stage).GatePurchaseId;
            return id == null || state.Owns(id) ? null : catalog.Purchase(id);
        }

        /// <summary>True when the current stage has a next stage and every gate condition except the purchase holds.</summary>
        public static bool GateConditionsMet(ClimbCatalog catalog, ClimbState state, CrewSetup setup)
        {
            if (state.Stage >= ClimbStage.BeyondKitchen) return false;
            StageDef st = catalog.StageOf(state.Stage);
            if (st.GateStarsTenths > 0)
            {
                int window = Math.Max(1, st.GateWindow);
                if (state.RecentStars.Count < window) return false;
                long sum = 0;
                for (int i = state.RecentStars.Count - window; i < state.RecentStars.Count; i++) sum += state.RecentStars[i];
                if (sum < (long)st.GateStarsTenths * window) return false;
            }
            if (st.GateMinChannels > 0 && catalog.ExtraChannels(state.Stage, state.Owned) < st.GateMinChannels) return false;
            if (st.GateFewPlayers && !FewPlayersOk(catalog, state, setup)) return false;
            return true;
        }

        /// <summary>
        /// The few-players check (no draws): <c>min(N, 2)</c> players plus staff and machines, mistakes at their mean rate, could
        /// serve at least 900 permille of the guests who chose you last service.
        /// </summary>
        public static bool FewPlayersOk(ClimbCatalog catalog, ClimbState state, CrewSetup setup)
        {
            PlayerProfile profile = catalog.Profile(setup.ProfileId);
            (int actions, int skill) = ClimbEngine.Capacity(catalog, state, profile, Math.Min(setup.Players, 2), setup.ServiceSeconds);
            long mistake = ClimbEngine.MistakePermille(skill);
            return (long)actions * 1000 * 1000 >= 900L * state.LastDemandActions * (1000 + mistake);
        }

        /// <summary>Big jobs that may run now: stage 5, off cooldown, requirement owned (id order; the balance is not checked).</summary>
        public static IReadOnlyList<BigJobDef> ReadyJobs(ClimbCatalog catalog, ClimbState state)
        {
            var list = new List<BigJobDef>();
            if (state.Stage < ClimbStage.BeyondKitchen) return list;
            foreach (BigJobDef job in catalog.BigJobs)
            {
                if (state.JobReadyAt.TryGetValue(job.Id, out int at) && at > state.ServiceIndex) continue;
                if (job.RequiresPurchase != null && !state.Owns(job.RequiresPurchase)) continue;
                list.Add(job);
            }
            return list;
        }

        /// <summary>What money could be saved for (T7): costs of unowned purchases of the stage (the gate included) and
        /// deposits of the big jobs this crew can take (stage 5, requirement owned; a cooldown only delays them), ascending.</summary>
        public static IReadOnlyList<long> GoalCosts(ClimbCatalog catalog, ClimbState state)
        {
            var costs = new SortedSet<long>();
            foreach (PurchaseDef p in Candidates(catalog, state)) costs.Add(p.Cost);
            foreach (PurchaseDef p in StatusCandidates(catalog, state)) costs.Add(p.Cost);
            PurchaseDef? gate = GatePurchase(catalog, state);
            if (gate != null) costs.Add(gate.Cost);
            if (state.Stage == ClimbStage.BeyondKitchen)
                foreach (BigJobDef j in catalog.BigJobs)
                    if (j.RequiresPurchase == null || state.Owns(j.RequiresPurchase)) costs.Add(j.Deposit);
            return costs.ToList().AsReadOnly();
        }

        /// <summary>
        /// The decisions before a service: buys (gate first when the policy allows it, then Productive items, then stage 5
        /// Status items), stage changes, prices. Returns the new state, what was bought (in order) and the money spent.
        /// </summary>
        public static (ClimbState State, IReadOnlyList<string> Bought, long Spent) Decide(ClimbCatalog catalog, ClimbState state, CrewSetup setup)
        {
            var bought = new List<string>();
            long spent = 0;
            ClimbState s = state;
            SpendPolicy spend = setup.Strategy.Spend;
            for (int guard = 0; guard < 256 && s.Balance >= 0; guard++)
            {
                // 1. The gate.
                if (GateConditionsMet(catalog, s, setup))
                {
                    PurchaseDef? gate = GatePurchase(catalog, s);
                    long cost = gate?.Cost ?? 0;
                    bool allowed = spend != SpendPolicy.Builder || Candidates(catalog, s).Count == 0;
                    if (allowed && s.Balance >= cost)
                    {
                        if (gate != null) { s = Buy(s, gate); bought.Add(gate.Id); spent += cost; }
                        s = EnterNextStage(catalog, s);
                        continue;
                    }
                }
                // 2. One Productive item.
                PurchaseDef? next = null;
                long gateCost = GatePurchase(catalog, s)?.Cost ?? 0;
                foreach (PurchaseDef p in Candidates(catalog, s))
                {
                    bool ok = spend switch
                    {
                        SpendPolicy.Rush => s.Balance - p.Cost >= gateCost,
                        SpendPolicy.Builder => s.Balance >= p.Cost,
                        _ => s.Balance >= 2 * p.Cost,
                    };
                    if (ok) { next = p; break; }
                }
                // 3. Status items: stage 5, nothing Productive left.
                if (next == null && s.Stage == ClimbStage.BeyondKitchen && Candidates(catalog, s).Count == 0)
                    foreach (PurchaseDef p in StatusCandidates(catalog, s))
                        if (s.Balance >= 2 * p.Cost) { next = p; break; }
                if (next == null) break;
                s = Buy(s, next);
                bought.Add(next.Id);
                spent += next.Cost;
            }
            s = s with { Prices = Prices(catalog, s, setup.Strategy.Price) };
            return (s, bought.AsReadOnly(), spent);
        }

        /// <summary>Enter the next stage: record the time, clear the stars window, lift reputation to the new floor.</summary>
        public static ClimbState EnterNextStage(ClimbCatalog catalog, ClimbState s)
        {
            if (s.Stage >= ClimbStage.BeyondKitchen) return s;
            ClimbStage next = s.Stage + 1;
            var entered = s.StageEnteredSeconds.ToArray();
            entered[(int)next] = s.PlaySeconds;
            return s with
            {
                Stage = next,
                StageEnteredSeconds = Array.AsReadOnly(entered),
                RecentStars = Array.Empty<int>(),
                Reputation = Math.Max(s.Reputation, catalog.StageOf(next).ReputationFloorPermille),
            };
        }

        private static ClimbState Buy(ClimbState s, PurchaseDef p)
        {
            var owned = new List<string>(s.Owned) { p.Id };
            return s with { Owned = owned.AsReadOnly(), Balance = s.Balance - p.Cost };
        }

        private static IReadOnlyList<PurchaseDef> Unowned(ClimbCatalog catalog, ClimbState state, PurchaseKind kind)
        {
            IReadOnlyList<DishDef>? menu = null;
            var list = new List<PurchaseDef>();
            foreach (PurchaseDef p in catalog.Purchases)
            {
                if (p.Kind != kind || p.Stage > state.Stage || state.Owns(p.Id)) continue;
                if (p.Effect.UnlocksDish != null)
                {
                    menu ??= catalog.Menu(state.Stage, state.Owned);
                    if (menu.Count >= catalog.StageOf(state.Stage).MaxDishes || menu.Any(d => d.Id == p.Effect.UnlocksDish)) continue;
                }
                list.Add(p);
            }
            list.Sort((a, b) =>
            {
                int c = a.Stage.CompareTo(b.Stage);
                if (c == 0) c = a.Cost.CompareTo(b.Cost);
                return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
            });
            return list;
        }
    }
}
