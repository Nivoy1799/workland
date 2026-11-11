using System.Text.Json.Nodes;

namespace Worktown.Gateway.Upstream;

/// <summary>One attempt against an OpenAI-compatible chat completions upstream.</summary>
public interface IUpstreamClient
{
    /// <param name="messages">Role/content pairs, in order.</param>
    /// <param name="schemaName">Name for <c>response_format.json_schema.name</c>.</param>
    /// <param name="schema">Full local schema; the client sends <see cref="UpstreamSchema.ForUpstream"/> of it.</param>
    Task<UpstreamResult> CompleteAsync(IReadOnlyList<(string Role, string Content)> messages,
        string schemaName, JsonNode schema, CancellationToken ct);
}
