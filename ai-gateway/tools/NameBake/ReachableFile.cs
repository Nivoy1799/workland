using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Worktown.NameBake;

/// <summary>
/// Spec 056b: the reachable-items file written by <c>scripts/item-list.sh</c> (spec 056a, format
/// <c>worktown-reachable-items</c> v1, hash version v1). Parsing checks the header, every hash and the set hash; anything
/// else is <see cref="InvalidDataException"/>.
/// </summary>
internal sealed class ReachableFile
{
    public const string Format = "worktown-reachable-items";
    public const int FormatVersion = 1;
    public const string HashVersion = "v1";

    /// <summary>One reachable item (the file's fields).</summary>
    public sealed record Item(string Hash, string CanonicalTree, string FallbackName, IReadOnlyList<string> Tags,
        IReadOnlyList<string> Routes, bool Target, IReadOnlyList<string> LabelWords);

    private ReachableFile(string setHash, IReadOnlyList<Item> items)
    {
        SetHash = setHash;
        Items = items;
    }

    /// <summary>The 16-hex set hash (checked against the items).</summary>
    public string SetHash { get; }

    /// <summary>The items in hash order.</summary>
    public IReadOnlyList<Item> Items { get; }

    public static ReachableFile Load(string path)
    {
        if (!File.Exists(path)) throw new InvalidDataException($"Reachable-items file not found: {path} (run scripts/item-list.sh).");
        return Parse(File.ReadAllText(path, Encoding.UTF8));
    }

    public static ReachableFile Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || Text(root, "format") != Format
                || root.GetProperty("formatVersion").GetInt32() != FormatVersion
                || Text(root, "hashVersion") != HashVersion)
                throw new InvalidDataException("Not a worktown-reachable-items v1 file (hash version v1).");
            var setHash = Text(root, "setHash");
            var items = new List<Item>();
            foreach (var e in root.GetProperty("items").EnumerateArray())
            {
                var item = new Item(Text(e, "hash"), Text(e, "canonicalTree"), Text(e, "fallbackName"), Strings(e, "tags"),
                    Strings(e, "routes"), e.GetProperty("target").GetBoolean(), Strings(e, "labelWords"));
                if (!IsHash(item.Hash)) throw new InvalidDataException("Bad item hash in the reachable-items file.");
                items.Add(item);
            }
            items.Sort((a, b) => string.CompareOrdinal(a.Hash, b.Hash));
            for (var i = 1; i < items.Count; i++)
                if (items[i].Hash == items[i - 1].Hash) throw new InvalidDataException("Duplicate item hash in the reachable-items file.");
            if (setHash != ComputeSetHash(items.Select(i => i.Hash)))
                throw new InvalidDataException("The reachable-items file's set hash does not match its items.");
            return new ReachableFile(setHash, items.AsReadOnly());
        }
        catch (Exception e) when (e is JsonException || e is KeyNotFoundException || e is InvalidOperationException || e is FormatException)
        {
            throw new InvalidDataException("Malformed reachable-items file.", e);
        }
    }

    /// <summary>The first 16 hex of the SHA-256 of the hashes, sorted ordinally and joined by a line feed (as spec 056a).</summary>
    public static string ComputeSetHash(IEnumerable<string> hashes)
    {
        var sorted = hashes.ToList();
        sorted.Sort(StringComparer.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', sorted))))[..16];
    }

    private static bool IsHash(string hash) => hash.Length == 64 && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Text(JsonElement e, string name) =>
        e.GetProperty(name).GetString() ?? throw new InvalidDataException($"Missing '{name}'.");

    private static IReadOnlyList<string> Strings(JsonElement e, string name) =>
        e.GetProperty(name).EnumerateArray().Select(x => x.GetString() ?? throw new InvalidDataException($"Null in '{name}'.")).ToArray();
}
