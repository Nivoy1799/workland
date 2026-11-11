using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Worktown.Gateway.Api;
using Worktown.Gateway.Cache;
using Worktown.Gateway.Config;
using Worktown.Gateway.Moderation;
using Worktown.Gateway.Pipeline;
using Worktown.Gateway.Upstream;
using Worktown.Gateway.Validation;
using Xunit;

namespace Worktown.Gateway.Tests;

/// <summary>The pipeline on fake time with a scripted upstream (no HTTP at all).</summary>
public class PipelineRetryTests
{
    private static readonly ItemNameRequest Fries = new(GatewayFactory.FriesHash, GatewayFactory.FriesTree,
        "crispy fried potato sticks", new[] { "fried" });

    private sealed class ScriptedUpstream : IUpstreamClient
    {
        private readonly Queue<UpstreamResult> _results;

        public ScriptedUpstream(params UpstreamResult[] results) => _results = new Queue<UpstreamResult>(results);

        public int Calls { get; private set; }

        public Task<UpstreamResult> CompleteAsync(IReadOnlyList<(string Role, string Content)> messages,
            string schemaName, JsonNode schema, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(_results.Dequeue());
        }
    }

    private static ItemNamePipeline Pipeline(IUpstreamClient upstream, TimeProvider time) => new(
        new InMemoryContentCache(10, NullLogger<InMemoryContentCache>.Instance),
        upstream,
        SchemaValidator.ForItemNameV1(),
        new PlaceholderDenyListModerator(new[] { "forbiddenword" }),
        Options.Create(new GatewayOptions { Model = GatewayFactory.Model }),
        time,
        NullLogger<ItemNamePipeline>.Instance);

    private static UpstreamResult Valid() => new UpstreamResult.Ok(StubUpstreamHandler.ValidContent, "stop");

    private static async Task WaitForTimerAsync(ManualTimeProvider time)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (time.PendingTimerCount == 0 && DateTime.UtcNow < deadline) await Task.Delay(1);
    }

    [Fact]
    public async Task Upstream429WaitsTheBackoffOnInjectedTimeBeforeRetrying()
    {
        var time = new ManualTimeProvider();
        var upstream = new ScriptedUpstream(new UpstreamResult.HttpError(429), Valid());
        var run = Pipeline(upstream, time).RunAsync(Fries, GatewayFactory.ServerId, CancellationToken.None);

        await WaitForTimerAsync(time);
        Assert.Equal(1, time.PendingTimerCount);
        Assert.Equal(1, upstream.Calls);

        time.Advance(ItemNamePipeline.RateLimitedBackoff - TimeSpan.FromTicks(1));
        Assert.False(run.IsCompleted);
        Assert.Equal(1, upstream.Calls);

        time.Advance(TimeSpan.FromTicks(1));
        var outcome = await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(200, outcome.StatusCode);
        Assert.Equal(2, upstream.Calls);
    }

    [Fact]
    public async Task Upstream5xxRetriesWithoutBackoff()
    {
        var time = new ManualTimeProvider();
        var upstream = new ScriptedUpstream(new UpstreamResult.HttpError(503), Valid());
        var outcome = await Pipeline(upstream, time).RunAsync(Fries, GatewayFactory.ServerId, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(200, outcome.StatusCode);
        Assert.Equal(2, upstream.Calls);
        Assert.Equal(0, time.PendingTimerCount);
    }

    [Fact]
    public async Task Two429sEndAsUpstreamErrorWithOneBackoff()
    {
        var time = new ManualTimeProvider();
        var upstream = new ScriptedUpstream(new UpstreamResult.HttpError(429), new UpstreamResult.HttpError(429));
        var run = Pipeline(upstream, time).RunAsync(Fries, GatewayFactory.ServerId, CancellationToken.None);
        await WaitForTimerAsync(time);
        time.Advance(ItemNamePipeline.RateLimitedBackoff);
        var outcome = await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ErrorResponse.UpstreamError, outcome.ErrorCode);
        Assert.Equal(2, upstream.Calls);
        Assert.Equal(0, time.PendingTimerCount);
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    public async Task RedirectIsAFinalUpstreamError(int status)
    {
        var upstream = new ScriptedUpstream(new UpstreamResult.HttpError(status), Valid());
        var outcome = await Pipeline(upstream, new ManualTimeProvider()).RunAsync(Fries, GatewayFactory.ServerId, CancellationToken.None);
        Assert.Equal(ErrorResponse.UpstreamError, outcome.ErrorCode);
        Assert.Equal(1, upstream.Calls);
    }
}
