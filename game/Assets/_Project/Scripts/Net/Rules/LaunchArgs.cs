using System.Collections.Generic;

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

        public LaunchMode Mode { get; private set; } = LaunchMode.Menu;
        public string Address { get; private set; } = string.Empty;
        public ushort Port { get; private set; } = DefaultTugboatPort;
        public string SteamTarget { get; private set; } = string.Empty;
        /// <summary><c>-bot</c>: drive the local player with a load-test bot (spec 004). Needs <c>-connect</c>.</summary>
        public bool IsBot { get; private set; }
        /// <summary><c>-seed n</c>: seed for the bot's random choices.</summary>
        public int Seed { get; private set; }
        /// <summary><c>-metrics path</c>: the server writes one JSON line per second there (spec 004).</summary>
        public string MetricsPath { get; private set; } = string.Empty;
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
                            return Fail("-steam needs a SteamID64 or address");
                        result.Mode = LaunchMode.ClientSteam;
                        result.SteamTarget = args[++i];
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
                    case "-metrics":
                        if (i + 1 >= args.Count || args[i + 1].StartsWith("-"))
                            return Fail("-metrics needs a file path");
                        result.MetricsPath = args[++i];
                        break;
                }
            }

            if (modeFlags > 1) return Fail("only one of -server, -host, -connect, -steam is allowed");
            if (result.IsBot && result.Mode != LaunchMode.ClientTugboat)
                return Fail("-bot needs -connect (bots use Tugboat)");
            if (result.MetricsPath.Length > 0 && result.Mode != LaunchMode.Server)
                return Fail("-metrics is only for -server");
            return result;
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
