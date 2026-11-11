using Microsoft.Extensions.Logging;
using Worktown.Gateway.Cache;
using Xunit;

namespace Worktown.Gateway.Tests;

public class InMemoryContentCacheTests
{
    private static CachedContent Entry(string payload) => new("item_name", GatewayFactory.FriesHash, GatewayFactory.FriesTree,
        new string('0', 64), payload, "item_name.s1.p1", GatewayFactory.Model, DateTimeOffset.UnixEpoch);

    private static (InMemoryContentCache Cache, CapturingLoggerProvider Logs) Create(int maxEntries)
    {
        var logs = new CapturingLoggerProvider();
        var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        return (new InMemoryContentCache(maxEntries, factory.CreateLogger<InMemoryContentCache>()), logs);
    }

    [Fact]
    public async Task FirstWriteWins()
    {
        var (cache, _) = Create(10);
        await cache.SetAsync("k", Entry("{\"first\":1}"), CancellationToken.None);
        await cache.SetAsync("k", Entry("{\"second\":2}"), CancellationToken.None);
        var hit = await cache.GetAsync("k", CancellationToken.None);
        Assert.Equal("{\"first\":1}", hit!.PayloadJson);
    }

    [Fact]
    public async Task WhenFullTheEntryIsNotAddedAndAWarningIsLogged()
    {
        var (cache, logs) = Create(1);
        await cache.SetAsync("a", Entry("{\"a\":1}"), CancellationToken.None);
        Assert.DoesNotContain(logs.Entries, e => e.StartsWith("Warning"));

        await cache.SetAsync("b", Entry("{\"b\":1}"), CancellationToken.None);
        Assert.Null(await cache.GetAsync("b", CancellationToken.None));
        Assert.NotNull(await cache.GetAsync("a", CancellationToken.None));
        Assert.Contains(logs.Entries, e => e.StartsWith("Warning") && e.Contains("cache full"));
    }

    [Fact]
    public async Task RewritingAnExistingKeyWhenFullLogsNothing()
    {
        var (cache, logs) = Create(1);
        await cache.SetAsync("a", Entry("{\"a\":1}"), CancellationToken.None);
        await cache.SetAsync("a", Entry("{\"a\":2}"), CancellationToken.None);
        Assert.DoesNotContain(logs.Entries, e => e.StartsWith("Warning"));
        Assert.Equal("{\"a\":1}", (await cache.GetAsync("a", CancellationToken.None))!.PayloadJson);
    }

    [Fact]
    public void MaxEntriesBelowOneIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(0));
    }

    [Fact]
    public async Task MissReturnsNull()
    {
        var (cache, _) = Create(10);
        Assert.Null(await cache.GetAsync("nope", CancellationToken.None));
    }
}
