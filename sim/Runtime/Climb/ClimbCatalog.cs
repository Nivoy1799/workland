using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Worktown.Sim.Cooking;

namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052: the data of the climb model. The constructor copies its inputs, multiplies every money value by
    /// <see cref="MoneyScale"/> (data is written in whole dollars) and validates (throws <see cref="ArgumentException"/>).
    /// Spec 037 (D39 revises D23): money is integer cents, so <see cref="CreateDefault"/> uses <see cref="CentsPerDollar"/>; the
    /// engine rounds to whole units, so the climb is the same at any scale (the climb report reads the same).
    /// Spec 052b: every money value must be a whole multiple of MoneyScale (037 review), and the catalog holds the D40 crew table
    /// (<see cref="CrewTable"/>, read through <see cref="CrewScaling.For"/>).
    /// All numbers of <see cref="CreateDefault"/> are provisional; tuning changes only those.
    /// </summary>
    public sealed class ClimbCatalog
    {
        /// <summary>The <see cref="PurchaseEffect.SavedOn"/> value that saves cleaning actions.</summary>
        public const string CleaningTarget = "cleaning";
        public const int MaxPermille = 5000;
        /// <summary>Spec 037 (D39): money is integer cents; the default data (whole dollars) is multiplied by this.</summary>
        public const long CentsPerDollar = 100;

        private static readonly Regex IdPattern = new Regex("^[a-z0-9_]{1,32}$", RegexOptions.CultureInvariant);

        private readonly Dictionary<string, PlayerProfile> _profiles = new Dictionary<string, PlayerProfile>(StringComparer.Ordinal);
        private readonly Dictionary<string, DishDef> _dishes = new Dictionary<string, DishDef>(StringComparer.Ordinal);
        private readonly Dictionary<string, ChannelDef> _channels = new Dictionary<string, ChannelDef>(StringComparer.Ordinal);
        private readonly Dictionary<string, RivalDef> _rivals = new Dictionary<string, RivalDef>(StringComparer.Ordinal);
        private readonly Dictionary<string, PurchaseDef> _purchases = new Dictionary<string, PurchaseDef>(StringComparer.Ordinal);
        private readonly Dictionary<string, BigJobDef> _bigJobs = new Dictionary<string, BigJobDef>(StringComparer.Ordinal);
        private readonly StageDef[] _stages = new StageDef[6];
        private readonly HashSet<string> _unlockedChannels = new HashSet<string>(StringComparer.Ordinal);

        public ClimbCatalog(IEnumerable<PlayerProfile> profiles, IEnumerable<DishDef> dishes, IEnumerable<ChannelDef> channels,
            IEnumerable<RivalDef> rivals, IEnumerable<PurchaseDef> purchases, IEnumerable<StageDef> stages,
            IEnumerable<BigJobDef> bigJobs, ChoiceWeights weights, string stage1DishId, long startingMoney, long moneyScale = 1,
            IEnumerable<CrewScaleRow>? crewTable = null)
        {
            if (moneyScale < 1) throw new ArgumentException("MoneyScale must be at least 1.", nameof(moneyScale));
            MoneyScale = moneyScale;
            long s = moneyScale;
            Profiles = Copy(profiles, nameof(profiles));
            Dishes = Copy(dishes, nameof(dishes)).Select(d => d with { IngredientCost = d.IngredientCost * s, ReferencePrice = d.ReferencePrice * s }).ToList().AsReadOnly();
            Channels = Copy(channels, nameof(channels)).Select(c => c with { Upkeep = c.Upkeep * s }).OrderBy(c => c.Id, StringComparer.Ordinal).ToList().AsReadOnly();
            Rivals = Copy(rivals, nameof(rivals)).Select(r => r with { PriceOffset = (int)(r.PriceOffset * s) }).OrderBy(r => r.Id, StringComparer.Ordinal).ToList().AsReadOnly();
            Purchases = Copy(purchases, nameof(purchases)).Select(p => p with { Cost = p.Cost * s, UpkeepPerService = p.UpkeepPerService * s }).ToList().AsReadOnly();
            Stages = Copy(stages, nameof(stages)).Select(st => st with { Upkeep = st.Upkeep * s }).OrderBy(st => (int)st.Stage).ToList().AsReadOnly();
            BigJobs = Copy(bigJobs, nameof(bigJobs)).Select(j => j with { Deposit = j.Deposit * s, Prize = j.Prize * s }).OrderBy(j => j.Id, StringComparer.Ordinal).ToList().AsReadOnly();
            Weights = weights ?? throw new ArgumentException("Weights are required.", nameof(weights));
            Stage1DishId = stage1DishId;
            StartingMoney = startingMoney * s;
            CrewTable = Copy(crewTable ?? NeutralCrewTable, nameof(crewTable));
            Validate();
        }

        /// <summary>
        /// Spec 052b: one row, no crew scaling at all (demand and goal 1000, the stage's own stock): the default of the
        /// constructor (unit-test catalogs) and of <c>--crew-scaling off</c>.
        /// </summary>
        public static IReadOnlyList<CrewScaleRow> NeutralCrewTable { get; } =
            Array.AsReadOnly(new[] { new CrewScaleRow(int.MaxValue, 1000, 0, 1000, 1000, 0) });

        /// <summary>
        /// Spec 052b, D40: the default crew table, h(n) per crew size (data; tuning changes only these numbers): solo is easy,
        /// 2 and 3 are harder, 4 is the reference (1000), above that a plateau.
        /// </summary>
        public static IReadOnlyList<CrewScaleRow> DefaultCrewTable { get; } = Array.AsReadOnly(new[]
        {
            new CrewScaleRow(1, 550),
            new CrewScaleRow(2, 750),
            new CrewScaleRow(3, 900),
            new CrewScaleRow(4, 1000),
            new CrewScaleRow(5, 1000),
            new CrewScaleRow(16, 1000),
        });

        /// <summary>
        /// Spec 052b C1: <paramref name="rows"/> as the game measured stage 1 (spec 053): no crew scaling of demand or goal and a
        /// fixed stock of <paramref name="stock"/> per service (the potato slot); difficulty and patience stay.
        /// </summary>
        public static IReadOnlyList<CrewScaleRow> CalibrationCrewTable(IReadOnlyList<CrewScaleRow> rows, int stock = 12) =>
            rows.Select(r => r with { FixedDemandPermille = 1000, FixedGoalPermille = 1000, FixedStock = stock }).ToList().AsReadOnly();

        /// <summary>Spec 052b: the crew table, sorted by MaxPlayers (validated). Read through <see cref="CrewScaling.For"/>.</summary>
        public IReadOnlyList<CrewScaleRow> CrewTable { get; }

        /// <summary>Spec 052b: the first crew row with <paramref name="players"/> &lt;= MaxPlayers (the last row above it).</summary>
        public CrewScaleRow RowFor(int players)
        {
            foreach (CrewScaleRow r in CrewTable)
                if (players <= r.MaxPlayers) return r;
            return CrewTable[CrewTable.Count - 1];
        }

        public long MoneyScale { get; }
        public IReadOnlyList<PlayerProfile> Profiles { get; }
        public IReadOnlyList<DishDef> Dishes { get; }
        /// <summary>Channels in id order.</summary>
        public IReadOnlyList<ChannelDef> Channels { get; }
        /// <summary>Rivals in id order.</summary>
        public IReadOnlyList<RivalDef> Rivals { get; }
        public IReadOnlyList<PurchaseDef> Purchases { get; }
        /// <summary>Stages 1..5 in order.</summary>
        public IReadOnlyList<StageDef> Stages { get; }
        /// <summary>Big jobs in id order.</summary>
        public IReadOnlyList<BigJobDef> BigJobs { get; }
        public ChoiceWeights Weights { get; }
        public string Stage1DishId { get; }
        public long StartingMoney { get; }

        public PlayerProfile Profile(string id) => Get(_profiles, id, "profile");
        public DishDef Dish(string id) => Get(_dishes, id, "dish");
        public ChannelDef Channel(string id) => Get(_channels, id, "channel");
        public RivalDef Rival(string id) => Get(_rivals, id, "rival");
        public PurchaseDef Purchase(string id) => Get(_purchases, id, "purchase");
        public BigJobDef BigJob(string id) => Get(_bigJobs, id, "big job");
        public StageDef StageOf(ClimbStage stage) => _stages[(int)stage];

        /// <summary>True for a channel no purchase unlocks (a counter that comes with the stage).</summary>
        public bool IsBaseChannel(string channelId) => _channels.ContainsKey(channelId) && !_unlockedChannels.Contains(channelId);

        /// <summary>
        /// The open channels in id order: the base channel with the highest stage up to <paramref name="stage"/>, plus every
        /// channel an owned purchase unlocks (from its own stage on).
        /// </summary>
        public IReadOnlyList<ChannelDef> ActiveChannels(ClimbStage stage, IReadOnlyList<string> owned)
        {
            ChannelDef? counter = null;
            foreach (ChannelDef c in Channels)
                if (IsBaseChannel(c.Id) && c.Stage <= stage && (counter == null || c.Stage > counter.Stage)) counter = c;
            var list = new List<ChannelDef>();
            foreach (ChannelDef c in Channels)
            {
                if (c == counter) { list.Add(c); continue; }
                if (c.Stage > stage || IsBaseChannel(c.Id)) continue;
                foreach (string id in owned)
                    if (_purchases.TryGetValue(id, out PurchaseDef? p) && p.Effect.UnlocksChannel == c.Id) { list.Add(c); break; }
            }
            return list;
        }

        /// <summary>The counter channel of <paramref name="stage"/> (the open base channel), or null.</summary>
        public ChannelDef? CounterOf(ClimbStage stage)
        {
            ChannelDef? counter = null;
            foreach (ChannelDef c in Channels)
                if (IsBaseChannel(c.Id) && c.Stage <= stage && (counter == null || c.Stage > counter.Stage)) counter = c;
            return counter;
        }

        /// <summary>Open purchase-unlocked channels (the "extra channels" of a gate).</summary>
        public int ExtraChannels(ClimbStage stage, IReadOnlyList<string> owned)
        {
            int n = 0;
            foreach (ChannelDef c in ActiveChannels(stage, owned)) if (!IsBaseChannel(c.Id)) n++;
            return n;
        }

        /// <summary>The menu: the stage 1 dish, then dishes unlocked by owned purchases in buying order, at most the stage's MaxDishes.</summary>
        public IReadOnlyList<DishDef> Menu(ClimbStage stage, IReadOnlyList<string> owned)
        {
            int max = StageOf(stage).MaxDishes;
            var list = new List<DishDef> { Dish(Stage1DishId) };
            foreach (string id in owned)
            {
                if (list.Count >= max) break;
                if (_purchases.TryGetValue(id, out PurchaseDef? p) && p.Effect.UnlocksDish != null && p.Stage <= stage
                    && !list.Exists(d => d.Id == p.Effect.UnlocksDish))
                    list.Add(Dish(p.Effect.UnlocksDish));
            }
            return list;
        }

        /// <summary>Rivals on <paramref name="channelId"/> in id order.</summary>
        public IReadOnlyList<RivalDef> RivalsOn(string channelId)
        {
            var list = new List<RivalDef>();
            foreach (RivalDef r in Rivals) if (r.ChannelId == channelId) list.Add(r);
            return list;
        }

        /// <summary>Actions owned purchases save on <paramref name="target"/> (a dish id or <see cref="CleaningTarget"/>).</summary>
        public int ActionsSaved(IReadOnlyList<string> owned, string target) =>
            Sum(owned, p => p.Effect.SavedOn == target ? p.Effect.ActionsSaved : 0);

        /// <summary>Quality bonus of owned purchases (permille).</summary>
        public int QualityBonus(IReadOnlyList<string> owned) => Sum(owned, p => p.Effect.QualityBonus);

        /// <summary>Ingredient cost saved by owned purchases (permille, at most 1000).</summary>
        public int CostSavedPermille(IReadOnlyList<string> owned) => Math.Min(1000, Sum(owned, p => p.Effect.CostSavedPermille));

        /// <summary>Demand ceiling bonus of owned purchases on <paramref name="channelId"/> (permille).</summary>
        public int CeilingBonusPermille(IReadOnlyList<string> owned, string channelId) =>
            Sum(owned, p => p.Effect.CeilingChannel == null || p.Effect.CeilingChannel == channelId ? p.Effect.CeilingBonusPermille : 0);

        /// <summary>Reputation bonus of owned purchases (permille).</summary>
        public int ReputationBonus(IReadOnlyList<string> owned) => Sum(owned, p => p.Effect.ReputationBonus);

        /// <summary>Owned purchases that are staff (they add actions every service).</summary>
        public IReadOnlyList<PurchaseDef> Staff(IReadOnlyList<string> owned)
        {
            var list = new List<PurchaseDef>();
            foreach (string id in owned)
                if (_purchases.TryGetValue(id, out PurchaseDef? p) && p.Effect.StaffActionsPerMinute > 0) list.Add(p);
            return list;
        }

        /// <summary>Spec 052b: work spots added by owned purchases (per copy).</summary>
        public int PurchaseWorkSpots(IReadOnlyList<string> owned) => Sum(owned, p => p.Effect.WorkSpots);

        /// <summary>Spec 052b: owned staff copies (purchases with StaffActionsPerMinute above 0).</summary>
        public int StaffCount(IReadOnlyList<string> owned) => Sum(owned, p => p.Effect.StaffActionsPerMinute > 0 ? 1 : 0);

        /// <summary>Spec 052b: owned machine copies (stage 4 purchases that save actions and are no staff).</summary>
        public int MachineCount(IReadOnlyList<string> owned) =>
            Sum(owned, p => p.Stage == ClimbStage.Automation && p.Effect.ActionsSaved > 0 && p.Effect.StaffActionsPerMinute == 0 ? 1 : 0);

        /// <summary>Upkeep of one service: the stage, its open channels and every owned purchase (wages included).</summary>
        public long Upkeep(ClimbStage stage, IReadOnlyList<string> owned)
        {
            long total = StageOf(stage).Upkeep;
            foreach (ChannelDef c in ActiveChannels(stage, owned)) total += c.Upkeep;
            foreach (string id in owned) if (_purchases.TryGetValue(id, out PurchaseDef? p)) total += p.UpkeepPerService;
            return total;
        }

        private int Sum(IReadOnlyList<string> owned, Func<PurchaseDef, int> f)
        {
            int total = 0;
            foreach (string id in owned) if (_purchases.TryGetValue(id, out PurchaseDef? p)) total += f(p);
            return total;
        }

        // ---------- validation ----------

        private void Validate()
        {
            foreach (PlayerProfile p in Profiles)
            {
                Id(p.Id, "profile"); Add(_profiles, p.Id, p, "profile");
                AtLeast(p.ActionsPerMinute, 1, "profile " + p.Id + " ActionsPerMinute");
                Permille(p.SkillPermille, "profile " + p.Id + " SkillPermille");
            }
            foreach (DishDef d in Dishes)
            {
                Id(d.Id, "dish"); Add(_dishes, d.Id, d, "dish"); Name(d.Name, d.Id);
                AtLeast(d.Actions, 1, "dish " + d.Id + " Actions");
                Money(d.IngredientCost, "dish " + d.Id); Money(d.ReferencePrice, "dish " + d.Id);
                if (d.ReferencePrice <= d.IngredientCost) throw new ArgumentException("Dish " + d.Id + ": ReferencePrice must exceed IngredientCost.");
                Permille(d.BaseQualityPermille, "dish " + d.Id + " BaseQualityPermille");
            }
            foreach (ChannelDef c in Channels)
            {
                Id(c.Id, "channel"); Add(_channels, c.Id, c, "channel"); Name(c.Name, c.Id); StageValue(c.Stage, c.Id);
                AtLeast(c.GuestsPerService, 0, "channel " + c.Id); AtLeast(c.WorkSpots, 0, "channel " + c.Id);
                Permille(c.ActionsPermille, "channel " + c.Id); Permille(c.PricePermille, "channel " + c.Id); Money(c.Upkeep, "channel " + c.Id);
            }
            foreach (RivalDef r in Rivals)
            {
                Id(r.Id, "rival"); Add(_rivals, r.Id, r, "rival"); Name(r.Name, r.Id);
                if (!_channels.ContainsKey(r.ChannelId)) throw new ArgumentException("Rival " + r.Id + ": unknown channel " + r.ChannelId + ".");
                Permille(r.QualityPermille, "rival " + r.Id); Permille(r.ReputationPermille, "rival " + r.Id);
                AtLeast(r.WaitSeconds, 0, "rival " + r.Id); AtLeast(r.MaxPriceCuts, 0, "rival " + r.Id); AtLeast(r.MaxQualitySteps, 0, "rival " + r.Id);
                if (r.PriceOffset % MoneyScale != 0) throw new ArgumentException("Rival " + r.Id + ": PriceOffset must be a whole multiple of MoneyScale.");
            }
            foreach (PurchaseDef p in Purchases)
            {
                Id(p.Id, "purchase"); Add(_purchases, p.Id, p, "purchase"); Name(p.Name, p.Id); StageValue(p.Stage, p.Id);
                Money(p.Cost, "purchase " + p.Id); Money(p.UpkeepPerService, "purchase " + p.Id);
                PurchaseEffect e = p.Effect ?? throw new ArgumentException("Purchase " + p.Id + ": Effect is required.");
                AtLeast(e.ActionsSaved, 0, "purchase " + p.Id); AtLeast(e.QualityBonus, 0, "purchase " + p.Id);
                AtLeast(e.ReputationBonus, 0, "purchase " + p.Id); AtLeast(e.StaffActionsPerMinute, 0, "purchase " + p.Id);
                Permille(e.CostSavedPermille, "purchase " + p.Id); Permille(e.CeilingBonusPermille, "purchase " + p.Id);
                Permille(e.StaffSkillPermille, "purchase " + p.Id);
                AtLeast(e.WorkSpots, 0, "purchase " + p.Id + " WorkSpots"); AtLeast(p.MaxCount, 0, "purchase " + p.Id + " MaxCount");
                if (p.CostGrowthPermille < 1000 || p.CostGrowthPermille > MaxPermille) throw new ArgumentException("Purchase " + p.Id + ": CostGrowthPermille must be 1000.." + MaxPermille + ".");
                if (p.Kind == PurchaseKind.Gate && p.MaxCount != 1) throw new ArgumentException("Purchase " + p.Id + ": a gate is bought once (MaxCount 1).");
            }
            foreach (PurchaseDef p in Purchases)
            {
                PurchaseEffect e = p.Effect;
                if (e.SavedOn != null && e.SavedOn != CleaningTarget && !_dishes.ContainsKey(e.SavedOn))
                    throw new ArgumentException("Purchase " + p.Id + ": unknown SavedOn " + e.SavedOn + ".");
                if (e.CeilingChannel != null && !_channels.ContainsKey(e.CeilingChannel))
                    throw new ArgumentException("Purchase " + p.Id + ": unknown CeilingChannel " + e.CeilingChannel + ".");
                if (e.UnlocksChannel != null)
                {
                    if (!_channels.ContainsKey(e.UnlocksChannel)) throw new ArgumentException("Purchase " + p.Id + ": unknown channel " + e.UnlocksChannel + ".");
                    _unlockedChannels.Add(e.UnlocksChannel);
                }
                if (e.UnlocksDish != null && !_dishes.ContainsKey(e.UnlocksDish))
                    throw new ArgumentException("Purchase " + p.Id + ": unknown dish " + e.UnlocksDish + ".");
            }
            foreach (StageDef st in Stages)
            {
                StageValue(st.Stage, "stage");
                if (_stages[(int)st.Stage] != null) throw new ArgumentException("Duplicate stage " + st.Stage + ".");
                _stages[(int)st.Stage] = st;
                AtLeast(st.WorkSpots, 0, "stage " + st.Stage); Permille(st.OverflowPermille, "stage " + st.Stage);
                AtLeast(st.PaymentActions, 0, "stage " + st.Stage); AtLeast(st.CleaningActions, 0, "stage " + st.Stage);
                Money(st.Upkeep, "stage " + st.Stage); AtLeast(st.MaxDishes, 1, "stage " + st.Stage);
                if (st.GateStarsTenths < 0 || st.GateStarsTenths > 50) throw new ArgumentException("Stage " + st.Stage + ": GateStarsTenths must be 0..50.");
                AtLeast(st.GateWindow, 0, "stage " + st.Stage); AtLeast(st.GateMinChannels, 0, "stage " + st.Stage);
                Permille(st.ReputationFloorPermille, "stage " + st.Stage);
                Count16(st.GateMinStaff, "stage " + st.Stage + " GateMinStaff"); Count16(st.GateMinMachines, "stage " + st.Stage + " GateMinMachines");
                if (st.WorkPermille < 1 || st.WorkPermille > MaxPermille) throw new ArgumentException("Stage " + st.Stage + ": WorkPermille must be 1.." + MaxPermille + ".");
                Permille(st.TipPermille, "stage " + st.Stage + " TipPermille"); AtLeast(st.StockPerService, 0, "stage " + st.Stage + " StockPerService");
                if (st.GatePurchaseId != null && !_purchases.ContainsKey(st.GatePurchaseId))
                    throw new ArgumentException("Stage " + st.Stage + ": unknown gate purchase " + st.GatePurchaseId + ".");
            }
            for (int i = 1; i <= 5; i++)
                if (_stages[i] == null) throw new ArgumentException("Missing stage " + (ClimbStage)i + ": exactly one StageDef per stage.");
            if (_stages[1].MaxDishes != 1) throw new ArgumentException("Stage 1 must have MaxDishes == 1.");
            foreach (BigJobDef j in BigJobs)
            {
                Id(j.Id, "big job"); Add(_bigJobs, j.Id, j, "big job"); Name(j.Name, j.Id);
                Money(j.Deposit, "big job " + j.Id); Money(j.Prize, "big job " + j.Id);
                AtLeast(j.Guests, 0, "big job " + j.Id); AtLeast(j.CooldownServices, 0, "big job " + j.Id);
                Permille(j.PricePermille, "big job " + j.Id); Permille(j.RivalScorePermille, "big job " + j.Id);
                if (j.StakeGrowthPermille < 1000 || j.StakeGrowthPermille > MaxPermille) throw new ArgumentException("Big job " + j.Id + ": StakeGrowthPermille must be 1000.." + MaxPermille + ".");
                if (j.MaxTier < 0 || j.MaxTier > 64) throw new ArgumentException("Big job " + j.Id + ": MaxTier must be 0..64.");
                if (j.RequiresPurchase != null && !_purchases.ContainsKey(j.RequiresPurchase))
                    throw new ArgumentException("Big job " + j.Id + ": unknown purchase " + j.RequiresPurchase + ".");
            }
            ChoiceWeights w = Weights;
            foreach (int v in new[] { w.Quality, w.Reputation, w.Price, w.Wait, w.Variety, w.Outside, w.Min }) Permille(v, "weights");
            if (Stage1DishId == null || !_dishes.ContainsKey(Stage1DishId)) throw new ArgumentException("Unknown stage 1 dish " + Stage1DishId + ".");
            if (CounterOf(ClimbStage.FoodCorner) == null) throw new ArgumentException("Stage 1 needs a base channel.");
            Money(StartingMoney, "starting money");
            ValidateCrewTable();
        }

        private void ValidateCrewTable()
        {
            if (CrewTable.Count == 0) throw new ArgumentException("The crew table needs at least one row.");
            int last = 0;
            foreach (CrewScaleRow r in CrewTable)
            {
                if (r.MaxPlayers <= last) throw new ArgumentException("Crew table: MaxPlayers must be at least 1 and ascending (" + r.MaxPlayers + ").");
                last = r.MaxPlayers;
                if (r.DifficultyPermille < 1 || r.DifficultyPermille > MaxPermille) throw new ArgumentException("Crew table: DifficultyPermille must be 1.." + MaxPermille + ".");
                if (r.PatienceSeconds < 0 || r.PatienceSeconds > 600) throw new ArgumentException("Crew table: PatienceSeconds must be 0..600.");
                Permille(r.FixedDemandPermille, "crew table FixedDemandPermille"); Permille(r.FixedGoalPermille, "crew table FixedGoalPermille");
                AtLeast(r.FixedStock, 0, "crew table FixedStock");
            }
            if (last < 16) throw new ArgumentException("Crew table: the last row must cover 16 players (MaxPlayers >= 16).");
        }

        private static void Count16(int value, string what)
        {
            if (value < 0 || value > 16) throw new ArgumentException(what + " must be 0..16.");
        }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> items, string name) where T : class
        {
            if (items == null) throw new ArgumentException(name + " are required.", name);
            var list = items.ToList();
            if (list.Any(i => i == null)) throw new ArgumentException(name + " contain null.", name);
            return list.AsReadOnly();
        }

        private static void Id(string id, string kind)
        {
            if (id == null || !IdPattern.IsMatch(id)) throw new ArgumentException("Bad " + kind + " id '" + id + "': use ^[a-z0-9_]{1,32}$.");
        }

        private static void Add<T>(Dictionary<string, T> map, string id, T value, string kind)
        {
            if (map.ContainsKey(id)) throw new ArgumentException("Duplicate " + kind + " id " + id + ".");
            map[id] = value;
        }

        private static void Name(string name, string id)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(id + ": Name is required.");
            string? word = ContentRules.FindForbidden(name);
            if (word != null) throw new ArgumentException(id + ": name '" + name + "' contains the forbidden word '" + word + "' (D27).");
        }

        private static void StageValue(ClimbStage stage, string id)
        {
            if ((int)stage < 1 || (int)stage > 5) throw new ArgumentException(id + ": stage must be 1..5.");
        }

        private static void AtLeast(int value, int min, string what)
        {
            if (value < min) throw new ArgumentException(what + " must be at least " + min + ".");
        }

        private static void Permille(int value, string what)
        {
            if (value < 0 || value > MaxPermille) throw new ArgumentException(what + ": permille must be 0.." + MaxPermille + ".");
        }

        private void Money(long value, string what)
        {
            if (value < 0) throw new ArgumentException(what + ": money must be >= 0.");
            if (value % MoneyScale != 0) throw new ArgumentException(what + ": money must be a whole multiple of MoneyScale " + MoneyScale + " (no cent is floored away).");
        }

        private static T Get<T>(Dictionary<string, T> map, string id, string kind)
        {
            if (id != null && map.TryGetValue(id, out T? value)) return value!;
            throw new ArgumentException("Unknown " + kind + " " + id + ".");
        }

        // ---------- default data (PROVISIONAL: tuning changes only these numbers; see specs 052 and 052b Implementation notes) ----------

        /// <summary>
        /// The default climb data of spec 052 (whole dollars times <paramref name="moneyScale"/>; spec 037: cents by default, D39).
        /// Spec 052b: <paramref name="crewTable"/> null is <see cref="DefaultCrewTable"/> (D40). <paramref name="stage1Extras"/> false leaves
        /// out the stage 1 Productive purchases (second basket, corner sign): the food corner as the game built it (spec 053), for C1
        /// and the GAME NUMBERS.
        /// </summary>
        public static ClimbCatalog CreateDefault(long moneyScale = CentsPerDollar, IReadOnlyList<CrewScaleRow>? crewTable = null,
            bool stage1Extras = true)
        {
            var profiles = new[]
            {
                new PlayerProfile("novice", 20, 450),
                new PlayerProfile("average", 26, 650),
                new PlayerProfile("expert", 36, 850),
            };
            var dishes = new[]
            {
                new DishDef("fries", "Fries", 6, 0, 4, 650),
                new DishDef("falafel_wrap", "Falafel Wrap", 10, 3, 9, 700),
                new DishDef("seitan_wrap", "Seitan Wrap", 11, 3, 10, 730),
                new DishDef("grilled_veggies", "Grilled Veggies", 9, 2, 8, 670),
                new DishDef("lemonade", "Lemonade", 3, 1, 3, 650),
            };
            var stages = new[]
            {
                new StageDef(ClimbStage.FoodCorner, 4, 250, 3, 0, 5, 1, "rent_restaurant", 0, 0, 0, 0, 0, 300,
                    WorkPermille: 3700, TipPermille: 250, StockPerService: 8),
                new StageDef(ClimbStage.FirstRestaurant, 6, 200, 1, 1, 50, 4, "service_license", 35, 5, 0, 0, 0, 450),
                new StageDef(ClimbStage.ServiceMix, 6, 200, 1, 1, 80, 5, null, 36, 5, 2, 0, 0, 550),
                new StageDef(ClimbStage.Automation, 6, 200, 1, 1, 100, 5, "automation_plan", 36, 5, 0, 2, 2, 600),
                new StageDef(ClimbStage.BeyondKitchen, 6, 200, 1, 1, 130, 5, null, 0, 0, 0, 0, 0, 600),
            };
            var channels = new[]
            {
                new ChannelDef("corner_counter", "Corner Counter", ClimbStage.FoodCorner, 40, 1000, 1000, 0, 0),
                new ChannelDef("street_counter", "Street Counter", ClimbStage.FirstRestaurant, 90, 1000, 1000, 0, 0),
                new ChannelDef("delivery", "Delivery", ClimbStage.ServiceMix, 30, 1300, 1100, 2, 25),
                new ChannelDef("custom_orders", "Custom Orders", ClimbStage.ServiceMix, 10, 1600, 1600, 1, 10),
                new ChannelDef("tables", "Tables", ClimbStage.ServiceMix, 25, 1200, 1150, 3, 20),
                new ChannelDef("buffet", "Buffet", ClimbStage.ServiceMix, 30, 500, 900, 1, 30),
            };
            var rivals = new[]
            {
                new RivalDef("corner_kiosk", "Corner Kiosk", "corner_counter", 450, 0, 45, 500, 2, 3),
                new RivalDef("miras_wraps", "Mira's Wraps", "street_counter", 600, 0, 75, 600, 3, 4),
                new RivalDef("dash_bowls", "Dash Bowls", "delivery", 520, -1, 90, 550, 3, 3),
                new RivalDef("long_table", "The Long Table", "tables", 650, 1, 120, 650, 2, 4),
            };
            var purchases = new[]
            {
                P("second_basket", "Second Basket", 1, PurchaseKind.Productive, 30, 0, Saves(2, "fries")),
                P("corner_sign", "Corner Sign", 1, PurchaseKind.Productive, 40, 0, Ceiling(100, null)),
                P("rent_restaurant", "Rent the Restaurant", 1, PurchaseKind.Gate, 300, 0, PurchaseEffect.None),
                P("dish_falafel_wrap", "Falafel Wrap Recipe", 2, PurchaseKind.Productive, 200, 0, Recipe("falafel_wrap")),
                P("dish_seitan_wrap", "Seitan Wrap Recipe", 2, PurchaseKind.Productive, 250, 0, Recipe("seitan_wrap")),
                P("dish_grilled_veggies", "Grilled Veggies Recipe", 2, PurchaseKind.Productive, 200, 0, Recipe("grilled_veggies")),
                P("dish_lemonade", "Lemonade Recipe", 2, PurchaseKind.Productive, 150, 0, Recipe("lemonade")),
                P("bulk_supplier", "Bulk Supplier", 2, PurchaseKind.Productive, 400, 0, PurchaseEffect.None with { CostSavedPermille = 200 }),
                P("decor_1", "Decor I", 2, PurchaseKind.Productive, 500, 0, Rep(30)),
                P("service_license", "Service License", 2, PurchaseKind.Gate, 1200, 0, PurchaseEffect.None),
                P("delivery_kit", "Delivery Kit", 3, PurchaseKind.Productive, 1800, 0, PurchaseEffect.None with { UnlocksChannel = "delivery" }),
                P("custom_counter", "Custom Order Counter", 3, PurchaseKind.Productive, 900, 0, PurchaseEffect.None with { UnlocksChannel = "custom_orders" }),
                P("tables_room", "Tables Room", 3, PurchaseKind.Productive, 1500, 0, PurchaseEffect.None with { UnlocksChannel = "tables" }),
                P("buffet_bar", "Buffet Bar", 3, PurchaseKind.Productive, 2000, 0, PurchaseEffect.None with { UnlocksChannel = "buffet" }),
                P("decor_2", "Decor II", 3, PurchaseKind.Productive, 1200, 0, Rep(30)),
                P("bigger_place", "Bigger Place", 3, PurchaseKind.Productive, 3000, 40, PurchaseEffect.None with { WorkSpots = 1, CeilingBonusPermille = 100 }, 4, 1600),
                P("staff_runner", "Runner", 4, PurchaseKind.Productive, 1000, 35, Staff(22, 600)),
                P("staff_cook", "Line Cook", 4, PurchaseKind.Productive, 2000, 45, Staff(26, 650), 6, 1300),
                P("auto_fill_drinks", "Auto Drink Filler", 4, PurchaseKind.Productive, 1500, 5, Saves(3, "lemonade")),
                P("dishwasher", "Dishwasher", 4, PurchaseKind.Productive, 2000, 5, Saves(1, CleaningTarget)),
                P("fry_dump", "Fry Dump Station", 4, PurchaseKind.Productive, 1500, 5, Saves(2, "fries")),
                P("automation_plan", "Automation Plan", 4, PurchaseKind.Gate, 3000, 0, PurchaseEffect.None),
                P("food_truck", "Food Truck", 5, PurchaseKind.Productive, 6000, 20, PurchaseEffect.None),
                P("delivery_car", "Delivery Car", 5, PurchaseKind.Productive, 3000, 10, Ceiling(200, "delivery")),
                P("ingredient_lab", "Ingredient Lab", 5, PurchaseKind.Productive, 4000, 20, PurchaseEffect.None with { QualityBonus = 60 }),
                P("campaign_1", "Ad Campaign I", 5, PurchaseKind.Productive, 1000, 0, Ceiling(100, null)),
                P("campaign_2", "Ad Campaign II", 5, PurchaseKind.Productive, 2000, 0, Ceiling(100, null)),
                P("campaign_3", "Ad Campaign III", 5, PurchaseKind.Productive, 4000, 0, Ceiling(100, null)),
                P("car_collection", "Car Collection", 5, PurchaseKind.Status, 15000, 10, Rep(5), 0, 1000),
                P("decor", "Decor", 5, PurchaseKind.Status, 3000, 0, Rep(10), 0, 1000),
            };
            var bigJobs = new[]
            {
                new BigJobDef("cook_off", "Cook-Off", BigJobKind.Competition, 800, 0, 1000, 700, 4000, null, 4, 1200, 5),
                new BigJobDef("street_festival", "Street Festival", BigJobKind.Event, 600, 150, 1100, 0, 0, "food_truck", 4, 1200, 5),
                new BigJobDef("stadium_day", "Stadium Day", BigJobKind.Event, 1000, 250, 1200, 0, 0, "food_truck", 6, 1200, 5),
            };
            var weights = new ChoiceWeights(Quality: 900, Reputation: 700, Price: 2600, Wait: 300, Variety: 80, Outside: 700, Min: 20);
            IEnumerable<PurchaseDef> shop = stage1Extras ? purchases
                : purchases.Where(p => !(p.Stage == ClimbStage.FoodCorner && p.Kind == PurchaseKind.Productive));
            return new ClimbCatalog(profiles, dishes, channels, rivals, shop, stages, bigJobs, weights, "fries", 25, moneyScale,
                crewTable ?? DefaultCrewTable);
        }

        private static PurchaseDef P(string id, string name, int stage, PurchaseKind kind, long cost, long upkeep, PurchaseEffect effect,
            int maxCount = 1, int growthPermille = 1000) =>
            new PurchaseDef(id, name, (ClimbStage)stage, kind, cost, upkeep, effect, maxCount, growthPermille);

        private static PurchaseEffect Saves(int actions, string on) => PurchaseEffect.None with { ActionsSaved = actions, SavedOn = on };
        private static PurchaseEffect Ceiling(int permille, string? channel) => PurchaseEffect.None with { CeilingBonusPermille = permille, CeilingChannel = channel };
        private static PurchaseEffect Rep(int bonus) => PurchaseEffect.None with { ReputationBonus = bonus };
        private static PurchaseEffect Recipe(string dishId) => PurchaseEffect.None with { UnlocksDish = dishId };
        private static PurchaseEffect Staff(int apm, int skill) => PurchaseEffect.None with { StaffActionsPerMinute = apm, StaffSkillPermille = skill };
    }
}
