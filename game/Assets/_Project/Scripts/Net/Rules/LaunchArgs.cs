using System.Collections.Generic;
using Worktown.Game.Player;

namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Parses the command line into one start mode. Unknown arguments (Unity's own, like
    /// -batchmode or -logFile) are ignored. More than one mode flag, or a flag without its
    /// value, is an error and falls back to <see cref="LaunchMode.Menu"/>.
    /// </summary>
    public sealed class LaunchArgs
    {
        public const ushort DefaultTugboatPort = 7770;
        /// <summary>Spec 047: the menu tour (Dev/MenuTourRunner) starts on the title screen, so it takes no mode flag.</summary>
        public const string MenuTour = "menu";
        /// <summary>Spec 053: the food corner tour (Dev/CornerTourRunner) starts on the title screen too, with <c>-save-dir</c>.</summary>
        public const string CornerTour = "corner";
        /// <summary>Spec 047: the highest <c>-max-players</c>.</summary>
        public const int MaxPlayersLimit = 64;

        public LaunchMode Mode { get; private set; } = LaunchMode.Menu;
        public string Address { get; private set; } = string.Empty;
        public ushort Port { get; private set; } = DefaultTugboatPort;
        public string SteamTarget { get; private set; } = string.Empty;
        /// <summary><c>-latency ms</c>: simulated latency for testing prediction (spec 010 AC2). 0 = off.</summary>
        public int LatencyMs { get; private set; }
        /// <summary>Spec 018: <c>-view first|third</c>, the start view for screenshots and tests. Null = not given.</summary>
        public ViewMode? View { get; private set; }
        /// <summary>Spec 027 (dev builds): <c>-tour name</c>, the scripted visual check this client runs. Empty = none.
        /// Only valid with <c>-connect</c>.</summary>
        public string Tour { get; private set; } = string.Empty;
        /// <summary>Spec 027: <c>-observe name</c>, watch that tour from its fixed observer camera. Empty = none.
        /// Valid with <c>-connect</c> or <c>-host</c>.</summary>
        public string Observe { get; private set; } = string.Empty;
        /// <summary>Spec 027: <c>-tour-out dir</c>, where the tour and the observer write their images. Empty = default.</summary>
        public string TourOut { get; private set; } = string.Empty;
        /// <summary>Spec 045 (the spec 004 load test, dev builds): <c>-bot</c>, drive the own player with a load-test bot. Needs <c>-connect</c>.</summary>
        public bool IsBot { get; private set; }
        /// <summary>Spec 045: <c>-seed n</c>, the bot's random seed, any int including negative values (0 when not given).</summary>
        public int Seed { get; private set; }
        /// <summary>Spec 045: <c>-metrics path</c>, one JSON line per second there (a server or host: its load; a bot: what it received).
        /// Empty = none.</summary>
        public string MetricsPath { get; private set; } = string.Empty;
        /// <summary>Spec 047: <c>-max-players n</c> (1..64), the places of a server or host (the spec 004 load test: 16 bots and the host);
        /// 0 = not given (JoinRules.MaxPlayers, D36).</summary>
        public int MaxPlayers { get; private set; }
        /// <summary>Spec 049: <c>-crash-log path</c>, where the crash and exception log goes (Diagnostics.CrashLog). Empty = the default
        /// (the player's data folder). Valid in every mode.</summary>
        public string CrashLogPath { get; private set; } = string.Empty;
        /// <summary>Spec 049 (dev builds): <c>-crash-test</c>, force exceptions on the main thread, a task and a background thread, then
        /// quit (scripts/perf-check.sh checks the crash log). Takes no mode flag.</summary>
        public bool CrashTest { get; private set; }
        /// <summary>Spec 049 (dev builds): <c>-perf-tour path</c>, the host's own player visits every station of the scene and writes the
        /// frame times and GC allocations per station there (one JSON line each). Needs <c>-host</c>.</summary>
        public string PerfTourPath { get; private set; } = string.Empty;
        /// <summary>Spec 049 (dev builds): <c>-stall-test s</c> (1..600), stop the server's network tick after s seconds so the tick watchdog
        /// must close the shop (scripts/perf-check.sh). 0 = off. Needs <c>-host</c> or <c>-server</c>.</summary>
        public int StallTestSeconds { get; private set; }
        /// <summary>Spec 054 (dev builds): <c>-net-stats</c>, the host or server turns on FishNet's per-packet traffic statistics and its
        /// metrics line gains the pid_*/obj_* fields (Dev/NetTrafficProbe). Collection allocates: such a run is never a perf baseline.
        /// Needs <c>-host</c> or <c>-server</c>.</summary>
        public bool NetStats { get; private set; }
        /// <summary>Spec 054 (dev builds): <c>-remote-hz 30|15</c>, how often the host sends other players' positions (NetRateRules;
        /// 0 = not given: <see cref="NetRateRules.DefaultRemoteHz"/>). For the human's A/B (docs/feel.md 9). Needs <c>-host</c> or <c>-server</c>.</summary>
        public int RemoteHz { get; private set; }
        /// <summary>Spec 054 (experiment, dev builds): <c>-player-collision on|off</c>; off: players walk through each other, on the
        /// server and in every client's prediction alike (the host decides, SessionPhysics.PlayersCollide). 053 review (inbox question
        /// 23): default off, players pass through each other; <c>on</c> is the A/B.
        /// Needs <c>-host</c> or <c>-server</c>.</summary>
        public bool PlayerCollision { get; private set; } = false;
        /// <summary>Spec 054: true when <c>-player-collision</c> was given.</summary>
        public bool PlayerCollisionGiven { get; private set; }
        /// <summary>Spec 053: <c>-new-shop</c>, host a new food corner with its own save file and skip the title screen.</summary>
        public bool NewShop { get; private set; }
        /// <summary>Spec 053: <c>-continue</c>, host the newest save and skip the title screen.</summary>
        public bool ContinueShop { get; private set; }
        /// <summary>Spec 053: <c>-save-dir path</c>, the save folder instead of the player's (tests, tours). Empty = the default (none in a
        /// scripted run).</summary>
        public string SaveDir { get; private set; } = string.Empty;
        /// <summary>Spec 053: this host starts with a save (<c>-new-shop</c> or <c>-continue</c>); <c>-host</c> is the open kitchen.</summary>
        public bool HostsSave => NewShop || ContinueShop;
        /// <summary>Spec 053: a tour that starts on the title screen (no mode flag).</summary>
        public bool TitleTour => Tour == MenuTour || Tour == CornerTour;

        /// <summary>Empty when parsing succeeded.</summary>
        public string Error { get; private set; } = string.Empty;

        public bool IsValid => Error.Length == 0;

        /// <summary>
        /// Follow-up 047: a scripted run (a tour or its observer, a bot, metrics, the crash, perf or stall test). Such runs quit with
        /// their own exit code, so a host's quit is never held for the HostClosed notice there (Rules.HostQuitRules).
        /// </summary>
        public bool IsScripted => Tour.Length > 0 || Observe.Length > 0 || IsBot || MetricsPath.Length > 0 || CrashTest
            || PerfTourPath.Length > 0 || StallTestSeconds > 0;

        public static LaunchArgs Parse(IReadOnlyList<string> args)
        {
            var result = new LaunchArgs();
            int modeFlags = 0;

            for (int i = 0; i < args.Count; i++)
            {
                switch (args[i])
                {
                    case "-server":
                        modeFlags++;
                        result.Mode = LaunchMode.Server;
                        break;
                    case "-host":
                        modeFlags++;
                        result.Mode = LaunchMode.Host;
                        break;
                    case "-new-shop": // spec 053
                        modeFlags++;
                        result.Mode = LaunchMode.Host;
                        result.NewShop = true;
                        break;
                    case "-continue": // spec 053
                        modeFlags++;
                        result.Mode = LaunchMode.Host;
                        result.ContinueShop = true;
                        break;
                    case "-save-dir": // spec 053
                        if (!HasValue(args, i)) return Fail("-save-dir needs a directory");
                        result.SaveDir = args[++i];
                        break;
                    case "-connect":
                        modeFlags++;
                        if (i + 1 >= args.Count || args[i + 1].StartsWith("-"))
                            return Fail("-connect needs ip[:port]");
                        if (!TrySplitEndpoint(args[++i], out string address, out ushort port))
                            return Fail("-connect: invalid endpoint '" + args[i] + "'");
                        result.Mode = LaunchMode.ClientTugboat;
                        result.Address = address;
                        result.Port = port;
                        break;
                    case "-steam":
                        modeFlags++;
                        if (i + 1 >= args.Count || args[i + 1].StartsWith("-"))
                            return Fail("-steam needs a SteamID64");
                        if (!RichPresenceRules.TryParseSteamId(args[i + 1], out _))
                            return Fail("-steam: invalid SteamID64 '" + args[i + 1] + "'");
                        result.Mode = LaunchMode.ClientSteam;
                        result.SteamTarget = args[++i];
                        break;
                    case "-latency":
                        if (i + 1 >= args.Count || !int.TryParse(args[i + 1], out int latency) || latency < 0 || latency > 2000)
                            return Fail("-latency needs milliseconds 0..2000");
                        result.LatencyMs = latency;
                        i++;
                        break;
                    case "-view":
                        if (i + 1 >= args.Count || !ViewRules.TryParse(args[i + 1], out ViewMode view))
                            return Fail("-view needs first or third");
                        result.View = view;
                        i++;
                        break;
                    case "-tour":
                        if (!HasValue(args, i)) return Fail("-tour needs a tour name");
                        result.Tour = args[++i];
                        break;
                    case "-observe":
                        if (!HasValue(args, i)) return Fail("-observe needs a tour name");
                        result.Observe = args[++i];
                        break;
                    case "-tour-out":
                        if (!HasValue(args, i)) return Fail("-tour-out needs a directory");
                        result.TourOut = args[++i];
                        break;
                    case "-bot":
                        result.IsBot = true;
                        break;
                    case "-seed":
                        if (i + 1 >= args.Count || !int.TryParse(args[i + 1], out int seed))
                            return Fail("-seed needs an integer");
                        result.Seed = seed;
                        i++;
                        break;
                    case "-max-players":
                        if (i + 1 >= args.Count || !int.TryParse(args[i + 1], out int places) || places < 1 || places > MaxPlayersLimit)
                            return Fail("-max-players needs a number 1.." + MaxPlayersLimit);
                        result.MaxPlayers = places;
                        i++;
                        break;
                    case "-crash-log":
                        if (!HasValue(args, i)) return Fail("-crash-log needs a file path");
                        result.CrashLogPath = args[++i];
                        break;
                    case "-crash-test":
                        result.CrashTest = true;
                        break;
                    case "-perf-tour":
                        if (!HasValue(args, i)) return Fail("-perf-tour needs a file path");
                        result.PerfTourPath = args[++i];
                        break;
                    case "-stall-test":
                        if (i + 1 >= args.Count || !int.TryParse(args[i + 1], out int stall) || stall < 1 || stall > 600)
                            return Fail("-stall-test needs seconds 1..600");
                        result.StallTestSeconds = stall;
                        i++;
                        break;
                    case "-net-stats":
                        result.NetStats = true;
                        break;
                    case "-player-collision":
                        if (i + 1 >= args.Count || (args[i + 1] != "on" && args[i + 1] != "off"))
                            return Fail("-player-collision needs on or off");
                        result.PlayerCollision = args[++i] == "on";
                        result.PlayerCollisionGiven = true;
                        break;
                    case "-remote-hz":
                        if (i + 1 >= args.Count || !int.TryParse(args[i + 1], out int hz) || !NetRateRules.IsRemoteHz(hz))
                            return Fail("-remote-hz needs " + NetRateRules.TickRate + " or " + NetRateRules.DefaultRemoteHz);
                        result.RemoteHz = hz;
                        i++;
                        break;
                    case "-metrics":
                        if (!HasValue(args, i)) return Fail("-metrics needs a file path");
                        result.MetricsPath = args[++i];
                        break;
                }
            }

            if (modeFlags > 1) return Fail("only one of -server, -host, -new-shop, -continue, -connect, -steam is allowed");
            if (result.Tour.Length > 0 && result.Observe.Length > 0) return Fail("-tour and -observe exclude each other");
            // Spec 047: the menu tour starts on the title screen (no mode flag); every other tour joins with -connect.
            if (result.TitleTour && result.Mode != LaunchMode.Menu)
                return Fail("-tour " + result.Tour + " takes no -server, -host, -new-shop, -continue, -connect or -steam");
            if (result.Tour.Length > 0 && !result.TitleTour && result.Mode != LaunchMode.ClientTugboat) return Fail("-tour needs -connect");
            // Spec 053: the corner tour saves and continues, so it brings its own folder (never the player's saves).
            if (result.Tour == CornerTour && result.SaveDir.Length == 0) return Fail("-tour corner needs -save-dir");
            if (result.SaveDir.Length > 0 && result.Mode != LaunchMode.Menu && !result.HostsSave)
                return Fail("-save-dir needs the title screen, -new-shop or -continue");
            if (result.Observe.Length > 0 && result.Mode != LaunchMode.ClientTugboat && result.Mode != LaunchMode.Host)
                return Fail("-observe needs -connect or -host");
            if (result.IsBot && result.Mode != LaunchMode.ClientTugboat) return Fail("-bot needs -connect (bots use Tugboat)");
            if (result.IsBot && (result.Tour.Length > 0 || result.Observe.Length > 0)) return Fail("-bot excludes -tour and -observe");
            if (result.MaxPlayers > 0 && result.Mode != LaunchMode.Server && result.Mode != LaunchMode.Host)
                return Fail("-max-players needs -server or -host");
            if (result.MetricsPath.Length > 0 && result.Mode != LaunchMode.Server && result.Mode != LaunchMode.Host && !result.IsBot)
                return Fail("-metrics needs -server, -host or -bot");
            if (result.PerfTourPath.Length > 0 && result.Mode != LaunchMode.Host) return Fail("-perf-tour needs -host");
            if (result.PerfTourPath.Length > 0 && (result.Tour.Length > 0 || result.Observe.Length > 0))
                return Fail("-perf-tour excludes -tour and -observe");
            if (result.StallTestSeconds > 0 && result.Mode != LaunchMode.Server && result.Mode != LaunchMode.Host)
                return Fail("-stall-test needs -server or -host");
            if (result.NetStats && result.Mode != LaunchMode.Server && result.Mode != LaunchMode.Host)
                return Fail("-net-stats needs -server or -host");
            if (result.RemoteHz > 0 && result.Mode != LaunchMode.Server && result.Mode != LaunchMode.Host)
                return Fail("-remote-hz needs -server or -host");
            if (result.PlayerCollisionGiven && result.Mode != LaunchMode.Server && result.Mode != LaunchMode.Host)
                return Fail("-player-collision needs -server or -host (the host decides for everyone)");
            if (result.CrashTest && result.Mode != LaunchMode.Menu) return Fail("-crash-test takes no -server, -host, -connect or -steam");
            return result;
        }

        private static bool HasValue(IReadOnlyList<string> args, int i)
        {
            return i + 1 < args.Count && !string.IsNullOrWhiteSpace(args[i + 1]) && !args[i + 1].StartsWith("-");
        }

        private static LaunchArgs Fail(string error)
        {
            return new LaunchArgs { Mode = LaunchMode.Menu, Error = error };
        }

        /// <summary>Splits "host" or "host:port". IPv6 literals are not supported here.</summary>
        public static bool TrySplitEndpoint(string value, out string address, out ushort port)
        {
            address = string.Empty;
            port = DefaultTugboatPort;
            if (string.IsNullOrWhiteSpace(value)) return false;

            int colon = value.LastIndexOf(':');
            if (colon < 0)
            {
                address = value;
                return true;
            }
            if (colon == 0 || value.IndexOf(':') != colon) return false;

            address = value.Substring(0, colon);
            return ushort.TryParse(value.Substring(colon + 1), out port) && port > 0;
        }
    }
}
