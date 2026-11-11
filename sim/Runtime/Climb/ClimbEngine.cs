using System;
using System.Collections.Generic;
using Worktown.Sim.Core;
using Worktown.Sim.Economy;

namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052: the service-level climb model. One <see cref="RunService"/> = policy, capacity, guests, serving, stars and
    /// reputation, money, rival reactions (or a stage 5 big job instead), then the clock moves by service + between seconds.
    /// One RNG per service (<see cref="EconomyRng.ForTick"/> of the setup seed and the service index), drawn in this order:
    /// guests (per channel, per guest: shop, then dish), serving (per served guest: mistake, then quality), big job.
    /// </summary>
    public sealed class ClimbEngine
    {
        /// <summary>Starting reputation (permille).</summary>
        public const int StartReputation = 500;
        /// <summary>Stars (tenths) kept for the gate window.</summary>
        public const int StarsKept = 32;

        public ClimbEngine(ClimbCatalog catalog)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public ClimbCatalog Catalog { get; }

        /// <summary>The state before the first service: stage 1, the starting money, nothing owned.</summary>
        public ClimbState Start(CrewSetup setup)
        {
            Check(setup);
            var entered = new long[] { -1, 0, -1, -1, -1, -1 };
            var rivals = new List<RivalState>();
            foreach (RivalDef r in Catalog.Rivals) rivals.Add(RivalState.Start(r.Id));
            int rep = Math.Max(StartReputation, Catalog.StageOf(ClimbStage.FoodCorner).ReputationFloorPermille);
            return new ClimbState(0, 0, ClimbStage.FoodCorner, Catalog.StartingMoney, rep,
                Catalog.Dish(Catalog.Stage1DishId).BaseQualityPermille, 80, 0, 0, Array.Empty<string>(),
                new Dictionary<string, long>(StringComparer.Ordinal), rivals.AsReadOnly(), Array.AsReadOnly(entered),
                Array.Empty<int>(), new Dictionary<string, int>(StringComparer.Ordinal), 0);
        }

        /// <summary>Services from <see cref="Start"/> while the play time is below <paramref name="horizonSeconds"/>.</summary>
        public ClimbRun Run(CrewSetup setup, long horizonSeconds)
        {
            ClimbState state = Start(setup);
            var results = new List<ServiceResult>();
            while (state.PlaySeconds < horizonSeconds)
            {
                var step = RunService(state, setup);
                state = step.State;
                results.Add(step.Result);
            }
            return new ClimbRun(setup, horizonSeconds, results, state, Catalog.MoneyScale);
        }

        /// <summary>One service (or a stage 5 big job instead of it).</summary>
        public (ClimbState State, ServiceResult Result) RunService(ClimbState state, CrewSetup setup)
        {
            Check(setup);
            PlayerProfile profile = Catalog.Profile(setup.ProfileId);
            (ClimbState s, IReadOnlyList<string> bought, long spent) = ClimbPolicy.Decide(Catalog, state, setup);
            IRandom rng = EconomyRng.ForTick(setup.Seed, s.ServiceIndex);
            int seconds = setup.ServiceSeconds + setup.BetweenSeconds;
            long upkeep = Catalog.Upkeep(s.Stage, s.Owned);
            IReadOnlyList<DishDef> menu = Catalog.Menu(s.Stage, s.Owned);

            BigJobDef? job = null;
            foreach (BigJobDef j in ClimbPolicy.ReadyJobs(Catalog, s))
                if (s.Balance >= 2 * j.Deposit) { job = j; break; }
            if (job != null) return RunBigJob(s, setup, profile, rng, job, menu, bought, spent, upkeep, seconds);

            // 3. Guests per channel.
            IReadOnlyList<ChannelDef> channels = Catalog.ActiveChannels(s.Stage, s.Owned);
            ChoiceWeights w = Catalog.Weights;
            long sumRef = 0, sumOffset = 0;
            foreach (DishDef d in menu) { sumRef += d.ReferencePrice; sumOffset += s.Prices[d.Id] - d.ReferencePrice; }
            // Spec 037: averaged in whole units (MoneyScale), so the model is the same at any money scale (cents, D39).
            long unit = Catalog.MoneyScale;
            long avgRef = sumRef / (menu.Count * unit) * unit;
            long offset = GuestChoice.FloorDiv(sumOffset, menu.Count * unit) * unit;
            int rep = Math.Min(1000, s.Reputation + Catalog.ReputationBonus(s.Owned));
            int you = GuestChoice.Attraction(w, s.LastQuality, rep, offset, avgRef, s.LastWait, menu.Count);
            var guests = new List<(ChannelDef Channel, DishDef Dish)>();
            var channelIds = new string[channels.Count];
            var yours = new int[channels.Count];
            var rivalTotals = new int[channels.Count];
            var perRival = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < channels.Count; i++)
            {
                ChannelDef ch = channels[i];
                channelIds[i] = ch.Id;
                IReadOnlyList<RivalDef> rivals = Catalog.RivalsOn(ch.Id);
                var attractions = new List<int> { you };
                foreach (RivalDef r in rivals)
                {
                    RivalState rs = s.RivalOf(r.Id);
                    attractions.Add(GuestChoice.Attraction(w, rs.QualityOf(r), r.ReputationPermille, rs.OffsetOf(r, Catalog.MoneyScale),
                        avgRef, r.WaitSeconds, 1));
                    perRival[r.Id] = 0;
                }
                attractions.Add(w.Outside);
                rivalTotals[i] = rivals.Count == 0 ? -1 : 0;
                int ceiling = GuestChoice.Ceiling(ch, Catalog.CeilingBonusPermille(s.Owned, ch.Id));
                for (int g = 0; g < ceiling; g++)
                {
                    int k = GuestChoice.Pick(attractions, rng);
                    if (k == 0)
                    {
                        guests.Add((ch, menu[rng.NextInt(0, menu.Count)]));
                        yours[i]++;
                    }
                    else if (k <= rivals.Count)
                    {
                        rivalTotals[i]++;
                        perRival[rivals[k - 1].Id]++;
                    }
                }
            }

            // 4. Serve.
            var orders = new List<(int Actions, DishDef Dish, int PricePermille)>(guests.Count);
            foreach ((ChannelDef ch, DishDef dish) in guests) orders.Add((GuestActions(Catalog, s, ch, dish), dish, ch.PricePermille));
            Serving sv = Serve(s, setup, profile, orders, rng);

            // 5. Wait, stars, reputation.
            int chosen = guests.Count;
            int wait = WaitSeconds(chosen, sv.Served);
            int starsTenths = StarsTenths(sv.Qualities, sv.Missed, wait);
            int newRep = s.Reputation;
            int den = 2 * sv.Served + sv.Missed;
            if (den > 0) newRep = NextReputation(s.Reputation, starsTenths, Catalog.StageOf(s.Stage).ReputationFloorPermille);
            int avgQuality = s.LastQuality;
            if (sv.Served > 0)
            {
                long sumQ = 0;
                foreach (int q in sv.Qualities) sumQ += q;
                avgQuality = (int)(sumQ / sv.Served);
            }

            // 7. Rivals react.
            var nextRivals = new List<RivalState>(s.Rivals.Count);
            foreach (RivalState rs in s.Rivals)
            {
                RivalDef def = Catalog.Rival(rs.RivalId);
                int idx = Array.IndexOf(channelIds, def.ChannelId);
                nextRivals.Add(idx < 0 ? rs : rs.React(def, yours[idx], perRival[def.Id]));
            }

            // 6. Money.
            long net = sv.Revenue - sv.IngredientCost - upkeep;
            var stars = new List<int>(s.RecentStars);
            if (den > 0) stars.Add(starsTenths);
            if (stars.Count > StarsKept) stars.RemoveRange(0, stars.Count - StarsKept);
            ClimbState end = s with
            {
                ServiceIndex = s.ServiceIndex + 1,
                PlaySeconds = s.PlaySeconds + seconds,
                Balance = s.Balance + net,
                Reputation = newRep,
                LastQuality = avgQuality,
                LastWait = wait,
                LastChosen = chosen,
                LastDemandActions = sv.DemandActions,
                Rivals = nextRivals.AsReadOnly(),
                RecentStars = stars.AsReadOnly(),
            };
            var result = new ServiceResult(s.ServiceIndex, s.Stage, null, s.PlaySeconds, seconds, chosen, sv.Served, sv.Missed,
                sv.Mistakes, sv.Revenue, sv.IngredientCost, upkeep, 0, spent, end.Balance, avgQuality, wait, starsTenths, newRep,
                Array.AsReadOnly(channelIds), Array.AsReadOnly(yours), Array.AsReadOnly(rivalTotals), bought,
                ClimbPolicy.GoalCosts(Catalog, end));
            return (end, result);
        }

        // ---------- pure helpers ----------

        /// <summary>Work spots: the stage's plus those of every open channel.</summary>
        public static int WorkSpots(ClimbCatalog catalog, ClimbState state)
        {
            int w = catalog.StageOf(state.Stage).WorkSpots;
            foreach (ChannelDef c in catalog.ActiveChannels(state.Stage, state.Owned)) w += c.WorkSpots;
            return w;
        }

        /// <summary>
        /// Actions of a service and the action-weighted skill: <c>APM * minutes</c> for each of the first <c>min(N, W)</c>
        /// players, <c>APM * minutes * OverflowPermille / 1000</c> for each beyond the work spots, plus every owned staff.
        /// </summary>
        public static (int Actions, int SkillPermille) Capacity(ClimbCatalog catalog, ClimbState state, PlayerProfile profile,
            int players, int serviceSeconds)
        {
            int minutes = serviceSeconds / 60;
            int spots = WorkSpots(catalog, state);
            int full = Math.Min(players, spots);
            int over = Math.Max(0, players - spots);
            long per = (long)profile.ActionsPerMinute * minutes;
            long overflowEach = per * catalog.StageOf(state.Stage).OverflowPermille / 1000;
            long actions = per * full + overflowEach * over;
            long weighted = actions * profile.SkillPermille;
            foreach (PurchaseDef staff in catalog.Staff(state.Owned))
            {
                long a = (long)staff.Effect.StaffActionsPerMinute * minutes;
                actions += a;
                weighted += a * staff.Effect.StaffSkillPermille;
            }
            int skill = actions > 0 ? (int)(weighted / actions) : profile.SkillPermille;
            return ((int)Math.Min(int.MaxValue, actions), skill);
        }

        /// <summary>Actions one guest costs: <c>max(1, dish actions * channel factor / 1000 - saved) + payment + cleaning - saved</c>.</summary>
        public static int GuestActions(ClimbCatalog catalog, ClimbState state, ChannelDef channel, DishDef dish)
        {
            StageDef st = catalog.StageOf(state.Stage);
            int make = Math.Max(1, (int)((long)dish.Actions * channel.ActionsPermille / 1000) - catalog.ActionsSaved(state.Owned, dish.Id));
            int cleaning = Math.Max(0, st.CleaningActions - catalog.ActionsSaved(state.Owned, ClimbCatalog.CleaningTarget));
            return make + st.PaymentActions + cleaning;
        }

        /// <summary>Chance of a mistake (permille): <c>(1000 - skill) / 8</c>.</summary>
        public static int MistakePermille(int skillPermille) => Math.Max(0, 1000 - skillPermille) / 8;

        /// <summary>Quality of one dish: <c>clamp(base + (skill - 500) * 600 / 1000 + bonus + jitter, 0, 1000)</c>.</summary>
        public static int DishQuality(int basePermille, int skillPermille, int bonus, int jitter)
        {
            long q = basePermille + GuestChoice.FloorDiv((long)(skillPermille - 500) * 600, 1000) + bonus + jitter;
            return (int)Math.Max(0, Math.Min(1000, q));
        }

        /// <summary>Stars of one dish: <c>1 + min(4, q / 200)</c>, one less (at least 1) when the wait is above 120 s.</summary>
        public static int Stars(int quality, int waitSeconds)
        {
            int stars = 1 + Math.Min(4, Math.Max(0, quality) / 200);
            return waitSeconds > 120 ? Math.Max(1, stars - 1) : stars;
        }

        /// <summary>Wait of a service: <c>40 + 40 * chosen / max(1, served)</c> seconds.</summary>
        public static int WaitSeconds(int chosen, int served) => 40 + (int)(40L * chosen / Math.Max(1, served));

        /// <summary>Average stars (tenths) of served dishes, each missed guest 1 star at half weight; 0 without guests.</summary>
        public static int StarsTenths(IReadOnlyList<int> qualities, int missed, int waitSeconds)
        {
            long sum = 0;
            foreach (int q in qualities) sum += Stars(q, waitSeconds);
            long den = 2L * qualities.Count + missed;
            return den == 0 ? 0 : (int)((2 * sum + missed) * 10 / den);
        }

        /// <summary><c>rep + (stars permille - rep) * 300 / 1000</c>, clamped to [floor, 1000].</summary>
        public static int NextReputation(int reputation, int starsTenths, int floorPermille)
        {
            long starsPermille = starsTenths * 20L;
            long rep = reputation + GuestChoice.FloorDiv((starsPermille - reputation) * 300, 1000);
            return (int)Math.Max(floorPermille, Math.Min(1000, rep));
        }

        // ---------- internals ----------

        private sealed class Serving
        {
            public int Served, Missed, Mistakes, DemandActions;
            public long Revenue, IngredientCost;
            public readonly List<int> Qualities = new List<int>();
        }

        private Serving Serve(ClimbState s, CrewSetup setup, PlayerProfile profile,
            IReadOnlyList<(int Actions, DishDef Dish, int PricePermille)> orders, IRandom rng)
        {
            (int actions, int skill) = Capacity(Catalog, s, profile, setup.Players, setup.ServiceSeconds);
            int mistakeP = MistakePermille(skill);
            int bonus = Catalog.QualityBonus(s.Owned);
            long remaining = actions, ingredientRaw = 0, revenueRaw = 0;
            var sv = new Serving();
            foreach ((int cost, DishDef dish, int pricePermille) in orders)
            {
                sv.DemandActions += cost;
                if (remaining < cost) { sv.Missed++; continue; }
                bool mistake = rng.NextInt(0, 1000) < mistakeP;
                remaining -= cost;
                ingredientRaw += dish.IngredientCost;
                if (mistake)
                {
                    sv.Mistakes++;
                    if (remaining < cost) { sv.Missed++; continue; }
                    remaining -= cost;
                    ingredientRaw += dish.IngredientCost;
                }
                sv.Qualities.Add(DishQuality(dish.BaseQualityPermille, skill, bonus, rng.NextInt(-40, 41)));
                sv.Served++;
                revenueRaw += s.Prices[dish.Id] * pricePermille;
            }
            // Spec 037: rounded down to whole units (MoneyScale) as in the whole-dollar model, so cents give the same climb.
            long unit = Catalog.MoneyScale;
            sv.Revenue = revenueRaw / (1000 * unit) * unit;
            sv.IngredientCost = ingredientRaw * (1000 - Catalog.CostSavedPermille(s.Owned)) / (1000 * unit) * unit;
            return sv;
        }

        private (ClimbState, ServiceResult) RunBigJob(ClimbState s, CrewSetup setup, PlayerProfile profile, IRandom rng, BigJobDef job,
            IReadOnlyList<DishDef> menu, IReadOnlyList<string> bought, long spent, long upkeep, int seconds)
        {
            long revenue = 0, ingredients = 0;
            int chosen = 0, served = 0, missed = 0, mistakes = 0, avgQuality = s.LastQuality, trophies = s.Trophies;
            if (job.Kind == BigJobKind.Competition)
            {
                int crew = profile.SkillPermille + Catalog.QualityBonus(s.Owned) + rng.NextInt(-100, 101);
                int rival = job.RivalScorePermille + rng.NextInt(-100, 101);
                if (crew > rival) { revenue = job.Prize; trophies++; }
            }
            else
            {
                ChannelDef counter = Catalog.CounterOf(s.Stage)!;
                var orders = new List<(int, DishDef, int)>(job.Guests);
                for (int g = 0; g < job.Guests; g++)
                {
                    DishDef dish = menu[rng.NextInt(0, menu.Count)];
                    orders.Add((GuestActions(Catalog, s, counter, dish), dish, job.PricePermille));
                }
                Serving sv = Serve(s, setup, profile, orders, rng);
                chosen = job.Guests; served = sv.Served; missed = sv.Missed; mistakes = sv.Mistakes;
                revenue = sv.Revenue; ingredients = sv.IngredientCost;
                if (served > 0)
                {
                    long sum = 0;
                    foreach (int q in sv.Qualities) sum += q;
                    avgQuality = (int)(sum / served);
                }
            }
            var readyAt = new Dictionary<string, int>(s.JobReadyAt, StringComparer.Ordinal) { [job.Id] = s.ServiceIndex + 1 + job.CooldownServices };
            long net = revenue - ingredients - upkeep - job.Deposit;
            ClimbState end = s with
            {
                ServiceIndex = s.ServiceIndex + 1,
                PlaySeconds = s.PlaySeconds + seconds,
                Balance = s.Balance + net,
                JobReadyAt = readyAt,
                Trophies = trophies,
            };
            var result = new ServiceResult(s.ServiceIndex, s.Stage, job.Id, s.PlaySeconds, seconds, chosen, served, missed, mistakes,
                revenue, ingredients, upkeep, job.Deposit, spent, end.Balance, avgQuality, 0, 0, s.Reputation,
                Array.Empty<string>(), Array.Empty<int>(), Array.Empty<int>(), bought, ClimbPolicy.GoalCosts(Catalog, end));
            return (end, result);
        }

        private void Check(CrewSetup setup)
        {
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            if (setup.Players < 1) throw new ArgumentException("Players must be at least 1.", nameof(setup));
            if (setup.ServiceSeconds < 60) throw new ArgumentException("ServiceSeconds must be at least 60.", nameof(setup));
            if (setup.BetweenSeconds < 0) throw new ArgumentException("BetweenSeconds must be >= 0.", nameof(setup));
            Catalog.Profile(setup.ProfileId);
        }
    }
}
