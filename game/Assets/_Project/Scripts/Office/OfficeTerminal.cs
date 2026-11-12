using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using Worktown.Game.Climb;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Game.Npc;
using Worktown.Sim.Climb;

namespace Worktown.Game.Office
{
    /// <summary>
    /// Spec 059: the office terminal's synced state, on the <c>office_terminal</c> piece (a station of kind OfficeTerminal): the special of
    /// the day (<see cref="Special"/>, a game dish id, 0 none), the last services (<see cref="History"/>, at most 8, saved) and who changed what
    /// (<see cref="Log"/>, packed ChangeLog entries, at most 8, not saved). Three synced fields, no RPC: presses come as the station intent
    /// (KitchenService routes RequestStation here: <see cref="ServerPress"/>), the server decides and writes. The Menu app's price steps go
    /// through MenuBoardDesk.ServerStepPrice and its toggles through ShopStage.ServerToggleRecipe, the same methods the menu board and the
    /// goal board use; this object logs both (theirs too). The client's screen is <see cref="OfficeTerminalView"/>.
    /// </summary>
    public sealed class OfficeTerminal : NetworkBehaviour
    {
        public readonly SyncVar<byte> Special = new SyncVar<byte>(Menu.None);
        public readonly SyncList<ServiceRecord> History = new SyncList<ServiceRecord>();
        public readonly SyncList<uint> Log = new SyncList<uint>();

        private byte _special;
        private ServiceRecord[] _history = Array.Empty<ServiceRecord>();
        private uint[] _log = Array.Empty<uint>();
        private EconomyService _economy;
        private ShopStage _stage;
        private bool _hooked;

        /// <summary>Client and server: counts every change seen (the views redraw only when it moved).</summary>
        public int Version { get; private set; }

        /// <summary>Server: the special of the day.</summary>
        public byte ServerSpecial => _special;
        /// <summary>Server: the last services, newest last.</summary>
        public IReadOnlyList<ServiceRecord> ServerHistory => _history;
        public IReadOnlyList<uint> ServerLog => _log;

        /// <summary>The special this process sees: the server's value, else the synced one.</summary>
        public byte SpecialNow => NetworkObject == null || IsServerInitialized ? _special : Special.Value;

        /// <summary>The office terminal of the running kitchen (the first one in the LayoutRegistry), or null.</summary>
        public static OfficeTerminal Find()
        {
            Station station = Worktown.Game.Layout.LayoutRegistry.First(StationKind.OfficeTerminal);
            return station != null ? station.GetComponent<OfficeTerminal>() : null;
        }

        private bool ServerUp => NetworkObject != null && IsServerInitialized;

        private void Awake()
        {
            Special.OnChange += (_, _, _) => Version++;
            History.OnChange += (_, _, _, _, _) => Version++;
            Log.OnChange += (_, _, _, _, _) => Version++;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            Hook();
            Push();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            Unhook();
        }

        private void OnDestroy() => Unhook();

        /// <summary>
        /// Finds the economy and the stage and listens to their changes (the log). 059 review: true only when both are there; a later press
        /// tries again (nothing is hooked half).
        /// </summary>
        private bool Hook()
        {
            if (_hooked) return true;
            if (_economy == null) _economy = FindFirstObjectByType<EconomyService>(); // the tests set them offline
            if (_stage == null) _stage = ShopStage.Find();
            if (_economy == null || _stage == null) return false;
            _economy.Menu.PriceStepped += OnPriceStepped;
            _stage.MenuToggled += OnMenuToggled;
            _hooked = true;
            return true;
        }

        private void Unhook()
        {
            if (!_hooked) return;
            if (_economy != null) _economy.Menu.PriceStepped -= OnPriceStepped;
            if (_stage != null) _stage.MenuToggled -= OnMenuToggled;
            _hooked = false;
        }

        /// <summary>Server, a game begins: a save's special (a dish id; a drink or an unknown dish reads as none) and history (the newest 8).</summary>
        public void ServerBegin(byte special, IReadOnlyList<ServiceRecord> history)
        {
            _special = Menu.ById(special) != null ? special : Menu.None;
            _history = ServiceHistory.Newest(history);
            _log = Array.Empty<uint>();
            Push();
            Debug.Log("[Office] the terminal begins: special " + (_special == Menu.None ? "none" : Menu.NameOf(_special)) + ", " + _history.Length
                + " service(s) remembered");
        }

        /// <summary>Server, a service ended: its record joins the history (the newest 8 are kept).</summary>
        public void ServerRecord(ServiceRecord record)
        {
            _history = ServiceHistory.Push(_history, record);
            Push();
            Debug.Log("[Office] day " + record.Day + ": " + record.Came + " came, " + record.Served + " served, " + record.Missed + " missed, wait "
                + record.WaitSeconds + " s, stars " + record.StarsTenths + ", sold " + record.Sold0 + "/" + record.Sold1 + "/" + record.Sold2 + "/"
                + record.Sold3 + "/" + record.Sold4 + "/" + record.Sold5);
        }

        /// <summary>
        /// Server: a press of the screen by player <paramref name="player"/> (its connection id; reach was checked):
        /// <c>option = app &lt;&lt; 12 | row &lt;&lt; 4 | action</c> (TerminalOptions). The Menu app steps a price, toggles a dish or sets the
        /// special; the Services app refuses (the counter stays on, the rest comes with stage 3); Results is read-only. Refusals change nothing.
        /// </summary>
        public CookOutcome ServerPress(int player, int option)
        {
            if (!TerminalOptions.TryUnpack(option, out TerminalApp app, out int row, out int action)) return CookOutcome.InvalidOption;
            if (!Hook()) return CookOutcome.UnknownStation; // 059 review: fail closed without the economy or the stage
            ClimbCatalog climb = _stage.Climb;
            byte stage = _stage.ServerStage;
            switch (app)
            {
                case TerminalApp.Menu: return PressMenu(player, stage, climb, row, action);
                case TerminalApp.Services: return ServicesAppRules.Decide(row, action, climb);
                default: return CookOutcome.InvalidOption;
            }
        }

        private CookOutcome PressMenu(int player, byte stage, ClimbCatalog climb, int row, int action)
        {
            byte owned = _stage != null ? _stage.ServerOwned : (byte)0;
            IReadOnlyList<int> lines = MenuAppRules.Lines(stage, climb, owned);
            if (row < 0 || row >= lines.Count) return CookOutcome.InvalidOption;
            int line = lines[row];
            switch (action)
            {
                case TerminalOptions.Minus:
                case TerminalOptions.Plus:
                    return _economy != null ? _economy.Menu.ServerStepPrice(line, MenuAppRules.StepsOf(action), player) : CookOutcome.UnknownStation;
                case TerminalOptions.Toggle:
                {
                    CookOutcome can = MenuAppRules.ToggleRow(stage, climb, line, out int recipeRow);
                    if (can != CookOutcome.Ok) return can;
                    return _stage != null ? _stage.ServerToggleRecipe(player, recipeRow) : CookOutcome.UnknownStation;
                }
                case TerminalOptions.Special:
                {
                    MenuPrices.Line l = MenuPrices.Lines[line];
                    if (l.Drink != null) return CookOutcome.InvalidOption;
                    byte menuNext = _stage != null ? _stage.ServerMenuNext : (byte)0, menuNow = _stage != null ? _stage.ServerMenuNow : (byte)0;
                    bool onMenu = SpecialRules.IsOn(l.Dish, SpecialRules.MenuDishes(stage, menuNext, climb))
                        || SpecialRules.IsOn(l.Dish, SpecialRules.MenuDishes(stage, menuNow, climb));
                    CookOutcome outcome = SpecialRules.Decide(stage, _special, l.Dish, onMenu, out byte next);
                    if (outcome != CookOutcome.Ok || next == _special) return outcome;
                    _special = next;
                    AddLog(ChangeLog.Special(ChangeLog.SlotOf(player), next));
                    Debug.Log("[Office] player " + player + ": " + (next == Menu.None ? "no special" : Menu.NameOf(next) + " is the special"));
                    return CookOutcome.Ok;
                }
                default: return CookOutcome.InvalidOption;
            }
        }

        private void OnPriceStepped(int line, long cents, int player)
        {
            AddLog(ChangeLog.Price(ChangeLog.SlotOf(player), line, cents));
            Debug.Log("[Office] player " + player + ": " + MenuPrices.Lines[line].Name + " " + EconomyRules.Money(cents));
        }

        private void OnMenuToggled(int player, int recipeRow, bool on) => AddLog(ChangeLog.Dish(ChangeLog.SlotOf(player), recipeRow, on));

        private void AddLog(uint entry)
        {
            _log = ChangeLog.Push(_log, entry);
            Push();
        }

        private void Push()
        {
            if (!ServerUp) return;
            if (Special.Value != _special) Special.Value = _special;
            SyncTo(History, _history);
            SyncTo(Log, _log);
            Version++;
        }

        /// <summary>
        /// 059 review: brings a synced list to <paramref name="values"/> with the fewest operations: the usual change (the oldest entries
        /// dropped, new ones appended) is RemoveAt(0) and Add; anything else (a game begins) is written anew.
        /// </summary>
        private static void SyncTo<T>(SyncList<T> synced, T[] values) where T : IEquatable<T>
        {
            for (int drop = 0; drop <= synced.Count; drop++)
            {
                int kept = synced.Count - drop;
                if (kept > values.Length || !SameRun(synced, drop, values, kept)) continue;
                for (int i = 0; i < drop; i++) synced.RemoveAt(0);
                for (int i = kept; i < values.Length; i++) synced.Add(values[i]);
                return;
            }
            synced.Clear();
            foreach (T v in values) synced.Add(v);
        }

        /// <summary>Whether <paramref name="synced"/> from <paramref name="from"/> on equals the first <paramref name="count"/> of <paramref name="values"/>.</summary>
        private static bool SameRun<T>(SyncList<T> synced, int from, T[] values, int count) where T : IEquatable<T>
        {
            for (int i = 0; i < count; i++) if (!synced[from + i].Equals(values[i])) return false;
            return true;
        }
    }
}
