namespace Worktown.Gateway.Cache;

/// <summary>A validated and moderated payload, stored exactly as the upstream returned it.</summary>
public sealed record CachedContent(string ContentType, string ItemHash, string CanonicalTree,
    string PayloadJson, string ContentVersion, string Model, DateTimeOffset CreatedAt);
