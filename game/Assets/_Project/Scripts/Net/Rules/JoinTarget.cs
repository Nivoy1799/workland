namespace Worktown.Game.Net.Rules
{
    /// <summary>Spec 047, pure: where a session goes (the rejoin connects to the same target again).</summary>
    public sealed class JoinTarget
    {
        public JoinTargetKind Kind { get; }
        /// <summary>Address (Tugboat) or the SteamID64 text (Steam); empty for a host.</summary>
        public string Address { get; }
        public ushort Port { get; }

        private JoinTarget(JoinTargetKind kind, string address, ushort port)
        {
            Kind = kind;
            Address = address ?? string.Empty;
            Port = port;
        }

        public static JoinTarget Host() => new JoinTarget(JoinTargetKind.Host, string.Empty, LaunchArgs.DefaultTugboatPort);

        public static JoinTarget ByAddress(string address, ushort port) => new JoinTarget(JoinTargetKind.Address, address, port);

        public static JoinTarget BySteam(string steamId) => new JoinTarget(JoinTargetKind.Steam, steamId, 0);

        /// <summary>For the loading screen and the log: "your shop", "127.0.0.1:7770", "Steam friend 7656...".</summary>
        public string Describe()
        {
            switch (Kind)
            {
                case JoinTargetKind.Host: return "your shop";
                case JoinTargetKind.Address: return Address + ":" + Port;
                default: return "Steam friend " + Address;
            }
        }
    }
}
