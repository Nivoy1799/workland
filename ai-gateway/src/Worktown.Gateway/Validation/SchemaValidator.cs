using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CorvusSchema = Corvus.Text.Json.Validator.JsonSchema;

namespace Worktown.Gateway.Validation;

/// <summary>
/// Strict validation of upstream output against an embedded JSON schema (draft 2020-12, via
/// Corvus.Text.Json.Validator). Invalid output is rejected as is: no trimming, no fence
/// stripping, no repair. Duplicate properties are rejected before schema validation.
/// </summary>
public sealed class SchemaValidator
{
    public const string ItemNameContentType = "item_name";
    public const int ItemNameSchemaVersion = 1;

    private static readonly JsonDocumentOptions _documentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    private readonly CorvusSchema _schema;

    public SchemaValidator(string contentType, int schemaVersion, string schemaText)
    {
        ContentType = contentType;
        SchemaVersion = schemaVersion;
        Schema = JsonNode.Parse(schemaText) ?? throw new InvalidOperationException("Schema text is empty.");
        _schema = CorvusSchema.FromText(schemaText);
    }

    public string ContentType { get; }
    public int SchemaVersion { get; }

    /// <summary>The full schema as a node (clone before changing it).</summary>
    public JsonNode Schema { get; }

    /// <summary>Validator for <c>schemas/item_name.v1.schema.json</c> (embedded resource).</summary>
    public static SchemaValidator ForItemNameV1() =>
        new(ItemNameContentType, ItemNameSchemaVersion, LoadEmbedded("schemas.item_name.v1.schema.json"));

    /// <summary>Reads an embedded schema file by its logical name.</summary>
    public static string LoadEmbedded(string logicalName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"Embedded schema '{logicalName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// True when <paramref name="content"/> is a single JSON document that matches the schema.
    /// On success <paramref name="payload"/> holds a detached copy of the root element.
    /// </summary>
    public bool TryValidate(string? content, out JsonElement payload)
    {
        payload = default;
        if (string.IsNullOrEmpty(content)) return false;
        try
        {
            using var doc = JsonDocument.Parse(content, _documentOptions);
            if (!_schema.Validate(content)) return false;
            payload = doc.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
