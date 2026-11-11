using Worktown.Gateway.Api;

namespace Worktown.Gateway.Prompts;

/// <summary>
/// Builds the chat messages for the item_name content type. Only validated in-game IDs and
/// computed properties are inserted. Any change to the text must bump <see cref="Version"/>
/// (it is part of the cache key).
/// </summary>
public static class ItemNamePrompt
{
    public const int Version = 1;

    public const string SystemText =
        "You name dishes in a cozy, low-poly town life game. " +
        "The game world is fully vegan: never mention meat, fish, seafood, eggs, dairy, honey " +
        "or any other animal product. " +
        "Given how a dish was made, reply with a short, appetizing, family-friendly English dish " +
        "name of 2 to 5 words, with no brands and no real people, and one description sentence. " +
        "Use plain English letters only. " +
        "Reply with JSON only, exactly in the form {\"name\": \"...\", \"description\": \"...\"}.";

    /// <summary>System and user messages for a request that already passed <see cref="RequestValidator"/>.</summary>
    public static IReadOnlyList<(string Role, string Content)> Build(ItemNameRequest request)
    {
        var tags = request.Tags is { Count: > 0 } list ? string.Join(", ", list) : "none";
        var user = $"Process: {request.CanonicalTree}\nPlain name: {request.FallbackName}\nTags: {tags}";
        return new[] { ("system", SystemText), ("user", user) };
    }
}
