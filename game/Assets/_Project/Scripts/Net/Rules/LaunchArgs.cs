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
        /// <summary>Empty when parsing succeeded.</summary>
        public string Error { get; private set; } = string.Empty;

        public bool IsValid => Error.Length == 0;

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
                    case "-metrics":
                        if (!HasValue(args, i)) return Fail("-metrics needs a file path");
                        result.MetricsPath = args[++i];
                        break;
                }
            }

            if (modeFlags > 1) return Fail("only one of -server, -host, -connect, -steam is allowed");
            if (result.Tour.Length > 0 && result.Observe.Length > 0) return Fail("-tour and -observe exclude each other");
            // Spec 047: the menu tour starts on the title screen (no mode flag); every other tour joins with -connect.
            if (result.Tour == MenuTour && result.Mode != LaunchMode.Menu) return Fail("-tour menu takes no -server, -host, -connect or -steam");
            if (result.Tour.Length > 0 && result.Tour != MenuTour && result.Mode != LaunchMode.ClientTugboat) return Fail("-tour needs -connect");
            if (result.Observe.Length > 0 && result.Mode != LaunchMode.ClientTugboat && result.Mode != LaunchMode.Host)
                return Fail("-observe needs -connect or -host");
            if (result.IsBot && result.Mode != LaunchMode.ClientTugboat) return Fail("-bot needs -connect (bots use Tugboat)");
            if (result.IsBot && (result.Tour.Length > 0 || result.Observe.Length > 0)) return Fail("-bot excludes -tour and -observe");
            if (result.MaxPlayers > 0 && result.Mode != LaunchMode.Server && result.Mode != LaunchMode.Host)
                return Fail("-max-players needs -server or -host");
            if (result.MetricsPath.Length > 0 && result.Mode != LaunchMode.Server && result.Mode != LaunchMode.Host && !result.IsBot)
                return Fail("-metrics needs -server, -host or -bot");
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
