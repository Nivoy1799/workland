using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Worktown.Gateway;
using Worktown.Gateway.Config;
using Worktown.Gateway.Moderation;
using Worktown.Gateway.Prompts;
using Worktown.Gateway.Validation;

namespace Worktown.NameBake;

/// <summary>
/// Spec 056b: <c>publish</c>. Takes the review rows marked <c>ok</c> whose item is in the current reachable set, checks
/// each name and description again (the item_name.v1 schema, <see cref="VeganContentRule"/>, the registered
/// <see cref="IModerator"/>; a reviewer may have edited them) and writes the name book (<see cref="NameBookWriter"/>,
/// <c>.tmp</c> then replace). Any failed check, or a decision other than <c>ok</c>, <c>no</c> or empty, is exit 2 and
/// writes nothing. Needs <c>GATEWAY_MODEL</c> (it is part of the cache key the rows refer to), no key and no token.
/// Prints published, pending, rejected and stale (not reachable any more) counts.
/// </summary>
internal static class PublishCommand
{
    public const int ExitOk = 0;
    public const int ExitBadSetup = 1;
    public const int ExitCheckFailed = 2;

    public sealed record Settings(string ReachablePath, string ReviewPath, string OutPath);

    public sealed record Result(int ExitCode, int Published, int Pending, int Rejected, int Stale);

    public static async Task<Result> RunAsync(Settings settings, IConfiguration config, Action<IServiceCollection>? configureServices,
        TextWriter output, TextWriter error, CancellationToken ct)
    {
        var model = config[GatewayOptions.ModelKey] ?? "";
        if (model.Length == 0 || model.Contains('|') || model.Any(char.IsWhiteSpace))
        {
            error.WriteLine($"publish: {GatewayOptions.ModelKey} is required (no '|' or whitespace).");
            return new Result(ExitBadSetup, 0, 0, 0, 0);
        }
        ReachableFile reachable;
        IReadOnlyList<ReviewRow> rows;
        try
        {
            reachable = ReachableFile.Load(settings.ReachablePath);
            rows = ReviewSheet.Load(settings.ReviewPath);
        }
        catch (Exception e) when (e is InvalidDataException || e is IOException || e is UnauthorizedAccessException)
        {
            error.WriteLine("publish: " + e.Message);
            return new Result(ExitBadSetup, 0, 0, 0, 0);
        }

        var services = new ServiceCollection();
        services.AddItemNamePipeline(config);
        configureServices?.Invoke(services);
        await using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<SchemaValidator>();
        var moderator = provider.GetRequiredService<IModerator>();

        var byKey12 = new Dictionary<string, ReachableFile.Item>(StringComparer.Ordinal);
        foreach (var item in reachable.Items)
        {
            if (!byKey12.TryAdd(BakeCommand.KeyOf(validator, model, item.Hash)[..12], item))
            {
                error.WriteLine("publish: two reachable items share a key prefix; the review sheet cannot tell them apart.");
                return new Result(ExitBadSetup, 0, 0, 0, 0);
            }
        }

        int pending = 0, rejected = 0, stale = 0;
        var failures = new List<string>();
        var book = new List<(string Hash, string Name, string Description)>();
        foreach (var row in rows)
        {
            if (row.Decision != ReviewRow.Ok && row.Decision != ReviewRow.No && row.Decision.Length != 0)
            {
                failures.Add($"row {row.Key12}: decision must be ok, no or empty");
                continue;
            }
            if (!byKey12.TryGetValue(row.Key12, out var item))
            {
                stale++;
                continue;
            }
            if (row.Decision.Length == 0) { pending++; continue; }
            if (row.Decision == ReviewRow.No) { rejected++; continue; }

            var json = new JsonObject { ["name"] = row.Name, ["description"] = row.Description }.ToJsonString();
            if (!validator.TryValidate(json, out var payload))
            {
                failures.Add($"row {row.Key12}: fails the {validator.ContentType} v{validator.SchemaVersion} schema");
                continue;
            }
            var verdict = VeganContentRule.Check(payload);
            if (verdict.Allowed) verdict = await moderator.CheckAsync(validator.ContentType, payload, ct).ConfigureAwait(false);
            if (!verdict.Allowed)
            {
                failures.Add($"row {row.Key12}: rejected ({verdict.ReasonCode})");
                continue;
            }
            book.Add((item.Hash, row.Name, row.Description));
        }
        if (failures.Count > 0)
        {
            foreach (var f in failures) error.WriteLine("publish: " + f);
            error.WriteLine("publish: nothing written.");
            return new Result(ExitCheckFailed, 0, pending, rejected, stale);
        }

        var contentVersion = $"{validator.ContentType}.s{validator.SchemaVersion}.p{ItemNamePrompt.Version}";
        var text = NameBookWriter.Write(reachable.SetHash, contentVersion, model, moderator.GetType().Name, book);
        try
        {
            NameBookWriter.WriteAtomic(settings.OutPath, text);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            error.WriteLine("publish: could not write the name book: " + e.Message);
            return new Result(ExitBadSetup, 0, pending, rejected, stale);
        }
        output.WriteLine($"publish: published={book.Count} pending={pending} rejected={rejected} stale={stale} -> {settings.OutPath}");
        return new Result(ExitOk, book.Count, pending, rejected, stale);
    }
}
