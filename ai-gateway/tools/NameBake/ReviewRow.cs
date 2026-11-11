namespace Worktown.NameBake;

/// <summary>
/// Spec 056b: one row of <c>ai-gateway/offline/review.tsv</c>: the first 12 hex of the item's cache key, its routes
/// (comma list), whether it is a route's target, its plain (fallback) name, the generated name and description (the
/// reviewer may edit them; publish re-checks them), the reviewer's helper flags, the decision (<c>ok</c>, <c>no</c> or
/// empty) and a note.
/// </summary>
internal sealed record ReviewRow(string Key12, string Routes, bool Target, string PlainName, string Name, string Description,
    string Flags, string Decision, string Note)
{
    public const string Ok = "ok";
    public const string No = "no";

    /// <summary>The first route (the sheet's sort key).</summary>
    public string FirstRoute => Routes.Split(',')[0];
}
