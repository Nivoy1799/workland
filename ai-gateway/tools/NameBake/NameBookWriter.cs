using System.Text;

namespace Worktown.NameBake;

/// <summary>
/// Spec 056b: the shipped name book (<c>game/Assets/_Project/Resources/ItemNames.json</c>, format <c>worktown-item-names</c>
/// v1): the reachable set it was made for, the content version, model and moderator, and the approved names sorted by hash.
/// LF, one item per line, no timestamps: the same inputs give a byte-identical file. Also the atomic write the tool's
/// files use.
/// </summary>
internal static class NameBookWriter
{
    public const string Format = "worktown-item-names";
    public const int FormatVersion = 1;
    public const string HashVersion = "v1";

    /// <summary>The book's text; <paramref name="items"/> in any order (sorted by hash here).</summary>
    public static string Write(string reachableSet, string contentVersion, string model, string moderator,
        IReadOnlyList<(string Hash, string Name, string Description)> items)
    {
        var sorted = items.ToList();
        sorted.Sort((a, b) => string.CompareOrdinal(a.Hash, b.Hash));
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"format\": ").Append(Quote(Format)).Append(",\n");
        sb.Append("  \"formatVersion\": ").Append(FormatVersion).Append(",\n");
        sb.Append("  \"hashVersion\": ").Append(Quote(HashVersion)).Append(",\n");
        sb.Append("  \"reachableSet\": ").Append(Quote(reachableSet)).Append(",\n");
        sb.Append("  \"contentVersion\": ").Append(Quote(contentVersion)).Append(",\n");
        sb.Append("  \"model\": ").Append(Quote(model)).Append(",\n");
        sb.Append("  \"moderator\": ").Append(Quote(moderator)).Append(",\n");
        sb.Append("  \"items\": [");
        for (var i = 0; i < sorted.Count; i++)
        {
            var (hash, name, description) = sorted[i];
            sb.Append(i == 0 ? "\n" : ",\n");
            sb.Append("    { \"hash\": ").Append(Quote(hash)).Append(", \"name\": ").Append(Quote(name))
                .Append(", \"description\": ").Append(Quote(description)).Append(" }");
        }
        sb.Append(sorted.Count == 0 ? "]\n" : "\n  ]\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>Writes UTF-8 (no BOM) to <c>path.tmp</c>, then moves it over <paramref name="path"/> (the folder is created).</summary>
    public static void WriteAtomic(string path, string text)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = full + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        File.Move(tmp, full, overwrite: true);
    }

    /// <summary>A JSON string of printable ASCII (the schema allows nothing else); anything else throws.</summary>
    private static string Quote(string value)
    {
        var sb = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            if (c < 0x20 || c > 0x7E) throw new InvalidDataException("Name book text must be printable ASCII.");
            if (c == '"' || c == '\\') sb.Append('\\');
            sb.Append(c);
        }
        return sb.Append('"').ToString();
    }
}
