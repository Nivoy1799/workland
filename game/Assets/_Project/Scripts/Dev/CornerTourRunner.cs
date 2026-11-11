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
using Worktown.Game.UI;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 053 (AC10), development builds only: the food corner tour of the visual check (<c>-tour corner -save-dir dir</c>; one window,
    /// it is its own host). From the title screen (no Continue: the tour's save folder starts empty) it presses New shop and shoots the
    /// corner with its covers and the HUD's goal line; a closed station refuses (the prompt and the server's NotOpenYet); it cooks and
    /// serves two fries guests who pay cash (the cook tour's own fries part); the clock moves to 14:00, so the service ends: the summary
    /// with the goal line, the pitch fee and the save; Close the shop; the title shows Continue with its line; Continue; the balance and
    /// the day are the saved ones. Shots and the sheet go to <c>tour-out/corner</c>; quits with 0, or 1 if a step failed.
    /// </summary>
    public sealed class CornerTourRunner : MonoBehaviour
    {
        private const float StepLimit = 60f;
        private const float WholeLimit = 600f;

        private NetBootstrap _bootstrap;
        private MainMenuView _menu;
        private TourCapture _capture;
        private int _timeouts;
        private bool _finished;
        private float _deadline;
        private int _framesRunning;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isBatchMode) return;
            LaunchArgs args = LaunchArgs.Parse(Environment.GetCommandLineArgs());
            if (args.Tour != LaunchArgs.CornerTour) return;
            Application.runInBackground = true;
            var go = new GameObject("CornerTourRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<CornerTourRunner>();
        }

        private IEnumerator Start()
        {
            LaunchArgs args = LaunchArgs.Parse(Environment.GetCommandLineArgs());
            string root = args.TourOut.Length > 0 ? args.TourOut : Path.Combine(Application.persistentDataPath, "visual");
            string dir = Path.Combine(root, LaunchArgs.CornerTour);
            _capture = new TourCapture(dir, TourRules.SheetCell);
            _deadline = Time.unscaledTime + WholeLimit;
            Debug.Log("[Tour] corner, images to " + dir + ", saves in " + args.SaveDir);
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
            _timeouts++;
            Debug.LogError("[Tour] corner: " + what);
        }

        private IEnumerator Until(Func<bool> done, string what)
        {
            float limit = Time.unscaledTime + StepLimit;
            while (!done())
            {
                if (Time.unscaledTime > limit)
                {
                    Fail("timed out waiting for " + what);
                    yield break;
                }
                yield return null;
            }
            Debug.Log("[Tour] corner: " + what);
        }

        private static IEnumerator Wait(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
        }

        private void Press(string id)
        {
            if (!_menu.Press(id)) Fail("no button '" + id + "'");
        }

        private IEnumerator Frames(string name, float seconds)
        {
            _framesRunning++;
            yield return _capture.Frames(name, Mathf.Max(1, Mathf.RoundToInt(seconds / 0.1f)), 0.1f);
            _framesRunning--;
        }

        private IEnumerator Run()
        {
            yield return Wait(3f);
            yield return _capture.Shot("title");
            if (_menu.HasContinue) Fail("the tour's empty save folder offers Continue");

            // New shop: a new food corner and its own save file.
            Press(MainMenuView.NewShopId);
            yield return Until(() => Flow.State == JoinState.InGame, "the new food corner is open");
            if (Flow.State != JoinState.InGame) yield break;
            PlayerController me = null;
            yield return Until(() => (me = FindOwnPlayer()) != null, "own player");
            if (me == null) yield break;
            FindFirstObjectByType<PlayerInputReader>()?.DisableForScript();
            yield return Wait(3f);
            var intents = new ScriptedIntentSource();
            me.IntentSource = intents;
            var actor = new TourActor(me, intents);
            SaveService saves = _bootstrap.SaveService;
            ShopStage stage = ShopStage.Find();
            if (stage == null || stage.StageNow != ShopStageRules.FoodCorner) Fail("the new shop is not the food corner");
            if (saves == null || saves.Current == null || saves.Written < 1) Fail("New shop wrote no save file");
            yield return actor.LevelView();
            yield return _capture.Shot("corner-hud-goal");
            Debug.Log("[Tour] corner: HUD goal " + HudView.GoalText(stage != null ? stage.StageNow : (byte)0, Balance(me),
                stage != null ? stage.GoalMoney.Value : 0, stage != null && stage.GoalReached.Value));

            Station stove = null, fryer = null;
            foreach (Station s in FindObjectsByType<Station>(FindObjectsSortMode.None))
            {
                if (s.Kind == StationKind.Stove && stove == null) stove = s;
                if (s.Kind == StationKind.Fryer) fryer = s;
            }
            float laneZ = stove != null ? stove.transform.position.z - TourRules.AisleOffset : TourRules.AisleZ;

            // The covers along the back line, and a closed station's refusal (the prompt, then the server's answer).
            if (stove != null)
            {
                yield return actor.WalkTo(new Vector3(stove.transform.position.x, 0f, laneZ), false, false);
                yield return actor.AimAt(stove.transform.position + Vector3.up * 0.4f, 1f);
                yield return Wait(0.5f);
                PickupInteractor hands = actor.Interactor;
                if (!hands.AimClosed) Fail("the stove under its cover is not shown as closed");
                yield return _capture.Shot("closed-stove-prompt");
                hands.DevSendStation(stove.Index, 0);
                yield return Until(() => hands.LastCookOutcome == CookOutcome.NotOpenYet, "the stove refused: " + ShopStageRules.NotOpenText);
                yield return Wait(0.3f);
                yield return _capture.Shot("closed-stove-refused");
                yield return actor.LevelView();
                yield return actor.Look(-60f, -10f, 0.8f);
                yield return _capture.Shot("covers-left");
                yield return actor.Look(120f, 0f, 1.2f);
                yield return _capture.Shot("covers-right");
                yield return actor.LevelView();
            }
            long startBalance = Balance(me);

            // Two fries guests, cooked and paid in cash (the cook tour's fries part).
            var capture = new CookCapture(shot => _capture.Shot(shot), (name, seconds) => Frames(name, seconds), _ => { }, () => _framesRunning > 0);
            for (int guest = 1; guest <= 2; guest++)
            {
                if (guest == 2 && fryer != null && fryer.TryGetComponent(out StationContents contents) && FryerRules.IsOn(contents.StoveFlags.Value))
                {
                    // The part switches the fryer on itself, so it goes off first (from the fryer: the last step ended at the register).
                    yield return actor.WalkTo(new Vector3(fryer.transform.position.x, 0f, laneZ), false, false);
                    actor.Interactor.DevSendStation(fryer.Index, FryerRules.PowerOption);
                    yield return Wait(0.6f);
                    if (FryerRules.IsOn(contents.StoveFlags.Value)) Fail("the fryer did not switch off between the two guests");
                }
                Debug.Log("[Tour] corner: fries for guest " + guest);
                yield return CookSequence.FryFries(actor, capture, laneZ);
                yield return actor.LevelView();
            }
            if (actor.Timeouts > 0) Fail(actor.Timeouts + " step(s) of the fries timed out");
            long served = Balance(me);
            Debug.Log("[Tour] corner: the shop wallet went from " + EconomyRules.Money(startBalance) + " to " + EconomyRules.Money(served));
            if (served <= startBalance) Fail("the two fries guests paid nothing into the shop wallet");
            yield return _capture.Shot("after-two-guests");

            // The service ends (the clock to 14:00): the summary with the goal, the pitch fee, the save.
            EconomyService economy = _bootstrap.NetworkManager.GetComponent<EconomyService>();
            int savedBefore = saves != null ? saves.Written : 0;
            if (economy != null)
            {
                long hour = Shift.HourOf(economy.Tick);
                if (hour < Shift.CloseHour) economy.SkipHours((int)(Shift.CloseHour - hour));
            }
            TicketRailView rail = FindFirstObjectByType<TicketRailView>();
            yield return Until(() => rail != null && rail.SummaryOpen.Value, "the service ended (its summary shows)");
            yield return Wait(1.5f);
            yield return _capture.Shot("service-summary-goal");
            if (saves == null || saves.Written <= savedBefore) Fail("the service end saved nothing");

            // Close the shop: saved again; the title offers Continue with its line.
            long closingBalance = saves != null ? saves.LastSavedBalance : -1;
            _bootstrap.Leave();
            yield return Until(() => Flow.State == JoinState.Menu && !_bootstrap.IsRunning, "the shop closed, back on the title screen");
            yield return Wait(1.5f);
            long savedBalance = saves != null ? saves.LastSavedBalance : -1;
            int savedDay = saves != null ? saves.LastSavedDay : 0;
            Debug.Log("[Tour] corner: saved at the close: day " + savedDay + ", " + EconomyRules.Money(savedBalance) + " (at the service end "
                + EconomyRules.Money(closingBalance) + "); Continue reads '" + _menu.ContinueText + "'");
            if (!_menu.HasContinue) Fail("the title offers no Continue after the close");
            if (_menu.ContinueText != MenuRules.ContinueLine(savedDay, savedBalance)) Fail("the Continue line is not the saved day and balance");
            yield return _capture.Shot("title-continue");

            // Continue: the same balance and day.
            Press(MainMenuView.ContinueId);
            yield return Until(() => Flow.State == JoinState.InGame, "continued");
            if (Flow.State != JoinState.InGame) yield break;
            yield return Until(() => (me = FindOwnPlayer()) != null, "own player again");
            FindFirstObjectByType<PlayerInputReader>()?.DisableForScript();
            yield return Wait(3f);
            saves = _bootstrap.SaveService;
            if (saves == null || saves.LoadedBalance != savedBalance || saves.LoadedDay != savedDay)
                Fail("Continue applied day " + (saves != null ? saves.LoadedDay : -1) + ", " + EconomyRules.Money(saves != null ? saves.LoadedBalance : -1)
                    + " instead of the saved day " + savedDay + ", " + EconomyRules.Money(savedBalance));
            MarketBoard board = FindFirstObjectByType<MarketBoard>();
            string clock = board != null ? board.Clock.Value : string.Empty;
            if (!clock.StartsWith("Day " + savedDay + ",", StringComparison.Ordinal)) Fail("the clock reads '" + clock + "', not day " + savedDay);
            Debug.Log("[Tour] corner: continued at '" + clock + "', the shop wallet " + EconomyRules.Money(Balance(me))
                + " (the saved balance less the service start's stock drop)");
            yield return _capture.Shot("continued");
            _bootstrap.Leave();
            yield return Until(() => Flow.State == JoinState.Menu && !_bootstrap.IsRunning, "closed again");
            yield return Wait(1f);
            yield return _capture.Shot("title-end");
        }

        private static long Balance(PlayerController me) =>
            me != null && me.TryGetComponent(out Wallet wallet) ? wallet.Balance.Value : -1L;

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
            Finish(_timeouts > 0 ? 1 : 0, "corner tour: " + _capture.Count + " images, sheet " + (sheet ?? "none") + ", failures " + _timeouts);
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
