using System.Text.Json;

namespace Worktown.Gateway.Moderation;

/// <summary>
/// Moderation seam. Runs after schema validation and before the cache write; a rejection is
/// final (not retried, not cached). The real approach is O6 (OPEN).
/// </summary>
public interface IModerator
{
    Task<ModerationResult> CheckAsync(string contentType, JsonElement payload, CancellationToken ct);
}
