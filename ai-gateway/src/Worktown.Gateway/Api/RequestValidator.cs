using System.Text.Json;
using System.Text.RegularExpressions;

namespace Worktown.Gateway.Api;

/// <summary>
/// Pure request validation for the item-name endpoint. Uses <c>\z</c> rather than <c>$</c>
/// because .NET's <c>$</c> also matches before a trailing newline.
/// </summary>
public static partial class RequestValidator
{
    public const int MaxBodyBytes = 8 * 1024;
    public const int MaxTags = 32;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    [GeneratedRegex(@"^[0-9a-f]{64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ItemHashRegex();

    [GeneratedRegex(@"^[a-z0-9_(),;=]{1,2048}\z", RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalTreeRegex();

    [GeneratedRegex(@"^[A-Za-z0-9 ,'-]{1,200}\z", RegexOptions.CultureInvariant)]
    private static partial Regex FallbackNameRegex();

    [GeneratedRegex(@"^[a-z0-9_]{1,32}\z", RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"^[a-z0-9-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ServerIdRegex();

    /// <summary>
    /// Deserializes a request body. Returns null for malformed JSON, unknown properties,
    /// duplicate properties, a non-object root or a body over <see cref="MaxBodyBytes"/>.
    /// </summary>
    public static ItemNameRequest? TryDeserialize(ReadOnlySpan<byte> utf8Body)
    {
        if (utf8Body.Length == 0 || utf8Body.Length > MaxBodyBytes) return null;
        try
        {
            return JsonSerializer.Deserialize<ItemNameRequest>(utf8Body, _jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>True when every field is present and matches the rules in spec 006.</summary>
    public static bool IsValid(ItemNameRequest request)
    {
        if (request.ItemHash is null || !ItemHashRegex().IsMatch(request.ItemHash)) return false;
        if (request.CanonicalTree is null || !CanonicalTreeRegex().IsMatch(request.CanonicalTree)) return false;
        if (!HasBalancedParentheses(request.CanonicalTree)) return false;
        if (request.FallbackName is null || !FallbackNameRegex().IsMatch(request.FallbackName)) return false;
        return AreTagsValid(request.Tags);
    }

    /// <summary>True for a valid <c>X-Worktown-Server-Id</c> value.</summary>
    public static bool IsValidServerId(string? serverId) =>
        serverId is not null && ServerIdRegex().IsMatch(serverId);

    /// <summary>True when parentheses nest correctly and close fully.</summary>
    public static bool HasBalancedParentheses(string text)
    {
        var depth = 0;
        foreach (var c in text)
        {
            if (c == '(') depth++;
            else if (c == ')' && --depth < 0) return false;
        }
        return depth == 0;
    }

    /// <summary>Tags: present, 0..32 items, each valid, sorted ordinally and distinct.</summary>
    public static bool AreTagsValid(IReadOnlyList<string>? tags)
    {
        if (tags is null || tags.Count > MaxTags) return false;
        for (var i = 0; i < tags.Count; i++)
        {
            var tag = tags[i];
            if (tag is null || !TagRegex().IsMatch(tag)) return false;
            if (i > 0 && string.CompareOrdinal(tags[i - 1], tag) >= 0) return false;
        }
        return true;
    }
}
