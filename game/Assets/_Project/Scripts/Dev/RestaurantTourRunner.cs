using System;
using System.Collections;
using System.IO;
using UnityEngine;
using Worktown.Game.Climb;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;
using Worktown.Game.Npc;
using Worktown.Game.Player;
using Worktown.Game.Save;
using Worktown.Game.Shop;
using Worktown.Game.UI;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 057 (AC11, AC12), development builds only: the restaurant tour of the visual check (<c>-tour restaurant -save-dir dir</c>; one
    /// window, its own host). The save folder holds a version 1 fixture (053's format: the food corner, the rent goal reached), which the
    /// script copies in. From the title: Continue (the corner, its covers, the goal board); rent the restaurant at the board (one press arms,
    /// the second pays); the service ends (the clock to 14:00): the move, the covers gone; buy the falafel wrap (two presses); a terminal
    /// order; the next service opens with the wrap on the frozen menu and meal guests come; it ends: the summary with the rent and the gate
    /// line (the AC12 numbers are ShopStage's "[Stage] restaurant service" log line); Close the shop; Continue: still the restaurant with
    /// its recipe. Shots and the sheet go to <c>tour-out/restaurant</c>; quits with 0, or 1 if a step failed.
    /// </summary>
    public sealed class RestaurantTourRunner : MonoBehaviour
    {
        private const float StepLimit = 60f;
        private const float WholeLimit = 600f;

        private NetBootstrap _bootstrap;
        private MainMenuView _menu;
        private TourCapture _capture;
        private int _failures;
        private bool _finished;
        private float _deadline;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isBatchMode) return;
            LaunchArgs args = LaunchArgs.Parse(Environment.GetCommandLineArgs());
            if (args.Tour != LaunchArgs.RestaurantTour) return;
            Application.runInBackground = true;
            var go = new GameObject("RestaurantTourRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<RestaurantTourRunner>();
        }

        private IEnumerator Start()
        {
            LaunchArgs args = LaunchArgs.Parse(Environment.GetCommandLineArgs());
            string root = args.TourOut.Length > 0 ? args.TourOut : Path.Combine(Application.persistentDataPath, "visual");
            string dir = Path.Combine(root, LaunchArgs.RestaurantTour);
            _capture = new TourCapture(dir, TourRules.SheetCell);
            _deadline = Time.unscaledTime + WholeLimit;
            Debug.Log("[Tour] restaurant, images to " + dir + ", saves in " + args.SaveDir);
            if (!args.IsValid)
            {
                Finish(1, "bad command line: " + args.Error);
                yield break;
            }
            yield return null;
            _bootstrap = FindFirstObjectByType<NetBootstrap>();
            _menu = MainMenuView.Find();
            if (_bootstrap == null || _menu == null)
            {
                Finish(1, "no NetBootstrap or MainMenuView in the scene");
                yield break;
            }
            yield return Run();
            yield return End();
        }

        private void Update()
        {
            if (_finished || Time.unscaledTime <= _deadline) return;
            _finished = true;
            StopAllCoroutines();
            Fail("the whole tour took longer than " + WholeLimit + " s");
            StartCoroutine(End());
        }

        private JoinFlow Flow => _bootstrap.Flow;

        private void Fail(string what)
        {
            _failures++;
            Debug.LogError("[Tour] restaurant: " + what);
        }

        private IEnumerator Until(Func<bool> done, string what, float limit = StepLimit)
        {
            float until = Time.unscaledTime + limit;
            while (!done())
            {
                if (Time.unscaledTime > until)
                {
                    Fail("timed out waiting for " + what);
                    yield break;
                }
                yield return null;
            }
            Debug.Log("[Tour] restaurant: " + what);
        }

        private static IEnumerator Wait(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
        }

        private IEnumerator ContinueToShop(string what)
        {
            if (!_menu.HasContinue) Fail("the title offers no Continue (" + what + ")");
            if (!_menu.Press(MainMenuView.ContinueId)) Fail("no Continue button");
            yield return Until(() => Flow.State == JoinState.InGame, what);
        }

        /// <summary>One press of goal board row <paramref name="row"/>: waits for the server's answer and returns it in <paramref name="answer"/>.</summary>
        private IEnumerator Press(PickupInteractor hands, Station board, int row, Action<CookOutcome?> answer)
        {
            int before = hands.ResultVersion;
            hands.DevSendStation(board.Id, row);
            yield return Until(() => hands.ResultVersion != before, "the goal board answered row " + row, 10f);
            answer(hands.LastCookOutcome);
        }

        private IEnumerator EndService(string what)
        {
            EconomyService economy = _bootstrap.NetworkManager.GetComponent<EconomyService>();
            if (economy != null)
            {
                long hour = Shift.HourOf(economy.Tick);
                if (hour < Shift.CloseHour) economy.SkipHours((int)(Shift.CloseHour - hour));
            }
            TicketRailView rail = FindFirstObjectByType<TicketRailView>();
            yield return Until(() => rail != null && rail.SummaryOpen.Value, what);
            yield return Wait(1.5f);
        }

        private IEnumerator Run()
        {
            yield return Wait(3f);
            yield return _capture.Shot("title");
            yield return ContinueToShop("continued the food corner (the fixture)");
            if (Flow.State != JoinState.InGame) yield break;
            PlayerController me = null;
            yield return Until(() => (me = FindOwnPlayer()) != null, "own player");
            if (me == null) yield break;
            FindFirstObjectByType<PlayerInputReader>()?.DisableForScript();
            yield return Wait(3f);
            var intents = new ScriptedIntentSource();
            me.IntentSource = intents;
            var actor = new TourActor(me, intents);
            PickupInteractor hands = actor.Interactor;
            ShopStage stage = ShopStage.Find();
            SaveService saves = _bootstrap.SaveService;
            if (stage == null || stage.StageNow != ShopStageRules.FoodCorner) Fail("the fixture did not load as the food corner");
            Station board = Worktown.Game.Layout.LayoutRegistry.First(StationKind.GoalBoard);
            if (board == null)
            {
                Fail("no goal board in the kitchen (the v1 fixture's layout should have it after the migration)");
                yield break;
            }
            Debug.Log("[Tour] restaurant: the fixture's goal " + EconomyRules.Money(stage.GoalMoney.Value) + " (crew " + stage.CrewPlayers.Value
                + "), the wallet " + EconomyRules.Money(Balance(me)));

            // The goal board on the back wall: from the aisle in front of it.
            float laneZ = TourRules.AisleZ;
            yield return actor.WalkTo(new Vector3(board.transform.position.x, 0f, laneZ), false, false);
            yield return actor.AimAt(board.transform.position, 1f);
            yield return Wait(0.5f);
            yield return _capture.Shot("corner-goal-board");

            // Rent the restaurant: the first press arms, the second pays (the service is open: the move waits for its end).
            CookOutcome? answer = null;
            yield return Press(hands, board, GoalBoardRules.StepRow, a => answer = a);
            if (answer != CookOutcome.ConfirmAgain) Fail("the first press answered " + answer + ", not ConfirmAgain");
            yield return _capture.Shot("rent-armed");
            long beforeRent = Balance(me);
            yield return Press(hands, board, GoalBoardRules.StepRow, a => answer = a);
            if (answer != CookOutcome.Ok) Fail("the second press answered " + answer + ", not Ok");
            yield return Until(() => Balance(me) < beforeRent, "the rent goal left the wallet");
            yield return Until(() => stage.MovePending.Value || stage.StageNow == ShopStageRules.FirstRestaurant, "the move is pending");
            yield return Wait(0.5f);
            yield return _capture.Shot("rent-paid-move-waits");

            // The service ends: the move, the covers gone.
            yield return EndService("the corner's service ended (the move)");
            yield return Until(() => stage.StageNow == ShopStageRules.FirstRestaurant, "moved into the restaurant");
            yield return _capture.Shot("summary-moving-day");
            // Buy the falafel wrap (two presses) during the summary, so the next service's frozen menu has it (the player is at the board).
            yield return actor.AimAt(board.transform.position, 1f);
            yield return Wait(0.5f);
            yield return _capture.Shot("restaurant-goal-board");
            int falafelRow = RestaurantMenu.RowOf(stage.Climb, "dish_falafel_wrap") + 1;
            yield return Press(hands, board, falafelRow, a => answer = a);
            if (answer != CookOutcome.ConfirmAgain) Fail("the recipe's first press answered " + answer);
            yield return Press(hands, board, falafelRow, a => answer = a);
            if (answer != CookOutcome.Ok) Fail("buying the falafel wrap answered " + answer);
            yield return Until(() => RestaurantMenu.Has(stage.RecipesOwned.Value, falafelRow - 1), "the falafel wrap is owned");
            yield return Wait(0.5f);
            yield return _capture.Shot("recipe-bought");

            // The covers are gone.
            yield return actor.LevelView();
            yield return actor.Look(-60f, -10f, 0.8f);
            yield return _capture.Shot("covers-gone-left");
            yield return actor.Look(120f, 0f, 1.2f);
            yield return _capture.Shot("covers-gone-right");
            foreach (Station s in FindObjectsByType<Station>(FindObjectsSortMode.None))
                if (!ShopStageRules.IsStationOpen(stage.StageNow, s.Kind)) Fail(s.DisplayName + " is still closed at the restaurant");
            if (saves == null || saves.Current == null || saves.Current.climb.stage != ShopStageRules.FirstRestaurant)
                Fail("the move was not saved");

            // A terminal order (the restaurant orders its stock).
            Station terminal = Worktown.Game.Layout.LayoutRegistry.First(StationKind.Terminal);
            if (terminal != null)
            {
                yield return actor.WalkTo(new Vector3(terminal.transform.position.x, 0f, laneZ), false, false);
                yield return actor.AimAt(terminal.transform.position, 0.8f);
                int line = StockCatalog.IndexOf("wrap");
                if (line < 0) line = StockCatalog.IndexOf(FoodCornerRules.StockGood);
                int before = hands.ResultVersion;
                hands.DevSendStation(terminal.Id, TerminalRules.OrderOption(line, 1));
                yield return Until(() => hands.ResultVersion != before, "the terminal answered", 10f);
                Debug.Log("[Tour] restaurant: the terminal order of " + StockCatalog.Goods[line].Id + " answered " + hands.LastCookOutcome);
                if (hands.LastCookOutcome != CookOutcome.Ok) Fail("the terminal order answered " + hands.LastCookOutcome);
                yield return _capture.Shot("terminal-order");
            }
            else Fail("no ordering terminal");

            // The next service opens with the wrap on the frozen menu: meal guests come for it.
            yield return Until(() => rail().SummaryOpen.Value == false && RestaurantMenu.Has(stage.MenuNow.Value, falafelRow - 1),
                "the next service opened with the falafel wrap on the menu", 40f);
            yield return Until(() => AnyGuestWants(Menu.FalafelWrap), "a guest orders the falafel wrap", 45f);
            yield return actor.LevelView();
            yield return actor.Look(180f, 0f, 1.2f);
            yield return _capture.Shot("restaurant-guests");
            foreach (Customer c in FindObjectsByType<Customer>(FindObjectsSortMode.None))
                if (c.Dish.Value != Menu.Fries && c.Dish.Value != Menu.FalafelWrap) Fail("a guest ordered " + Menu.NameOf(c.Dish.Value) + ", not on the menu");

            // The service ends: the summary with the rent and the gate line.
            yield return EndService("the restaurant's first service ended");
            yield return Wait(1f);
            yield return _capture.Shot("summary-rent-gate");
            Debug.Log("[Tour] restaurant: owed " + EconomyRules.Money(stage.RentOwed.Value) + ", window " + stage.WindowCount.Value + " service(s), average "
                + GateRules.Stars(stage.WindowAverageTenths.Value) + ", the wallet " + EconomyRules.Money(Balance(me)));

            // Close the shop and Continue: still the restaurant with its recipe.
            _bootstrap.Leave();
            yield return Until(() => Flow.State == JoinState.Menu && !_bootstrap.IsRunning, "the shop closed, back on the title screen");
            yield return Wait(1.5f);
            yield return _capture.Shot("title-continue");
            yield return ContinueToShop("continued the restaurant");
            if (Flow.State != JoinState.InGame) yield break;
            yield return Until(() => FindOwnPlayer() != null, "own player again");
            FindFirstObjectByType<PlayerInputReader>()?.DisableForScript();
            yield return Wait(3f);
            stage = ShopStage.Find();
            if (stage == null || stage.StageNow != ShopStageRules.FirstRestaurant) Fail("Continue did not bring back the restaurant");
            if (stage != null && !RestaurantMenu.Has(stage.RecipesOwned.Value, falafelRow - 1)) Fail("Continue lost the falafel wrap recipe");
            saves = _bootstrap.SaveService;
            if (saves != null && saves.Current != null && saves.Current.shopName != GoalBoardRules.RestaurantName)
                Fail("the shop's name is '" + saves.Current.shopName + "'");
            yield return _capture.Shot("continued-restaurant");
            _bootstrap.Leave();
            yield return Until(() => Flow.State == JoinState.Menu && !_bootstrap.IsRunning, "closed again");
            yield return Wait(1f);
            yield return _capture.Shot("title-end");
        }

        private static TicketRailView rail() => FindFirstObjectByType<TicketRailView>();

        private static bool AnyGuestWants(byte dish)
        {
            foreach (Customer c in FindObjectsByType<Customer>(FindObjectsSortMode.None))
                if (c.Dish.Value == dish) return true;
            return false;
        }

        private static long Balance(PlayerController me) =>
            me != null && me.TryGetComponent(out Wallet wallet) ? wallet.BalanceOrUnknown : -1L;

        private static PlayerController FindOwnPlayer()
        {
            foreach (PlayerController p in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                if (p != null && p.IsOwner) return p;
            return null;
        }

        private IEnumerator End()
        {
            _finished = true;
            string sheet = null;
            yield return _capture.WriteSheet(TourRules.SheetColumns, path => sheet = path);
            Finish(_failures > 0 ? 1 : 0, "restaurant tour: " + _capture.Count + " images, sheet " + (sheet ?? "none") + ", failures " + _failures);
        }

        private void Finish(int code, string message)
        {
            _finished = true;
            if (code == 0) Debug.Log("[Tour] done: " + message);
            else Debug.LogError("[Tour] failed: " + message);
            Application.Quit(code);
        }
    }
}
