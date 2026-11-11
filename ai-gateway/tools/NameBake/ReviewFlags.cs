namespace Worktown.NameBake;

/// <summary>
/// Spec 056b: help for the reviewer, never a verdict. <see cref="Doneness"/>: a word of the item's own bucket labels
/// ("crispy", "dark") is missing from the name; <see cref="Same"/>: the name equals the plain name (case-insensitive);
/// <see cref="Dup"/>: a row with other routes has the same name (case-insensitive); <see cref="Long"/>: the name is over
/// <see cref="MaxNameLength"/> chars.
/// </summary>
internal static class ReviewFlags
{
    public const string Doneness = "doneness";
    public const string Same = "same";
    public const string Dup = "dup";
    public const string Long = "long";
    public const int MaxNameLength = 30;

    private static readonly char[] WordBreaks = { ' ', '-', ',' };

    /// <summary>True when a label word (each hyphen part of it) is not a word of the name (case-insensitive).</summary>
    public static bool MissesDoneness(string name, IReadOnlyList<string> labelWords)
    {
        var words = new HashSet<string>(name.Split(WordBreaks, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        foreach (var label in labelWords)
            foreach (var part in label.Split(WordBreaks, StringSplitOptions.RemoveEmptyEntries))
                if (!words.Contains(part)) return true;
        return false;
    }

    public static bool IsSame(string name, string plainName) =>
        string.Equals(name.Trim(), plainName.Trim(), StringComparison.OrdinalIgnoreCase);

    public static bool IsLong(string name) => name.Length > MaxNameLength;

    /// <summary>
    /// The flags of every row (a comma list in the order doneness, same, dup, long). <paramref name="labelWords"/> holds each
    /// row's label words by key12 (none when missing).
    /// </summary>
    public static IReadOnlyList<string> Compute(IReadOnlyList<ReviewRow> rows, IReadOnlyDictionary<string, IReadOnlyList<string>> labelWords)
    {
        var routesByName = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var name = row.Name.Trim();
            if (!routesByName.TryGetValue(name, out var set)) routesByName[name] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(row.Routes);
        }
        var flags = new List<string>(rows.Count);
        foreach (var row in rows)
        {
            var list = new List<string>(4);
            if (labelWords.TryGetValue(row.Key12, out var words) && MissesDoneness(row.Name, words)) list.Add(Doneness);
            if (IsSame(row.Name, row.PlainName)) list.Add(Same);
            if (routesByName[row.Name.Trim()].Count > 1) list.Add(Dup);
            if (IsLong(row.Name)) list.Add(Long);
            flags.Add(string.Join(',', list));
        }
        return flags;
    }
}
