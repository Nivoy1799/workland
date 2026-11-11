using System.Text;

namespace Worktown.NameBake;

/// <summary>
/// Spec 056b: the human review sheet <c>ai-gateway/offline/review.tsv</c> (tab-separated, LF, one header line). After each
/// bake <see cref="Merge"/> builds one row per cached reachable item, sorted by first route, then targets first, then plain
/// name: an existing row (same cache key) keeps its name, description, decision and note (a reviewer's edits stay); a new
/// row gets the generated text and an empty decision; a regenerated row (it was <c>no</c>) gets the new text, an empty
/// decision and <c>rejected: &lt;old name&gt;</c> in its note. The flags are recomputed every time (<see cref="ReviewFlags"/>).
/// </summary>
internal static class ReviewSheet
{
    public const string Header = "key12\troutes\ttarget\tplain_name\tname\tdescription\tflags\tdecision\tnote";
    private const int Columns = 9;

    /// <summary>The rows of the sheet at <paramref name="path"/>; none when the file is missing.</summary>
    public static IReadOnlyList<ReviewRow> Load(string path) =>
        File.Exists(path) ? Parse(File.ReadAllText(path, Encoding.UTF8)) : Array.Empty<ReviewRow>();

    /// <summary>
    /// Parses a sheet (CRLF from a spreadsheet is fine, missing trailing empty columns too). The decision is trimmed and lower
    /// case. A wrong header, a row with too many columns, a bad key12 or a duplicate key12 is <see cref="InvalidDataException"/>.
    /// </summary>
    public static IReadOnlyList<ReviewRow> Parse(string text)
    {
        var rows = new List<ReviewRow>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var lines = text.Split('\n');
        var headerSeen = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0) continue;
            if (!headerSeen)
            {
                if (line.TrimStart('﻿') != Header) throw new InvalidDataException("review.tsv: unexpected header line.");
                headerSeen = true;
                continue;
            }
            var cells = line.Split('\t');
            if (cells.Length > Columns || cells.Length < 6) throw new InvalidDataException($"review.tsv line {i + 1}: expected {Columns} columns.");
            string Cell(int c) => c < cells.Length ? cells[c] : "";
            var key12 = Cell(0).Trim();
            if (key12.Length != 12 || !key12.All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f'))
                throw new InvalidDataException($"review.tsv line {i + 1}: bad key12.");
            if (!keys.Add(key12)) throw new InvalidDataException($"review.tsv line {i + 1}: duplicate key12.");
            rows.Add(new ReviewRow(key12, Cell(1), Cell(2).Trim() == "true", Cell(3), Cell(4), Cell(5), Cell(6),
                Cell(7).Trim().ToLowerInvariant(), Cell(8)));
        }
        return rows;
    }

    /// <summary>The sheet text: the header, then one line per row in the given order, LF.</summary>
    public static string Format(IReadOnlyList<ReviewRow> rows)
    {
        var sb = new StringBuilder(Header).Append('\n');
        foreach (var r in rows)
        {
            var cells = new[] { r.Key12, r.Routes, r.Target ? "true" : "false", r.PlainName, r.Name, r.Description, r.Flags, r.Decision, r.Note };
            foreach (var c in cells)
                if (c.Contains('\t') || c.Contains('\n') || c.Contains('\r'))
                    throw new InvalidDataException($"review.tsv: row {r.Key12} has a tab or line break in a cell.");
            sb.Append(string.Join('\t', cells)).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Writes the sheet atomically (<c>.tmp</c>, then replace).</summary>
    public static void Save(string path, IReadOnlyList<ReviewRow> rows) => NameBookWriter.WriteAtomic(path, Format(rows));

    /// <summary>
    /// One row per entry of <paramref name="current"/> (the cached reachable items with their generated text), merged with
    /// <paramref name="existing"/> by key12; <paramref name="regenerated"/> maps the key12 of each row regenerated in this
    /// bake to its rejected name. Sorted, flags recomputed.
    /// </summary>
    public static IReadOnlyList<ReviewRow> Merge(IReadOnlyList<ReviewRow> existing,
        IReadOnlyList<(ReachableFile.Item Item, string Key12, string Name, string Description)> current,
        IReadOnlyDictionary<string, string> regenerated)
    {
        var old = new Dictionary<string, ReviewRow>(StringComparer.Ordinal);
        foreach (var r in existing) old[r.Key12] = r;
        var rows = new List<ReviewRow>(current.Count);
        var labelWords = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (item, key12, name, description) in current)
        {
            var routes = string.Join(',', item.Routes);
            labelWords[key12] = item.LabelWords;
            old.TryGetValue(key12, out var prior);
            ReviewRow row;
            if (regenerated.TryGetValue(key12, out var rejectedName))
            {
                var note = "rejected: " + rejectedName;
                if (prior is { Note.Length: > 0 }) note = prior.Note + "; " + note;
                row = new ReviewRow(key12, routes, item.Target, item.FallbackName, name, description, "", "", note);
            }
            else if (prior is not null)
            {
                row = prior with { Routes = routes, Target = item.Target, PlainName = item.FallbackName };
            }
            else
            {
                row = new ReviewRow(key12, routes, item.Target, item.FallbackName, name, description, "", "", "");
            }
            rows.Add(row);
        }
        rows.Sort(Compare);
        var flags = ReviewFlags.Compute(rows, labelWords);
        for (var i = 0; i < rows.Count; i++) rows[i] = rows[i] with { Flags = flags[i] };
        return rows.AsReadOnly();
    }

    private static int Compare(ReviewRow a, ReviewRow b)
    {
        var c = string.CompareOrdinal(a.FirstRoute, b.FirstRoute);
        if (c != 0) return c;
        if (a.Target != b.Target) return a.Target ? -1 : 1;
        c = string.CompareOrdinal(a.PlainName, b.PlainName);
        return c != 0 ? c : string.CompareOrdinal(a.Key12, b.Key12);
    }
}
