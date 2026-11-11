using System.Text.Json;

namespace Worktown.Gateway.Moderation;

/// <summary>
/// D27 (the world is vegan) as a fixed moderation step. It always runs in the pipeline, before
/// the replaceable <see cref="IModerator"/> and before the cache write; it is not swapped out
/// through DI. A hit rejects the output (422, reason <see cref="Reason"/>); the text is never
/// repaired.
/// The word list and matching rules are a copy of <c>Worktown.Sim.Cooking.ContentRules</c>
/// (spec 028). The gateway does not reference /sim (spec 006, Open question 7); a drift test in
/// the test project asserts that both lists are equal.
/// </summary>
public static class VeganContentRule
{
    public const string Reason = "vegan_rule";

    // Keep equal to Worktown.Sim.Cooking.ContentRules.ForbiddenWords (same order); a test checks it.
    private static readonly string[] _words =
    {
        "cheese", "milk", "butter", "egg", "meat", "beef", "pork", "chicken", "fish", "bacon", "ham", "gelatin",
        "honey", "cream", "yoghurt", "yogurt",
    };

    /// <summary>The forbidden words, singular; plurals ("eggs", "fishes") are matched as well.</summary>
    public static IReadOnlyList<string> ForbiddenWords { get; } = Array.AsReadOnly(_words);

    /// <summary>Rejects the payload when any string value contains a forbidden word.</summary>
    public static ModerationResult Check(JsonElement payload) =>
        ContainsForbidden(payload) ? ModerationResult.Reject(Reason) : ModerationResult.Allow;

    /// <summary>
    /// The first forbidden word in <paramref name="text"/> or null. Same rules as /sim: case-insensitive,
    /// whole words, letters delimit words, and a word matches when it equals a forbidden word or that
    /// word plus "s" or "es". So "eggplant" passes and "egg-free" does not.
    /// </summary>
    public static string? FindForbidden(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var i = 0;
        while (i < text.Length)
        {
            if (!char.IsLetter(text[i])) { i++; continue; }
            var start = i;
            while (i < text.Length && char.IsLetter(text[i])) i++;
            var word = text.Substring(start, i - start).ToLowerInvariant();
            foreach (var forbidden in _words)
            {
                if (word == forbidden || word == forbidden + "s" || word == forbidden + "es") return forbidden;
            }
        }
        return null;
    }

    private static bool ContainsForbidden(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return FindForbidden(element.GetString()) is not null;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (ContainsForbidden(property.Value)) return true;
                }
                return false;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (ContainsForbidden(item)) return true;
                }
                return false;
            default:
                return false;
        }
    }
}
