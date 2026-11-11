using System.Text.Json;
using Microsoft.Extensions.Options;
using Worktown.Gateway.Api;
using Worktown.Gateway.Cache;
using Worktown.Gateway.Config;
using Worktown.Gateway.Moderation;
using Worktown.Gateway.Prompts;
using Worktown.Gateway.Upstream;
using Worktown.Gateway.Validation;

namespace Worktown.Gateway.Pipeline;

/// <summary>
/// The item_name pipeline after auth and rate limiting: request validation, cache lookup,
/// upstream call (at most <see cref="MaxAttempts"/>), schema validation, moderation, cache write.
/// Logs only ids, codes and timings; never secrets, prompts or upstream output.
/// </summary>
public sealed class ItemNamePipeline
{
    public const int MaxAttempts = 2;

    private readonly IContentCache _cache;
    private readonly IUpstreamClient _upstream;
    private readonly SchemaValidator _validator;
    private readonly IModerator _moderator;
    private readonly GatewayOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<ItemNamePipeline> _logger;

    public ItemNamePipeline(IContentCache cache, IUpstreamClient upstream, SchemaValidator validator,
        IModerator moderator, IOptions<GatewayOptions> options, TimeProvider time, ILogger<ItemNamePipeline> logger)
    {
        _cache = cache;
        _upstream = upstream;
        _validator = validator;
        _moderator = moderator;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    /// <summary>"item_name.s{schema}.p{prompt}".</summary>
    public string ContentVersion => $"{SchemaValidator.ItemNameContentType}.s{_validator.SchemaVersion}.p{ItemNamePrompt.Version}";

    public async Task<PipelineOutcome> RunAsync(ItemNameRequest request, string serverId, CancellationToken ct)
    {
        var started = _time.GetTimestamp();
        var trace = new RequestTrace();
        var outcome = await RunCoreAsync(request, serverId, trace, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "item_name server={ServerId} item={ItemHash} key={KeyPrefix} cache={Cache} attempts={Attempts} outcome={Outcome} latency_ms={LatencyMs}",
            RequestValidator.IsValidServerId(serverId) ? serverId : "-",
            trace.ItemHash ?? "-",
            trace.KeyPrefix ?? "-",
            trace.Cache,
            trace.Attempts,
            outcome.Code,
            (long)_time.GetElapsedTime(started).TotalMilliseconds);
        return outcome;
    }

    private async Task<PipelineOutcome> RunCoreAsync(ItemNameRequest request, string serverId, RequestTrace trace, CancellationToken ct)
    {
        if (!RequestValidator.IsValidServerId(serverId) || !RequestValidator.IsValid(request))
            return PipelineOutcome.Error(ErrorResponse.InvalidRequest);

        var itemHash = request.ItemHash!;
        var tree = request.CanonicalTree!;
        trace.ItemHash = itemHash;
        var key = CacheKey.Compute(_validator.ContentType, _validator.SchemaVersion, ItemNamePrompt.Version, _options.Model, itemHash);
        trace.KeyPrefix = key[..12];

        var cached = await _cache.GetAsync(key, ct).ConfigureAwait(false);
        if (cached is not null)
        {
            trace.Cache = "hit";
            if (!string.Equals(cached.CanonicalTree, tree, StringComparison.Ordinal))
            {
                _logger.LogWarning("item_name tree mismatch server={ServerId} item={ItemHash} key={KeyPrefix}",
                    serverId, itemHash, trace.KeyPrefix);
                return PipelineOutcome.Error(ErrorResponse.TreeMismatch);
            }
            using var doc = JsonDocument.Parse(cached.PayloadJson);
            return PipelineOutcome.Success(ToResponse(itemHash, doc.RootElement, cacheHit: true, cached.ContentVersion, cached.Model));
        }
        trace.Cache = "miss";

        var messages = ItemNamePrompt.Build(request);
        string lastError = ErrorResponse.UpstreamError;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            trace.Attempts = attempt;
            var result = await _upstream.CompleteAsync(messages, _validator.ContentType, _validator.Schema, ct).ConfigureAwait(false);
            switch (result)
            {
                case UpstreamResult.Ok ok:
                    if (ok.FinishReason != "stop" || !_validator.TryValidate(ok.Content, out var payload))
                    {
                        lastError = ErrorResponse.UpstreamInvalidOutput;
                        continue;
                    }
                    var verdict = await _moderator.CheckAsync(_validator.ContentType, payload, ct).ConfigureAwait(false);
                    if (!verdict.Allowed) return PipelineOutcome.Error(ErrorResponse.ContentRejected);
                    var entry = new CachedContent(_validator.ContentType, itemHash, tree, ok.Content!,
                        ContentVersion, _options.Model, _time.GetUtcNow());
                    await _cache.SetAsync(key, entry, ct).ConfigureAwait(false);
                    return PipelineOutcome.Success(ToResponse(itemHash, payload, cacheHit: false, ContentVersion, _options.Model));
                case UpstreamResult.HttpError http:
                    lastError = ErrorResponse.UpstreamError;
                    if (!http.IsRetryable) return PipelineOutcome.Error(lastError);
                    continue;
                case UpstreamResult.Timeout:
                    lastError = ErrorResponse.UpstreamTimeout;
                    continue;
                default:
                    lastError = ErrorResponse.UpstreamError;
                    continue;
            }
        }
        return PipelineOutcome.Error(lastError);
    }

    private static ItemNameResponse ToResponse(string itemHash, JsonElement payload, bool cacheHit, string contentVersion, string model) =>
        new(itemHash, payload.GetProperty("name"u8).GetString()!, payload.GetProperty("description"u8).GetString()!,
            cacheHit, contentVersion, model);

    private sealed class RequestTrace
    {
        public string? ItemHash { get; set; }
        public string? KeyPrefix { get; set; }
        public string Cache { get; set; } = "-";
        public int Attempts { get; set; }
    }
}
