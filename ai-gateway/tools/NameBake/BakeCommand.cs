using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Worktown.Gateway;
using Worktown.Gateway.Api;
using Worktown.Gateway.Cache;
using Worktown.Gateway.Config;
using Worktown.Gateway.Moderation;
using Worktown.Gateway.Pipeline;
using Worktown.Gateway.Prompts;
using Worktown.Gateway.Validation;

namespace Worktown.NameBake;

/// <summary>
/// Spec 056b: <c>bake</c>. For each reachable item in hash order, one call in flight: an <see cref="ItemNameRequest"/> from
/// its hash, tree, plain name and tags through the spec 006 <see cref="ItemNamePipeline"/> (built in-process by
/// <see cref="GatewayServices.AddItemNamePipeline"/>: the same upstream client, schema validator, vegan rule, registered
/// moderator and cache key) with a <see cref="FileContentCache"/>. A cached item costs nothing; a review row marked
/// <c>no</c> has its cache entry removed and is generated once more (counted in the limit). Stops after
/// <see cref="Settings.Limit"/> upstream misses (exit 0) or <see cref="MaxErrorsInARow"/> upstream errors in a row (exit 3).
/// <see cref="Settings.DryRun"/> asks nothing and needs only <c>GATEWAY_MODEL</c>. Then the review sheet is merged. Prints
/// counts only: never the key, the prompt, upstream output or a header. Needs no <c>GATEWAY_SERVICE_TOKEN</c>.
/// </summary>
internal static class BakeCommand
{
    public const string ServerId = "offline-bake";
    public const int MaxErrorsInARow = 5;
    public const int ExitOk = 0;
    public const int ExitBadSetup = 1;
    public const int ExitTooManyErrors = 3;

    /// <summary>The offline files, the most upstream misses (null: no limit) and whether to ask nothing.</summary>
    public sealed record Settings(string ReachablePath, string CachePath, string ReviewPath, int? Limit, bool DryRun);

    /// <summary>What a bake did (a dry run fills <see cref="Cached"/>, <see cref="WouldAsk"/> and <see cref="Skipped"/>).</summary>
    public sealed class Counts
    {
        public int Items { get; set; }
        public int Cached { get; set; }
        public int Generated { get; set; }
        public int Regenerated { get; set; }
        public int SchemaInvalid { get; set; }
        public int VeganRejected { get; set; }
        public int ModeratorRejected { get; set; }
        public int Errors { get; set; }
        public int Skipped { get; set; }
        public int WouldAsk { get; set; }
        public int Misses { get; set; }
        public bool LimitReached { get; set; }
        public bool StoppedOnErrors { get; set; }
    }

    public sealed record Result(int ExitCode, Counts Counts);

    public static async Task<Result> RunAsync(Settings settings, IConfiguration config, Action<IServiceCollection>? configureServices,
        TextWriter output, TextWriter error, CancellationToken ct)
    {
        var counts = new Counts();
        ReachableFile reachable;
        IReadOnlyList<ReviewRow> sheet;
        FileContentCache cache;
        try
        {
            reachable = ReachableFile.Load(settings.ReachablePath);
            sheet = ReviewSheet.Load(settings.ReviewPath);
            cache = new FileContentCache(settings.CachePath);
        }
        catch (Exception e) when (e is InvalidDataException || e is IOException || e is UnauthorizedAccessException)
        {
            error.WriteLine("bake: " + e.Message);
            return new Result(ExitBadSetup, counts);
        }
        if (settings.Limit is < 0)
        {
            error.WriteLine("bake: --limit must be 0 or more.");
            return new Result(ExitBadSetup, counts);
        }
        counts.Items = reachable.Items.Count;
        var rows = sheet.ToDictionary(r => r.Key12, StringComparer.Ordinal);
        var keys = SchemaValidator.ForItemNameV1();

        if (settings.DryRun)
        {
            var model = config[GatewayOptions.ModelKey] ?? "";
            if (model.Length == 0)
            {
                error.WriteLine($"bake: {GatewayOptions.ModelKey} is required.");
                return new Result(ExitBadSetup, counts);
            }
            foreach (var item in reachable.Items)
            {
                var request = RequestOf(item);
                var key = KeyOf(keys, model, item.Hash);
                if (!RequestValidator.IsValid(request))
                {
                    counts.Skipped++;
                    continue;
                }
                var cached = await cache.GetAsync(key, ct).ConfigureAwait(false);
                var redo = cached is not null && rows.TryGetValue(key[..12], out var row) && row.Decision == ReviewRow.No;
                if (cached is not null && !redo) counts.Cached++;
                else counts.WouldAsk++;
            }
            output.WriteLine($"bake --dry-run: items={counts.Items} cached={counts.Cached} would-ask={counts.WouldAsk} skipped={counts.Skipped}");
            return new Result(ExitOk, counts);
        }

        var services = new ServiceCollection();
        services.AddItemNamePipeline(config);
        configureServices?.Invoke(services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        GatewayOptions options;
        try
        {
            options = provider.GetRequiredService<IOptions<GatewayOptions>>().Value;
        }
        catch (OptionsValidationException e)
        {
            foreach (var failure in e.Failures) error.WriteLine("bake: " + failure); // names variables, never values
            return new Result(ExitBadSetup, counts);
        }
        var moderator = new CountingModerator(provider.GetRequiredService<IModerator>());
        using var scope = provider.CreateScope();
        var pipeline = ActivatorUtilities.CreateInstance<ItemNamePipeline>(scope.ServiceProvider, cache, moderator);

        var regenerated = new Dictionary<string, string>(StringComparer.Ordinal);
        var skippedKeys = new List<string>();
        var mismatchedKeys = new List<string>();
        var errorsInARow = 0;
        try
        {
            foreach (var item in reachable.Items)
            {
                var request = RequestOf(item);
                var key = KeyOf(keys, options.Model, item.Hash);
                var key12 = key[..12];
                if (!RequestValidator.IsValid(request))
                {
                    counts.Skipped++;
                    skippedKeys.Add(key12);
                    continue;
                }
                var cached = await cache.GetAsync(key, ct).ConfigureAwait(false);
                var redo = cached is not null && rows.TryGetValue(key12, out var row) && row.Decision == ReviewRow.No;
                if (cached is not null && !redo)
                {
                    if (!string.Equals(cached.InputHash, ItemNamePrompt.InputHash(request), StringComparison.Ordinal))
                    {
                        counts.Errors++;
                        mismatchedKeys.Add(key12);
                    }
                    else counts.Cached++;
                    continue;
                }
                if (settings.Limit is int limit && counts.Misses >= limit)
                {
                    counts.LimitReached = true;
                    break;
                }
                if (redo)
                {
                    cache.Remove(key);
                    regenerated[key12] = rows[key12].Name;
                }
                counts.Misses++;
                moderator.Rejected = false;
                var outcome = await pipeline.RunAsync(request, ServerId, ct).ConfigureAwait(false);
                switch (outcome.ErrorCode)
                {
                    case null:
                        counts.Generated++;
                        if (redo) counts.Regenerated++;
                        errorsInARow = 0;
                        break;
                    case ErrorResponse.UpstreamInvalidOutput:
                        counts.SchemaInvalid++;
                        errorsInARow = 0;
                        break;
                    case ErrorResponse.ContentRejected:
                        if (moderator.Rejected) counts.ModeratorRejected++;
                        else counts.VeganRejected++;
                        errorsInARow = 0;
                        break;
                    case ErrorResponse.UpstreamError:
                    case ErrorResponse.UpstreamTimeout:
                        counts.Errors++;
                        errorsInARow++;
                        break;
                    default:
                        counts.Errors++;
                        break;
                }
                if (errorsInARow >= MaxErrorsInARow)
                {
                    counts.StoppedOnErrors = true;
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            error.WriteLine("bake: cancelled; the cache keeps what was generated.");
        }

        // The review sheet: one row per cached reachable item.
        var current = new List<(ReachableFile.Item, string, string, string)>();
        foreach (var item in reachable.Items)
        {
            var key = KeyOf(keys, options.Model, item.Hash);
            var cached = await cache.GetAsync(key, CancellationToken.None).ConfigureAwait(false);
            if (cached is null || !string.Equals(cached.InputHash, ItemNamePrompt.InputHash(RequestOf(item)), StringComparison.Ordinal))
                continue;
            using var doc = JsonDocument.Parse(cached.PayloadJson);
            current.Add((item, key[..12], doc.RootElement.GetProperty("name").GetString() ?? "",
                doc.RootElement.GetProperty("description").GetString() ?? ""));
        }
        IReadOnlyList<ReviewRow> merged;
        try
        {
            merged = ReviewSheet.Merge(sheet, current, regenerated);
            ReviewSheet.Save(settings.ReviewPath, merged);
        }
        catch (Exception e) when (e is InvalidDataException || e is IOException || e is UnauthorizedAccessException)
        {
            error.WriteLine("bake: could not write the review sheet: " + e.Message);
            return new Result(ExitBadSetup, counts);
        }

        output.WriteLine($"bake: items={counts.Items} cached={counts.Cached} generated={counts.Generated} regenerated={counts.Regenerated} "
            + $"schema-invalid={counts.SchemaInvalid} vegan-rejected={counts.VeganRejected} moderator-rejected={counts.ModeratorRejected} "
            + $"errors={counts.Errors} skipped={counts.Skipped}");
        if (skippedKeys.Count > 0) output.WriteLine("bake: skipped (request rules) key prefixes: " + string.Join(' ', skippedKeys));
        if (mismatchedKeys.Count > 0)
            output.WriteLine("bake: cached entries that no longer match their item (plain name or tags changed): " + string.Join(' ', mismatchedKeys));
        var pending = merged.Count(r => r.Decision.Length == 0);
        output.WriteLine($"bake: review sheet rows={merged.Count} pending={pending}");
        if (counts.LimitReached) output.WriteLine($"bake: stopped at the limit of {settings.Limit} upstream misses; run again to go on.");
        if (counts.StoppedOnErrors)
        {
            output.WriteLine($"bake: stopped after {MaxErrorsInARow} upstream errors in a row.");
            return new Result(ExitTooManyErrors, counts);
        }
        return new Result(ExitOk, counts);
    }

    private static ItemNameRequest RequestOf(ReachableFile.Item item) =>
        new(item.Hash, item.CanonicalTree, item.FallbackName, item.Tags);

    /// <summary>The spec 006 cache key of the item (as <see cref="ItemNamePipeline"/> computes it).</summary>
    internal static string KeyOf(SchemaValidator validator, string model, string itemHash) =>
        CacheKey.Compute(validator.ContentType, validator.SchemaVersion, ItemNamePrompt.Version, model, itemHash);

    /// <summary>The registered moderator, noting whether it rejected (the vegan rule runs before it, so a rejection it did not make is the vegan rule's).</summary>
    private sealed class CountingModerator : IModerator
    {
        private readonly IModerator _inner;

        public CountingModerator(IModerator inner) => _inner = inner;

        public bool Rejected { get; set; }

        public async Task<ModerationResult> CheckAsync(string contentType, JsonElement payload, CancellationToken ct)
        {
            var result = await _inner.CheckAsync(contentType, payload, ct).ConfigureAwait(false);
            if (!result.Allowed) Rejected = true;
            return result;
        }
    }
}
