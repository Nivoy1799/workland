using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Worktown.Sim.Layout;

namespace Worktown.Sim.Tests.Layout
{
    /// <summary>Spec 062a: the game's default layout (game/Assets/_Project/Resources/Layouts/default.json), read as the game reads it.</summary>
    internal static class DefaultLayoutFile
    {
        public static ShopLayout Load()
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Find()));
            JsonElement root = doc.RootElement;
            var pieces = new List<LayoutPiece>();
            foreach (JsonElement p in root.GetProperty("pieces").EnumerateArray())
                pieces.Add(new LayoutPiece(p.GetProperty("id").GetInt32(), p.GetProperty("type").GetString() ?? string.Empty,
                    p.GetProperty("xMm").GetInt32(), p.GetProperty("zMm").GetInt32(), p.GetProperty("turns").GetInt32(),
                    p.GetProperty("lengthMm").GetInt32(), p.GetProperty("tier").GetInt32(), p.GetProperty("paidCents").GetInt64()));
            var homes = new List<ItemHome>();
            foreach (JsonElement h in root.GetProperty("homes").EnumerateArray())
                homes.Add(new ItemHome(h.GetProperty("item").GetString() ?? string.Empty, h.GetProperty("piece").GetInt32(),
                    h.GetProperty("local").GetInt32(), h.GetProperty("spot").GetInt32()));
            return new ShopLayout(root.GetProperty("version").GetInt32(), pieces, null, homes, root.GetProperty("nextId").GetInt32());
        }

        private static string Find()
        {
            for (DirectoryInfo? d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                string candidate = Path.Combine(d.FullName, "game", "Assets", "_Project", "Resources", "Layouts", "default.json");
                if (File.Exists(candidate)) return candidate;
            }
            throw new FileNotFoundException("game/Assets/_Project/Resources/Layouts/default.json not found above " + AppContext.BaseDirectory);
        }
    }
}
