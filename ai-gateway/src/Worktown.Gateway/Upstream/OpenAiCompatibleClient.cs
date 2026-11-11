using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Worktown.Gateway.Config;

namespace Worktown.Gateway.Upstream;

/// <summary>
/// Typed HttpClient for <c>POST {base}/chat/completions</c>. Never logs the key, headers,
/// messages or response content. The per-attempt timeout uses the injected <see cref="TimeProvider"/>.
/// </summary>
public sealed class OpenAiCompatibleClient : IUpstreamClient
{
    public const string HttpClientName = "upstream";
    public const int MaxTokens = 300;

    private readonly HttpClient _http;
    private readonly GatewayOptions _options;
    private readonly TimeProvider _time;

    public OpenAiCompatibleClient(HttpClient http, IOptions<GatewayOptions> options, TimeProvider time)
    {
        _http = http;
        _options = options.Value;
        _time = time;
    }

    public async Task<UpstreamResult> CompleteAsync(IReadOnlyList<(string Role, string Content)> messages,
        string schemaName, JsonNode schema, CancellationToken ct)
    {
        var body = BuildBody(_options, messages, schemaName, schema);
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.UpstreamBaseUrl.TrimEnd('/') + "/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.UpstreamApiKey);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_options.UpstreamTimeoutSeconds), _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, linked.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return new UpstreamResult.HttpError((int)response.StatusCode);
            var text = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            return ParseEnvelope(text);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new UpstreamResult.Timeout();
        }
        catch (HttpRequestException)
        {
            return new UpstreamResult.NetworkError();
        }
    }

    /// <summary>The chat-completions body for the configured flavor (pure; exposed for tests).</summary>
    public static JsonObject BuildBody(GatewayOptions options, IReadOnlyList<(string Role, string Content)> messages,
        string schemaName, JsonNode schema)
    {
        var messageArray = new JsonArray();
        foreach (var (role, content) in messages)
            messageArray.Add(new JsonObject { ["role"] = role, ["content"] = content });

        var body = new JsonObject
        {
            ["model"] = options.Model,
            ["messages"] = messageArray,
            ["temperature"] = options.Temperature,
            ["max_tokens"] = MaxTokens,
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = schemaName,
                    ["strict"] = true,
                    ["schema"] = UpstreamSchema.ForUpstream(schema),
                },
            },
        };

        if (options.UpstreamFlavor == GatewayOptions.FlavorOpenRouter)
        {
            var provider = new JsonObject { ["require_parameters"] = true };
            if (options.OpenRouterProviders.Count > 0)
                provider["only"] = new JsonArray(options.OpenRouterProviders.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray());
            provider["allow_fallbacks"] = true;
            body["provider"] = provider;
            if (options.ReasoningEffort.Length > 0)
                body["reasoning"] = new JsonObject { ["effort"] = options.ReasoningEffort };
        }
        return body;
    }

    /// <summary>Reads <c>choices[0].message.content</c> and <c>finish_reason</c>; nulls when malformed.</summary>
    public static UpstreamResult.Ok ParseEnvelope(string responseText)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseText);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("choices"u8, out var choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                string? finish = first.ValueKind == JsonValueKind.Object
                    && first.TryGetProperty("finish_reason"u8, out var fr) && fr.ValueKind == JsonValueKind.String
                    ? fr.GetString() : null;
                string? content = first.ValueKind == JsonValueKind.Object
                    && first.TryGetProperty("message"u8, out var msg) && msg.ValueKind == JsonValueKind.Object
                    && msg.TryGetProperty("content"u8, out var c) && c.ValueKind == JsonValueKind.String
                    ? c.GetString() : null;
                return new UpstreamResult.Ok(content, finish);
            }
        }
        catch (JsonException)
        {
        }
        return new UpstreamResult.Ok(null, null);
    }
}
