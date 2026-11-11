namespace Worktown.Gateway.Cache;

/// <summary>
/// A validated and moderated payload, stored exactly as the upstream returned it.
/// <see cref="InputHash"/> is <c>ItemNamePrompt.InputHash</c> of the request that produced it, so a
/// hit is only served when every prompt input (tree, fallback name, tags) matches.
/// </summary>
public sealed record CachedContent(string ContentType, string ItemHash, string CanonicalTree, string InputHash,
    string PayloadJson, string ContentVersion, string Model, DateTimeOffset CreatedAt);
