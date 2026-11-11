using System.Text.Json.Serialization;

namespace Worktown.Gateway.Api;

/// <summary>200 body of POST /v1/content/item-name. Flavor text only (D5).</summary>
public sealed record ItemNameResponse(
    [property: JsonPropertyName("itemHash")] string ItemHash,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("cacheHit")] bool CacheHit,
    [property: JsonPropertyName("contentVersion")] string ContentVersion,
    [property: JsonPropertyName("model")] string Model);
