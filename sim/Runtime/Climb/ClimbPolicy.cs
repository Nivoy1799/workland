using System;
using System.Collections.Generic;
using System.Linq;

namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052: what a crew decides before a service (no draws): buy per its <see cref="SpendPolicy"/>, pass a gate whose
    /// conditions hold, set prices per its <see cref="PricePolicy"/>. While the balance is below 0 nothing is bought.
    /// Spec 052b: purchases may be bought again up to their MaxCount (the next copy costs <see cref="CostNow"/>); money is
    /// reference (4-player) money (D40), so costs here are the catalog's own; the stage 4 gate needs staff, machines and stars.
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

        /// <summary>Prices of the owned menu (active or not, so a dish put back has its price).</summary>
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

        /// <summary>Spec 052b: the cost of the next copy of <paramref name="purchase"/> (reference money).</summary>
        public static long CostNow(ClimbCatalog catalog, ClimbState state, PurchaseDef purchase) =>
            purchase.CostOf(state.Count(purchase.Id), catalog.MoneyScale);

        /// <summary>
        /// Productive purchases of stages up to the current one with a copy left, in (stage, next cost, id) order. A dish is no
        /// candidate when the menu is full.
        /// </summary>
        public static IReadOnlyList<PurchaseDef> Candidates(ClimbCatalog catalog, ClimbState state) =>
            Unowned(catalog, state, PurchaseKind.Productive);

        /// <summary>Status purchases of stages up to the current one with a copy left, in (stage, next cost, id) order.</summary>
        public static IReadOnlyList<PurchaseDef> StatusCandidates(ClimbCatalog catalog, ClimbState state) =>
            Unowned(catalog, state, PurchaseKind.Status);

        /// <summary>The gate purchase of the current stage when not owned yet, or null.</summary>
        public static PurchaseDef? GatePurchase(ClimbCatalog catalog, ClimbState state)
        {
            string? id = catalog.StageOf(state.Stage).GatePurchaseId;
            return id == null || state.Owns(id) ? null : catalog.Purchase(id);
        }

        /// <summary>
        /// True when the current stage has a next stage and every gate condition except the purchase holds: stars over the
        /// window, extra channels, and (spec 052b) owned staff and machine copies.
        /// </summary>
        public static bool GateConditionsMet(ClimbCatalog catalog, ClimbState state)
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
            if (st.GateMinStaff > 0 && catalog.StaffCount(state.Owned) < st.GateMinStaff) return false;
            if (st.GateMinMachines > 0 && catalog.MachineCount(state.Owned) < st.GateMinMachines) return false;
            return true;
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

        /// <summary>Spec 052b: the deposit of <paramref name="job"/> at the crew's current tier (reference money).</summary>
        public static long DepositNow(ClimbCatalog catalog, ClimbState state, BigJobDef job) =>
            job.DepositAt(state.TierOf(job.Id), catalog.MoneyScale);

        /// <summary>The spread of each competition score draw: <c>NextInt(-Spread, Spread + 1)</c>.</summary>
        public const int CompetitionSpread = 100;

        /// <summary>
        /// 052b review: the chance (permille, floored) that <c>crew + x &gt; rival + y</c> with x and y each uniform in
        /// -100..100, as the engine draws them. No draws: counted exactly over the 201 x 201 pairs.
        /// </summary>
        public static int WinPermille(int crewScore, int rivalScore)
        {
            const int n = 2 * CompetitionSpread + 1;
            long d = (long)rivalScore - crewScore, wins = 0;
            for (int x = -CompetitionSpread; x <= CompetitionSpread; x++)
                wins += Math.Max(0L, Math.Min(n, x - d + CompetitionSpread)); // y from -100 up to x - d - 1
            return (int)(wins * 1000 / ((long)n * n));
        }

        /// <summary>
        /// 052b review: the expected real net of <paramref name="job"/> for this crew now, without draws: a competition is
        /// win chance x prize; an event serves the guests the crew can serve (<see cref="ClimbEngine.ServableGuests"/>) at the
        /// active menu's average price times the tier's price factor, less the ingredients at the mean mistake rate. Both pay
        /// the upkeep and the deposit (all scaled for the crew, D40).
        /// </summary>
        public static long ExpectedJobNet(ClimbCatalog catalog, ClimbState state, CrewSetup setup, BigJobDef job)
        {
            long unit = catalog.MoneyScale;
            CrewFactors crew = CrewScaling.For(catalog, setup.Players);
            PlayerProfile profile = catalog.Profile(setup.ProfileId);
            int tier = state.TierOf(job.Id);
            long upkeep = CrewScaling.ScaleMoney(catalog.Upkeep(state.Stage, state.Owned), crew.DemandPermille, unit);
            long deposit = CrewScaling.ToReal(job.DepositAt(tier, unit), crew.GoalPermille, unit);
            if (job.Kind == BigJobKind.Competition)
            {
                int win = WinPermille(profile.SkillPermille + catalog.QualityBonus(state.Owned), job.RivalScorePermille);
                long prize = CrewScaling.ToReal(job.PrizeAt(tier, unit), crew.GoalPermille, unit);
                return prize * win / 1000 - deposit - upkeep;
            }
            IReadOnlyList<DishDef> menu = MenuFit.Active(catalog, state);
            ChannelDef? counter = catalog.CounterOf(state.Stage);
            if (menu.Count == 0 || counter == null) return -deposit - upkeep;
            int guests = Math.Min(CrewScaling.ScaleCount(job.Guests, crew.DemandPermille),
                ClimbEngine.ServableGuests(catalog, state, profile, setup.Players, setup.ServiceSeconds, crew.DemandPermille, counter));
            long price = 0, cost = 0;
            foreach (DishDef d in menu)
            {
                price += state.Prices.TryGetValue(d.Id, out long p) ? p : d.ReferencePrice;
                cost += d.IngredientCost;
            }
            int mistake = ClimbEngine.MistakePermille(ClimbEngine.Capacity(catalog, state, profile, setup.Players, setup.ServiceSeconds, crew.DemandPermille).SkillPermille);
            long revenue = guests * price * job.PricePermilleAt(tier) / (1000L * menu.Count * unit) * unit;
            long ingredients = guests * cost * (1000 + mistake) / 1000 * (1000 - catalog.CostSavedPermille(state.Owned)) / (1000L * menu.Count * unit) * unit;
            return revenue - ingredients - upkeep - deposit;
        }

        /// <summary>
        /// 052b review: the big job the crew takes instead of the next normal service, or null: the first ready job (id order)
        /// whose deposit the balance covers twice and whose expected net (<see cref="ExpectedJobNet"/>) is at least the last
        /// normal service's net.
        /// </summary>
        public static BigJobDef? ChooseJob(ClimbCatalog catalog, ClimbState state, CrewSetup setup)
        {
            foreach (BigJobDef j in ReadyJobs(catalog, state))
                if (state.Balance >= 2 * DepositNow(catalog, state, j) && ExpectedJobNet(catalog, state, setup, j) >= state.LastNet) return j;
            return null;
        }

        /// <summary>What money could be saved for (T7): the next copy of every candidate purchase of the stage (the gate included)
        /// and the current-tier deposits of the big jobs this crew can take (stage 5, requirement owned; a cooldown only delays
        /// them), ascending.</summary>
        public static IReadOnlyList<long> GoalCosts(ClimbCatalog catalog, ClimbState state)
        {
            var costs = new SortedSet<long>();
            foreach (PurchaseDef p in Candidates(catalog, state)) costs.Add(CostNow(catalog, state, p));
            foreach (PurchaseDef p in StatusCandidates(catalog, state)) costs.Add(CostNow(catalog, state, p));
            PurchaseDef? gate = GatePurchase(catalog, state);
            if (gate != null) costs.Add(gate.Cost);
            if (state.Stage == ClimbStage.BeyondKitchen)
                foreach (BigJobDef j in catalog.BigJobs)
                    if (j.RequiresPurchase == null || state.Owns(j.RequiresPurchase)) costs.Add(DepositNow(catalog, state, j));
            return costs.ToList().AsReadOnly();
        }

        /// <summary>
        /// The decisions before a service: buys (gate first when the policy allows it, then Productive items, then stage 5
        /// Status items), stage changes, prices. Returns the new state, what was bought (in order) and the money spent.
        /// The engine then lets the crew fit its menu (<see cref="MenuFit"/>).
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
                if (GateConditionsMet(catalog, s))
                {
                    PurchaseDef? gate = GatePurchase(catalog, s);
                    long cost = gate?.Cost ?? 0;
                    bool allowed = spend != SpendPolicy.Builder || Candidates(catalog, s).Count == 0;
                    if (allowed && s.Balance >= cost)
                    {
                        if (gate != null) { s = Buy(catalog, s, gate, cost); bought.Add(gate.Id); spent += cost; }
                        s = EnterNextStage(catalog, s);
                        continue;
                    }
                }
                // 2. One Productive item.
                PurchaseDef? next = null;
                long nextCost = 0;
                long gateCost = GatePurchase(catalog, s)?.Cost ?? 0;
                foreach (PurchaseDef p in Candidates(catalog, s))
                {
                    long c = CostNow(catalog, s, p);
                    bool ok = spend switch
                    {
                        SpendPolicy.Rush => s.Balance - c >= gateCost,
                        SpendPolicy.Builder => s.Balance >= c,
                        _ => s.Balance >= 2 * c,
                    };
                    if (ok) { next = p; nextCost = c; break; }
                }
                // 3. Status items: stage 5, nothing Productive left.
                if (next == null && s.Stage == ClimbStage.BeyondKitchen && Candidates(catalog, s).Count == 0)
                    foreach (PurchaseDef p in StatusCandidates(catalog, s))
                    {
                        long c = CostNow(catalog, s, p);
                        if (s.Balance >= 2 * c) { next = p; nextCost = c; break; }
                    }
                if (next == null) break;
                s = Buy(catalog, s, next, nextCost);
                bought.Add(next.Id);
                spent += nextCost;
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
                RecentMissed = Array.Empty<int>(), // 052b review: a new stage starts a new menu history
                Reputation = Math.Max(s.Reputation, catalog.StageOf(next).ReputationFloorPermille),
            };
        }

        /// <summary>Buys one copy: owned + id, balance - cost; a newly unlocked dish is active at once (spec 052b).</summary>
        private static ClimbState Buy(ClimbCatalog catalog, ClimbState s, PurchaseDef p, long cost)
        {
            var owned = new List<string>(s.Owned) { p.Id };
            IReadOnlyList<string> active = s.ActiveMenu;
            string? dish = p.Effect.UnlocksDish;
            if (dish != null && !active.Contains(dish) && catalog.Menu(s.Stage, owned).Any(d => d.Id == dish))
                active = new List<string>(active) { dish }.AsReadOnly();
            return s with { Owned = owned.AsReadOnly(), Balance = s.Balance - cost, ActiveMenu = active };
        }

        private static IReadOnlyList<PurchaseDef> Unowned(ClimbCatalog catalog, ClimbState state, PurchaseKind kind)
        {
            IReadOnlyList<DishDef>? menu = null;
            var list = new List<(PurchaseDef P, long Cost)>();
            foreach (PurchaseDef p in catalog.Purchases)
            {
                if (p.Kind != kind || p.Stage > state.Stage || !p.CanBuyMore(state.Count(p.Id))) continue;
                if (p.Effect.UnlocksDish != null)
                {
                    menu ??= catalog.Menu(state.Stage, state.Owned);
                    if (menu.Count >= catalog.StageOf(state.Stage).MaxDishes || menu.Any(d => d.Id == p.Effect.UnlocksDish)) continue;
                }
                list.Add((p, CostNow(catalog, state, p)));
            }
            list.Sort((a, b) =>
            {
                int c = a.P.Stage.CompareTo(b.P.Stage);
                if (c == 0) c = a.Cost.CompareTo(b.Cost);
                return c != 0 ? c : string.CompareOrdinal(a.P.Id, b.P.Id);
            });
            return list.Select(x => x.P).ToList().AsReadOnly();
        }
    }
}
