using System;
using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;
using Worktown.Game.Climb;
using Worktown.Game.Economy;
using Worktown.Game.Net;
using Worktown.Game.Npc;
using Worktown.Game.Shop;
using Worktown.Sim.Climb;
using Worktown.Sim.Economy;

namespace Worktown.Game.Save
{
    /// <summary>
    /// Spec 053, server (the host): the shop's save. When the server starts it begins the game NetBootstrap.HostStart asks for: the open
    /// kitchen (no save: <c>-host</c>, <c>-server</c>, the tours), a New shop (a new food corner, its own new file written at once) or a
    /// Continue (the newest save applied before the first player spawns: the shop wallet gets the saved balance with no grant, the
    /// stock counts, hygiene, stage, goal and stats are set, the economy starts fresh at 08:00 of the saved day, seeded worldSeed ^ day).
    /// It drives the stage's service moments (ShopStage: the drop at a start; the pitch fee and the goal at an end) and saves on the
    /// AutosaveRules triggers: a service end, the goal, "Close the shop", the quit, every 5 minutes of a service. Only the host writes;
    /// a client never reads or writes a shop save. A save that cannot be written is logged and the game goes on.
    /// </summary>
    public sealed class SaveService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private EconomyService _economy;
        [SerializeField] private SupplyService _supply;
        [SerializeField] private CleaningService _cleaning;
        [SerializeField] private CustomerService _customers;
        [SerializeField] private ShopStage _stage;

        private const int NewIdAttempts = 8;

        private NetBootstrap _bootstrap;
        private ShopSave _current;
        private readonly List<ShopSave.StockPart> _unknownStock = new List<ShopSave.StockPart>();
        private bool _begun;
        private float _lastSaveAt;
        private float _playStartedAt;
        private long _playSecondsBefore;

        /// <summary>The save of the running game (null: the open kitchen, no save).</summary>
        public ShopSave Current => _current;
        /// <summary>The file the running game saves to (empty without one).</summary>
        public string CurrentPath => _current != null && Store != null ? Store.PathFor(_current.saveId) ?? string.Empty : string.Empty;
        /// <summary>How many saves were written in this process (the tour and the log).</summary>
        public int Written { get; private set; }
        /// <summary>The balance and day of the last save written (the corner tour checks Continue against them).</summary>
        public long LastSavedBalance { get; private set; } = -1;
        public int LastSavedDay { get; private set; }
        /// <summary>The balance and day the last Continue applied.</summary>
        public long LoadedBalance { get; private set; } = -1;
        public int LoadedDay { get; private set; }

        private SaveStore Store => _bootstrap != null ? _bootstrap.Saves : null;

        private void Start()
        {
            _bootstrap = FindFirstObjectByType<NetBootstrap>();
            if (_networkManager != null) _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
            if (_customers != null)
            {
                _customers.ServiceOpened += OnServiceOpened;
                _customers.ServiceClosed += OnServiceClosed;
            }
            if (_networkManager != null && _networkManager.IsServerStarted) Begin();
        }

        private void OnDestroy()
        {
            if (_networkManager != null && _networkManager.ServerManager != null)
                _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
            if (_customers != null)
            {
                _customers.ServiceOpened -= OnServiceOpened;
                _customers.ServiceClosed -= OnServiceClosed;
            }
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                if (!_begun) Begin(); // once per server run (each transport reports its own start)
                return;
            }
            if (args.ConnectionState != LocalConnectionState.Stopped || _networkManager.ServerManager.IsAnyServerStarted()) return;
            _begun = false;
            _current = null; // "Close the shop" saved before the stop (NetBootstrap.Leave)
            _unknownStock.Clear();
        }

        // ---- beginning a game ----

        private void Begin()
        {
            _begun = true;
            ShopStart start = _bootstrap != null ? _bootstrap.HostStart : ShopStart.OpenKitchen;
            _current = null;
            _unknownStock.Clear();
            try
            {
                switch (start)
                {
                    case ShopStart.NewShop:
                        BeginNewShop();
                        break;
                    case ShopStart.Continue:
                        BeginContinue();
                        break;
                    default:
                        if (_stage != null) _stage.ServerBegin(ShopStageRules.OpenKitchen, 0, 0, false);
                        Debug.Log("[Save] the open kitchen: no save");
                        break;
                }
            }
            catch (Exception e)
            {
                // A save must never stop the game: what failed is logged and the shop opens as the open kitchen would.
                Debug.LogException(e);
                Debug.LogError("[Save] the game could not begin from its save; the shop opens without one");
                _current = null;
            }
        }

        private void BeginNewShop()
        {
            SaveStore store = Store;
            ClimbCatalog climb = ClimbCatalog.CreateDefault();
            EconomyCatalog economy = _economy != null ? _economy.Catalog : EconomyCatalog.CreateDefault();
            string id = NewSaveId();
            for (int i = 0; store != null && store.HasSlot && store.IsTaken(id) && i < NewIdAttempts; i++) id = NewSaveId();
            ShopSave save = NewSave(id, NewSeed(), FoodCornerRules.StartingMoney(economy, climb), ShopSaveCodec.TimeText(DateTime.UtcNow),
                Application.version, _bootstrap != null && _bootstrap.Join != null ? _bootstrap.Join.BuildHash : string.Empty);
            Apply(save);
            if (store == null || !store.HasSlot)
            {
                _current = null;
                Debug.Log("[Save] a new food corner without a save slot (a scripted run without -save-dir): nothing is written");
                return;
            }
            if (!store.CreateNew(save, out string error))
            {
                Debug.LogError("[Save] the new shop's file could not be written (" + error + "); it saves again at the next moment");
                return;
            }
            Wrote(save, SaveTrigger.Created);
        }

        private void BeginContinue()
        {
            SaveStore store = Store;
            SaveLoadResult newest = store != null && store.HasSlot ? store.Newest() : null;
            if (newest == null || !newest.Ok)
            {
                // 053 review: never a new shop instead: the shop closes again and the title shows why (a scripted run exits with an error).
                SaveError error = newest != null ? newest.Error : SaveError.NotJson;
                Debug.LogError("[Save] Continue found no save that loads (" + (newest != null ? error + " in " + System.IO.Path.GetFileName(newest.Path) : "none")
                    + "); the file is kept");
                if (_stage != null) _stage.ServerBegin(ShopStageRules.OpenKitchen, 0, 0, false);
                if (_bootstrap != null) _bootstrap.FailSaveStart(error);
                return;
            }
            foreach (string note in newest.Notes) Debug.LogWarning("[Save] clamped: " + note);
            if (newest.FromBackup) Debug.LogWarning("[Save] " + System.IO.Path.GetFileName(newest.Path) + " was broken; its .bak copy loaded");
            Apply(newest.Save);
            LoadedBalance = newest.Save.money.balance;
            LoadedDay = newest.Save.clock.day;
            Debug.Log("[Save] continued " + System.IO.Path.GetFileName(newest.Path) + ": day " + LoadedDay + ", " + EconomyRules.Money(LoadedBalance));
        }

        /// <summary>Applies <paramref name="save"/> to the starting server (before the first player spawns).</summary>
        private void Apply(ShopSave save)
        {
            _current = save;
            _unknownStock.Clear();
            foreach (ShopSave.StockPart part in save.stock)
                if (part != null && ShopSaveCodec.LineOfSlot(part.slot) < 0) _unknownStock.Add(part);
            if (_economy != null)
                _economy.StartGame(SeedOf(save), save.clock.day, save.money.balance, save.money.earnedTotal, save.money.spentTotal);
            if (_supply != null)
            {
                _supply.EnsureGame();
                _supply.Room.RestoreCounts(CountsOf(save));
                _supply.PublishIfChanged();
            }
            if (_cleaning != null) _cleaning.RestoreHygiene(save.hygienePercent);
            ClimbCatalog climb = ClimbCatalog.CreateDefault();
            EconomyCatalog economy = _economy != null ? _economy.Catalog : EconomyCatalog.CreateDefault();
            if (_stage != null)
                _stage.ServerBegin(ShopStageRules.ClampSaved(save.climb.stage), FoodCornerRules.RentGoal(economy, climb),
                    FoodCornerRules.PitchFee(economy, climb), save.climb.goalReached);
            _playSecondsBefore = save.clock.playSeconds;
            _playStartedAt = Time.unscaledTime;
            _lastSaveAt = Time.unscaledTime;
        }

        // ---- the service moments ----

        private void OnServiceOpened()
        {
            if (_stage != null) _stage.ServerServiceOpened();
        }

        private void OnServiceClosed()
        {
            bool goal = _stage != null && _stage.ServerServiceEnded();
            if (_current == null || _customers == null) return;
            AddService(_current, _customers.LastSummary);
            SaveNow(goal ? SaveTrigger.GoalReached : SaveTrigger.ServiceEnd);
        }

        private void Update()
        {
            if (_current == null || _networkManager == null || !_networkManager.IsServerStarted) return;
            if (AutosaveRules.ShouldSave(SaveTrigger.Periodic, true, Store != null && Store.HasSlot, Time.unscaledTime - _lastSaveAt,
                    _customers != null && _customers.ServiceOpen))
                SaveNow(SaveTrigger.Periodic);
        }

        private void OnApplicationQuit()
        {
            if (_networkManager != null && _networkManager.IsServerStarted) SaveNow(SaveTrigger.AppQuit); // best effort
        }

        // ---- saving ----

        /// <summary>Server: saves the running game now (when <paramref name="trigger"/> says so); false when nothing was written.</summary>
        public bool SaveNow(SaveTrigger trigger)
        {
            SaveStore store = Store;
            bool server = _networkManager != null && _networkManager.IsServerStarted;
            if (_current == null || !AutosaveRules.ShouldSave(trigger, server, store != null && store.HasSlot, Time.unscaledTime - _lastSaveAt,
                    _customers != null && _customers.ServiceOpen))
                return false;
            try
            {
                Capture(_current);
                if (!store.Write(_current, out string error))
                {
                    Debug.LogError("[Save] could not write the save (" + trigger + "): " + error + "; the last good file is kept");
                    return false;
                }
                Wrote(_current, trigger);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError("[Save] saving failed (" + trigger + "); the game goes on");
                return false;
            }
        }

        private void Wrote(ShopSave save, SaveTrigger trigger)
        {
            _lastSaveAt = Time.unscaledTime;
            Written++;
            LastSavedBalance = save.money.balance;
            LastSavedDay = save.clock.day;
            Debug.Log("[Save] saved " + System.IO.Path.GetFileName(Store.PathFor(save.saveId)) + " (" + trigger + "): day " + save.clock.day + ", "
                + EconomyRules.Money(save.money.balance) + ", " + save.clock.servicesPlayed + " service(s)");
        }

        /// <summary>The live game into <paramref name="save"/>.</summary>
        private void Capture(ShopSave save)
        {
            int[] counts = new int[StockCatalog.Count];
            if (_supply != null) _supply.Room.ReadCounts(counts);
            long balance = _economy != null ? _economy.ShopBalance : save.money.balance;
            long pending = _supply != null ? _supply.Room.PendingCost() : 0L; // orders on their way are refunded at the stop: count them
            long day = _customers != null ? _customers.NextDay : save.clock.day;
            long play = _playSecondsBefore + (long)Math.Max(0f, Time.unscaledTime - _playStartedAt);
            Fill(save, balance + pending, _economy != null ? _economy.Earned : save.money.earnedTotal, _economy != null ? _economy.Spent : save.money.spentTotal,
                day, play, counts, _unknownStock, _cleaning != null ? _cleaning.HygienePercent : save.hygienePercent,
                _stage != null ? _stage.ServerStage : (byte)save.climb.stage, _stage != null && _stage.ServerReached);
        }

        // ---- pure parts (tested) ----

        /// <summary>A new food corner's save: day 1, its starting money, the starting stock, 100 % hygiene, the stage 1 unlock.</summary>
        public static ShopSave NewSave(string saveId, ulong worldSeed, long startingMoney, string nowUtc, string gameVersion, string buildHash)
        {
            var counts = new int[StockCatalog.Count];
            new StockRoom().ReadCounts(counts);
            var save = new ShopSave
            {
                gameVersion = gameVersion ?? string.Empty,
                buildHash = buildHash ?? string.Empty,
                saveId = saveId,
                createdUtc = nowUtc,
                savedUtc = nowUtc,
                shopName = FoodCornerRules.ShopName,
                worldSeed = worldSeed,
            };
            save.climb.stage = ShopStageRules.FoodCorner;
            save.climb.unlocks = new[] { FoodCornerRules.StartUnlock };
            save.money.balance = Math.Max(0L, startingMoney);
            save.clock.day = 1;
            save.hygienePercent = 100;
            save.stock = StockParts(counts, null);
            return save;
        }

        /// <summary>The live values into <paramref name="save"/> (clamped like the codec clamps a load).</summary>
        public static void Fill(ShopSave save, long balance, long earned, long spent, long day, long playSeconds, int[] counts,
            IReadOnlyList<ShopSave.StockPart> unknownStock, int hygiene, byte stage, bool goalReached)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            save.money.balance = Math.Max(0L, Math.Min(EconomyLimits.MaxBalance, balance));
            save.money.earnedTotal = Math.Max(0L, earned);
            save.money.spentTotal = Math.Max(0L, spent);
            save.clock.day = (int)Math.Max(1L, Math.Min(int.MaxValue, day));
            save.clock.playSeconds = Math.Max(0L, playSeconds);
            save.stock = StockParts(counts, unknownStock);
            save.hygienePercent = Math.Max(0, Math.Min(100, hygiene));
            save.climb.stage = ShopStageRules.ClampSaved(stage);
            save.climb.goalReached = goalReached;
        }

        /// <summary>A closed service's numbers into the save's stats and its count.</summary>
        public static void AddService(ShopSave save, ShiftSummary summary)
        {
            if (save == null) return;
            save.clock.servicesPlayed++;
            save.stats.guestsServed += Math.Max(0, summary.Served);
            save.stats.guestsMissed += Math.Max(0, summary.Missed);
            save.stats.starSum += Math.Max(0, summary.StarsTotal);
            save.stats.bestServiceEarned = Math.Max(save.stats.bestServiceEarned, summary.Earned);
        }

        /// <summary>Every stock line's count as a save part, then the unknown parts a load brought (kept for the next save).</summary>
        public static ShopSave.StockPart[] StockParts(int[] counts, IReadOnlyList<ShopSave.StockPart> unknown)
        {
            var parts = new List<ShopSave.StockPart>();
            for (int i = 0; counts != null && i < counts.Length && i < StockCatalog.Count; i++)
                parts.Add(new ShopSave.StockPart { slot = ShopSaveCodec.SlotOf(i), count = Math.Max(0, counts[i]) });
            if (unknown != null)
                foreach (ShopSave.StockPart part in unknown)
                    if (part != null) parts.Add(new ShopSave.StockPart { slot = part.slot, count = part.count });
            return parts.ToArray();
        }

        /// <summary>The saved count of every stock line (StockCatalog order), -1 for a line the save does not name (it keeps its start).</summary>
        public static int[] CountsOf(ShopSave save)
        {
            var counts = new int[StockCatalog.Count];
            for (int i = 0; i < counts.Length; i++) counts[i] = -1;
            if (save?.stock == null) return counts;
            foreach (ShopSave.StockPart part in save.stock)
            {
                int line = part != null ? ShopSaveCodec.LineOfSlot(part.slot) : -1;
                if (line >= 0) counts[line] = Math.Max(0, part.count);
            }
            return counts;
        }

        /// <summary>The economy's seed of a loaded day: worldSeed ^ day.</summary>
        public static ulong SeedOf(ShopSave save) => save.worldSeed ^ (ulong)Math.Max(1, save.clock.day);

        private static string NewSaveId() => Guid.NewGuid().ToString("N");

        private static ulong NewSeed() => BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);
    }
}
