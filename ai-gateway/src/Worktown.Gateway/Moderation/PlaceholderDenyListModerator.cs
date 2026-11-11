using System.Text.Json;

namespace Worktown.Gateway.Moderation;

// PLACEHOLDER (O6 OPEN): replace before Early Access; choosing the approach is a human decision.
/// <summary>
/// Placeholder moderation: rejects a payload when any string value contains a deny-listed word
/// as a whole word (case-insensitive, ASCII letters). Not real moderation.
/// </summary>
public sealed class PlaceholderDenyListModerator : IModerator
{
    public const string ReasonDenyListed = "deny_listed_word";

    // A short list of obvious profanity and slurs. Deliberately minimal (O6 OPEN).
    private static readonly string[] _defaultWords =
    {
        "fuck", "fucking", "fucker", "shit", "shitty", "cunt", "bitch", "bastard", "asshole",
        "whore", "slut", "nigger", "nigga", "faggot", "fag", "retard", "retarded", "spic", "kike",
    };

    private readonly HashSet<string> _words;

    public PlaceholderDenyListModerator()
        : this(_defaultWords)
    {
    }

    public PlaceholderDenyListModerator(IEnumerable<string> words)
    {
        _words = new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);
    }

    public Task<ModerationResult> CheckAsync(string contentType, JsonElement payload, CancellationToken ct) =>
        Task.FromResult(ContainsDeniedWord(payload) ? ModerationResult.Reject(ReasonDenyListed) : ModerationResult.Allow);

    private bool ContainsDeniedWord(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return ContainsDeniedWord(element.GetString() ?? "");
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (ContainsDeniedWord(property.Value)) return true;
                }
                return false;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (ContainsDeniedWord(item)) return true;
                }
                return false;
            default:
                return false;
        }
    }

    private bool ContainsDeniedWord(string text)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var isLetter = i < text.Length && char.IsAsciiLetter(text[i]);
            if (isLetter)
            {
                if (start < 0) start = i;
            }
            else if (start >= 0)
            {
                if (_words.Contains(text[start..i])) return true;
                start = -1;
            }
        }
        return false;
    }
}
