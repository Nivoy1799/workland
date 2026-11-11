using System;
using System.Collections;
using System.IO;
using FishNet.Connection;
using FishNet.Managing;
using UnityEngine;
using UnityEngine.InputSystem;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;
using Worktown.Game.Player;
using Worktown.Game.Settings;
using Worktown.Game.UI;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 047, development builds only: the menu tour of the visual check (scripts/visual-check.sh --tours menu). Two windows:
    /// <list type="bullet">
    /// <item><c>-tour menu</c> starts on the title screen (no mode flag) and goes through it by pressing its buttons (MainMenuView.Press,
    /// the same actions as clicks): the title, Join by address, the loading screen, the shop, the lobby panel; the host kicks it (the
    /// error screen; before that a dropped connection: the rejoin card and back in); back to the menu; then it hosts its own shop (New
    /// shop: the loading screen, the host's lobby) and closes it.</item>
    /// <item><c>-host -observe menu</c> is the host it joins: once the tour player is in, it shows its own lobby (the Kick buttons),
    /// drops the tour player's connection without a notice (the tour shows the rejoin card and gets back in by itself), then kicks it
    /// and quits (freeing the port for the tour's own shop).</item>
    /// </list>
    /// Shots and the sheet go to <c>tour-out/menu</c> and <c>tour-out/menu-observer</c>; quits with 0, or 1 if a step timed out.
    /// </summary>
    public sealed class MenuTourRunner : MonoBehaviour
    {
        private const float StepLimit = 60f;
        private const float WholeLimit = 300f;

        private bool _host;
        private NetBootstrap _bootstrap;
        private MainMenuView _menu;
        private LobbyView _lobby;
        private SettingsView _settings;
        private TourCapture _capture;
        private int _timeouts;
        private bool _finished;
        private float _deadline;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isBatchMode) return;
            LaunchArgs args = LaunchArgs.Parse(Environment.GetCommandLineArgs());
            bool tour = args.Tour == LaunchArgs.MenuTour;
            bool host = args.Observe == LaunchArgs.MenuTour && args.Mode == LaunchMode.Host;
            if (!tour && !host) return;
            Application.runInBackground = true;
            var go = new GameObject("MenuTourRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<MenuTourRunner>()._host = host;
        }

        private IEnumerator Start()
        {
            LaunchArgs args = LaunchArgs.Parse(Environment.GetCommandLineArgs());
            string root = args.TourOut.Length > 0 ? args.TourOut : Path.Combine(Application.persistentDataPath, "visual");
            string dir = Path.Combine(root, LaunchArgs.MenuTour + (_host ? "-observer" : string.Empty));
            _capture = new TourCapture(dir, TourRules.SheetCell);
            _deadline = Time.unscaledTime + WholeLimit;
            Debug.Log("[Tour] menu " + (_host ? "host" : "tour") + ", images to " + dir);
            yield return null;
            _bootstrap = FindFirstObjectByType<NetBootstrap>();
            _menu = MainMenuView.Find();
            _lobby = LobbyView.Find();
            _settings = SettingsView.Find();
            if (_bootstrap == null || _menu == null || _lobby == null)
            {
                Finish(1, "no NetBootstrap, MainMenuView or LobbyView in the scene");
                yield break;
            }
            yield return _host ? RunHost() : RunTour();
            yield return End();
        }

        private void Update()
        {
            if (_finished || Time.unscaledTime <= _deadline) return;
            _finished = true;
            StopAllCoroutines();
            _timeouts++;
            Debug.LogError("[Tour] menu: the whole tour took longer than " + WholeLimit + " s");
            StartCoroutine(End());
        }

        private JoinFlow Flow => _bootstrap.Flow;

        /// <summary>Waits until <paramref name="done"/> (true) or <see cref="StepLimit"/> (false, one timeout counted).</summary>
        private IEnumerator Until(Func<bool> done, string what, Action<bool> result = null)
        {
            float limit = Time.unscaledTime + StepLimit;
            while (!done())
            {
                if (Time.unscaledTime > limit)
                {
                    _timeouts++;
                    Debug.LogError("[Tour] menu: timed out waiting for " + what);
                    result?.Invoke(false);
                    yield break;
                }
                yield return null;
            }
            Debug.Log("[Tour] menu: " + what);
            result?.Invoke(true);
        }

        private void Press(string id)
        {
            if (!_menu.Press(id))
            {
                _timeouts++;
                Debug.LogError("[Tour] menu: no button '" + id + "'");
            }
        }

        private static IEnumerator Wait(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
        }

        // ---- The tour client: starts on the title screen ----

        private IEnumerator RunTour()
        {
            yield return Wait(3f);
            yield return _capture.Shot("title");
            yield return SettingsPages();
            Press(MainMenuView.JoinAddressId);
            _menu.SetAddress("127.0.0.1:" + LaunchArgs.DefaultTugboatPort);
            yield return Wait(0.5f);
            yield return _capture.Shot("join-by-address");

            // The host window starts 3 s after this one: join until it answers.
            yield return Wait(6f);
            bool inShop = false, errorShot = false;
            float limit = Time.unscaledTime + 90f;
            while (!inShop && Time.unscaledTime < limit)
            {
                Press(MainMenuView.ConnectAddressId);
                yield return _capture.Frames("joining", 4, 0.1f);
                yield return Until(() => Flow.State == JoinState.InGame || Flow.State == JoinState.Message, "joined or refused");
                if (Flow.State == JoinState.InGame)
                {
                    inShop = true;
                    break;
                }
                if (!errorShot)
                {
                    errorShot = true;
                    yield return _capture.Shot("could-not-connect");
                }
                Press(MainMenuView.DismissId);
                Press(MainMenuView.JoinAddressId);
                yield return Wait(2f);
            }
            if (!inShop)
            {
                _timeouts++;
                Debug.LogError("[Tour] menu: never got into the host's shop");
                yield break;
            }
            DisableInput();
            yield return Wait(3f);
            yield return _capture.Shot("in-the-shop");
            _lobby.SetOpen(true);
            yield return Wait(1f);
            yield return _capture.Shot("lobby-guest");

            // The host drops this connection without a word (a lost connection): the rejoin card, then back in the shop.
            yield return Until(() => Flow.State == JoinState.Rejoining, "the connection dropped");
            yield return _capture.Shot("rejoining");
            yield return Until(() => Flow.State == JoinState.InGame, "rejoined");
            DisableInput();
            yield return Wait(2f);
            yield return _capture.Shot("rejoined");

            // The host kicks this player: the error screen.
            yield return Until(() => Flow.State == JoinState.Message, "the host's kick");
            yield return Wait(0.5f);
            yield return _capture.Shot("kicked");
            if (Flow.Reason != DisconnectReason.Kicked)
            {
                _timeouts++;
                Debug.LogError("[Tour] menu: expected Kicked, got " + Flow.Reason);
            }
            Press(MainMenuView.DismissId);
            yield return Wait(1f);
            yield return _capture.Shot("menu-again");

            // The host window quits a few seconds after the kick; then this one opens its own shop.
            yield return Wait(10f);
            Press(MainMenuView.NewShopId);
            yield return _capture.Frames("opening", 4, 0.1f);
            yield return Until(() => Flow.State == JoinState.InGame, "own shop open");
            if (Flow.State != JoinState.InGame) yield break;
            DisableInput();
            yield return Wait(3f);
            _lobby.SetOpen(true);
            yield return Wait(1f);
            yield return _capture.Shot("lobby-host");
            _lobby.SetOpen(false);
            yield return SettingsInShop();
            _bootstrap.Leave();
            yield return Until(() => Flow.State == JoinState.Menu && !_bootstrap.IsRunning, "shop closed, back on the title screen");
            yield return Wait(1.5f);
            yield return _capture.Shot("closed-to-menu");
        }

        // ---- Spec 048: the settings screen ----

        /// <summary>
        /// From the title screen: every page, a rebind that swaps (Jump onto E, Use's key), the comfort page applied live (UI scale 125 %,
        /// the colour-blind ring colours), each page reset, Back to the title. A scripted run never stores settings: checked first.
        /// </summary>
        private IEnumerator SettingsPages()
        {
            if (SettingsApplier.Persistent)
            {
                _timeouts++;
                Debug.LogError("[Tour] menu: the settings are persistent in a scripted run (they would change this PC's saved settings)");
            }
            Press(MainMenuView.SettingsId);
            yield return Wait(0.5f);
            if (_settings == null || !_settings.IsOpen)
            {
                _timeouts++;
                Debug.LogError("[Tour] menu: Settings did not open the settings screen");
                yield break;
            }
            yield return _capture.Shot("settings-graphics");
            _settings.ShowPage(SettingsPage.Audio);
            yield return Wait(0.3f);
            yield return _capture.Shot("settings-audio");
            _settings.ShowPage(SettingsPage.Controls);
            yield return Wait(0.3f);
            yield return _capture.Shot("settings-controls");
            // Follow-up 2026-11-11 (spec 048): the Controls page fits at 1280x720 (two binding columns), so nothing needs scrolling.
            float overflow = _settings.ScrollOverflow;
            Debug.Log("[Tour] menu: the controls page reaches " + overflow.ToString("0") + " px past the visible area at "
                + Screen.width + "x" + Screen.height);
            if (overflow > 1f)
            {
                _timeouts++;
                Debug.LogError("[Tour] menu: the controls page still needs scrolling at " + Screen.width + "x" + Screen.height);
            }
            _settings.ScrollTo(1f);
            if (!_settings.Rebind(ControlAction.Jump, Key.E) || GameSettings.CurrentBindings.KeyOf(ControlAction.Use) != Key.Space)
            {
                _timeouts++;
                Debug.LogError("[Tour] menu: Jump onto E did not swap with Use");
            }
            yield return Wait(0.3f);
            yield return _capture.Shot("settings-rebind-swap");
            SettingsPress(SettingsView.ResetId);
            _settings.ScrollTo(0f);
            _settings.ShowPage(SettingsPage.Comfort);
            yield return Wait(0.3f);
            yield return _capture.Shot("settings-comfort");
            GameSettings live = GameSettings.Current.Clone();
            live.UiScale = 1.25f;
            live.ColourBlindRings = true;
            GameSettings.Use(live);
            yield return Wait(0.5f);
            yield return _capture.Shot("settings-comfort-live");
            SettingsPress(SettingsView.ResetId);
            yield return Wait(0.3f);
            SettingsPress(SettingsView.BackId);
            yield return Wait(0.3f);
            if (_settings.IsOpen || _menu.CurrentPage != "Title")
            {
                _timeouts++;
                Debug.LogError("[Tour] menu: Back did not return to the title (" + _menu.CurrentPage + ")");
            }
        }

        /// <summary>In the tour's own shop: the settings screen over it (what Esc opens), then the world labels off and on.</summary>
        private IEnumerator SettingsInShop()
        {
            if (_settings == null) yield break;
            _settings.Open(true);
            yield return Wait(0.5f);
            yield return _capture.Shot("settings-in-shop");
            SettingsPress(SettingsView.BackId);
            yield return Wait(0.5f);
            yield return _capture.Shot("labels-on");
            GameSettings off = GameSettings.Current.Clone();
            off.Labels = false;
            GameSettings.Use(off);
            yield return Wait(0.5f);
            yield return _capture.Shot("labels-off");
            GameSettings on = GameSettings.Current.Clone();
            on.Labels = true;
            GameSettings.Use(on);
        }

        private void SettingsPress(string id)
        {
            if (!_settings.Press(id))
            {
                _timeouts++;
                Debug.LogError("[Tour] menu: no settings button '" + id + "'");
            }
        }

        // ---- The host: kicks the tour player ----

        private IEnumerator RunHost()
        {
            yield return Until(() => Flow.State == JoinState.InGame, "host in its shop");
            DisableInput();
            yield return Until(() => _bootstrap.Join != null && _bootstrap.Join.PlayerCount >= 2, "the tour player joined");
            yield return Wait(7f); // the tour shoots the shop and its lobby panel meanwhile
            _lobby.SetOpen(true);
            yield return Wait(1f);
            yield return _capture.Shot("lobby-host-with-guest");
            // A lost connection: the server drops the tour player without a notice; it rejoins by itself within 30 s.
            int dropped = RemoteClient(_bootstrap.NetworkManager);
            if (dropped < 0 || !_bootstrap.NetworkManager.ServerManager.Clients.TryGetValue(dropped, out NetworkConnection drop))
            {
                _timeouts++;
                Debug.LogError("[Tour] menu: no tour player to drop");
                yield break;
            }
            Debug.Log("[Tour] menu: dropping player " + dropped + " (no notice)");
            drop.Disconnect(true);
            yield return Until(() => _bootstrap.Join.PlayerCount < 2, "the tour player dropped");
            yield return Until(() => _bootstrap.Join.PlayerCount >= 2, "the tour player rejoined");
            yield return Wait(5f);
            yield return _capture.Shot("guest-rejoined");
            int guest = RemoteClient(_bootstrap.NetworkManager);
            if (guest < 0 || !_lobby.PressKick(guest))
            {
                _timeouts++;
                Debug.LogError("[Tour] menu: could not kick the tour player");
                yield break;
            }
            yield return Wait(1.5f);
            yield return _capture.Shot("after-kick");
            yield return Wait(2f);
        }

        private static int RemoteClient(NetworkManager nm)
        {
            if (nm == null) return -1;
            foreach (NetworkConnection connection in nm.ServerManager.Clients.Values)
                if (connection != null && connection.IsActive && !connection.IsLocalClient) return connection.ClientId;
            return -1;
        }

        /// <summary>Spec 045: scripted roles listen to no devices.</summary>
        private static void DisableInput() => FindFirstObjectByType<PlayerInputReader>()?.DisableForScript();

        private IEnumerator End()
        {
            _finished = true;
            string sheet = null;
            yield return _capture.WriteSheet(TourRules.SheetColumns, path => sheet = path);
            Finish(_timeouts > 0 ? 1 : 0, "menu " + (_host ? "host" : "tour") + ": " + _capture.Count + " images, sheet " + (sheet ?? "none")
                + ", timeouts " + _timeouts);
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
