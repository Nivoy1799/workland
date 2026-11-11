namespace Worktown.Game.Net.Rules
{
    /// <summary>Spec 047: one row of the lobby panel.</summary>
    public readonly struct LobbyLine
    {
        public readonly int ClientId;
        /// <summary>The name and its tags: "Ann (host)", "Bo (you)".</summary>
        public readonly string Text;
        /// <summary>This viewer may kick that player (the host, for anyone but itself).</summary>
        public readonly bool CanKick;

        public LobbyLine(int clientId, string text, bool canKick)
        {
            ClientId = clientId;
            Text = text;
            CanKick = canKick;
        }

        /// <summary>
        /// Spec 047, pure: the rows for a roster (ids and names in the same order), the host first, then by id. <paramref name="ownId"/>
        /// is this viewer's connection; <paramref name="asServer"/> true for the host (only it kicks, JoinRules.CanKick).
        /// </summary>
        public static LobbyLine[] Build(int[] ids, string[] names, int hostId, int ownId, bool asServer)
        {
            if (ids == null || names == null || ids.Length != names.Length) return new LobbyLine[0];
            var order = new int[ids.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            System.Array.Sort(order, (a, b) =>
            {
                bool ha = ids[a] == hostId, hb = ids[b] == hostId;
                if (ha != hb) return ha ? -1 : 1;
                return ids[a].CompareTo(ids[b]);
            });
            var lines = new LobbyLine[ids.Length];
            for (int n = 0; n < order.Length; n++)
            {
                int i = order[n];
                string text = names[i] ?? string.Empty;
                if (ids[i] == hostId) text += " (host)";
                if (ids[i] == ownId) text += " (you)";
                lines[n] = new LobbyLine(ids[i], text, JoinRules.CanKick(asServer, ids[i] == hostId || ids[i] == ownId));
            }
            return lines;
        }

        /// <summary>The panel's heading: "Players 3/16".</summary>
        public static string Heading(int players, int maxPlayers) => "Players " + players + "/" + maxPlayers;
    }
}
