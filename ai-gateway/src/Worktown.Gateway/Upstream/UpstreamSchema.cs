using System.Text.Json.Nodes;

namespace Worktown.Gateway.Upstream;

/// <summary>
/// Builds the schema sent in <c>response_format</c>. Provider support for some keywords varies,
/// so they are stripped here; the local validation of the full schema stays the authority.
/// </summary>
public static class UpstreamSchema
{
    private static readonly HashSet<string> _strippedKeywords = new(StringComparer.Ordinal)
    {
        "$schema", "$id", "pattern", "minLength", "maxLength",
    };

    // Keywords whose object value maps names to subschemas (the names are not keywords).
    private static readonly HashSet<string> _schemaMapKeywords = new(StringComparer.Ordinal)
    {
        "properties", "patternProperties", "$defs", "definitions", "dependentSchemas",
    };

    // Keywords whose value is data, not a schema: never touched.
    private static readonly HashSet<string> _dataKeywords = new(StringComparer.Ordinal)
    {
        "const", "enum", "default", "examples",
    };

    /// <summary>Returns a deep copy without the stripped keywords; the input is not changed.</summary>
    public static JsonNode ForUpstream(JsonNode schema) => StripSchema(schema.DeepClone());

    private static JsonNode StripSchema(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var name in obj.Select(p => p.Key).ToList())
                {
                    if (_strippedKeywords.Contains(name))
                    {
                        obj.Remove(name);
                        continue;
                    }
                    var child = obj[name];
                    if (child is null || _dataKeywords.Contains(name)) continue;
                    if (_schemaMapKeywords.Contains(name) && child is JsonObject map)
                    {
                        foreach (var entry in map.Select(p => p.Key).ToList())
                        {
                            if (map[entry] is { } sub) StripSchema(sub);
                        }
                    }
                    else
                    {
                        StripSchema(child);
                    }
                }
                return obj;
            case JsonArray array:
                foreach (var item in array)
                {
                    if (item is not null) StripSchema(item);
                }
                return array;
            default:
                return node;
        }
    }
}
