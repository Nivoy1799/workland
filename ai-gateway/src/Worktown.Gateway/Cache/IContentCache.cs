namespace Worktown.Gateway.Cache;

/// <summary>Generated-content cache keyed by <see cref="CacheKey"/>. Persistence is O4 (OPEN).</summary>
public interface IContentCache
{
    Task<CachedContent?> GetAsync(string key, CancellationToken ct);

    /// <summary>First write wins: an existing entry is never replaced.</summary>
    Task SetAsync(string key, CachedContent value, CancellationToken ct);
}
