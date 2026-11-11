using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Worktown.Gateway.Config;

namespace Worktown.Gateway.Cache;

/// <summary>Process-local cache with first-write-wins semantics and a max entry count.</summary>
public sealed class InMemoryContentCache : IContentCache
{
    private readonly ConcurrentDictionary<string, CachedContent> _entries = new(StringComparer.Ordinal);
    private readonly int _maxEntries;
    private readonly ILogger<InMemoryContentCache> _logger;

    public InMemoryContentCache(IOptions<GatewayOptions> options, ILogger<InMemoryContentCache> logger)
        : this(options.Value.CacheMaxEntries, logger)
    {
    }

    public InMemoryContentCache(int maxEntries, ILogger<InMemoryContentCache> logger)
    {
        if (maxEntries < 1) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        _maxEntries = maxEntries;
        _logger = logger;
    }

    public int Count => _entries.Count;

    public Task<CachedContent?> GetAsync(string key, CancellationToken ct) =>
        Task.FromResult(_entries.TryGetValue(key, out var value) ? value : null);

    public Task SetAsync(string key, CachedContent value, CancellationToken ct)
    {
        if (_entries.ContainsKey(key)) return Task.CompletedTask;
        if (_entries.Count >= _maxEntries)
        {
            _logger.LogWarning("Content cache full ({MaxEntries} entries); entry not added", _maxEntries);
            return Task.CompletedTask;
        }
        _entries.TryAdd(key, value);
        return Task.CompletedTask;
    }
}
