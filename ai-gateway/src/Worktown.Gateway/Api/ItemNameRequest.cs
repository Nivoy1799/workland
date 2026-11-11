using System.Text.Json.Serialization;

namespace Worktown.Gateway.Api;

/// <summary>
/// Body of POST /v1/content/item-name. All fields come from game data on the official server
/// (spec 005 item identity); exact stats and quality are deliberately absent (D20).
/// Unknown properties are rejected.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ItemNameRequest(
    [property: JsonPropertyName("itemHash")] string? ItemHash,
    [property: JsonPropertyName("canonicalTree")] string? CanonicalTree,
    [property: JsonPropertyName("fallbackName")] string? FallbackName,
    [property: JsonPropertyName("tags")] IReadOnlyList<string>? Tags);
