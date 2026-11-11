using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Worktown.Gateway.Config;
using Worktown.Gateway.Moderation;
using Worktown.Gateway.Prompts;
using Worktown.Gateway.Upstream;
using Worktown.NameBake;
using Worktown.Sim.Cooking;
using Xunit;

namespace Worktown.Gateway.Tests;

/// <summary>
/// Spec 056b: a temp offline folder with a fixture reachable file (written with the /sim writer, spec 056a), the stub
/// upstream, a test moderator (deny list "forbiddenword") and captured logs. No network, no real key, no service token.
/// </summary>
internal sealed class OfflineFixture : IDisposable
{
    public const string FriesName = "crispy fried potato sticks";

    public OfflineFixture(int items = 3)
    {
        Dir = Path.Combine(Path.GetTempPath(), "worktown-offline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Dir);
        var list = new List<ReachableItem>
        {
            new(GatewayFactory.FriesHash, GatewayFactory.FriesTree, FriesName, Array.Empty<string>(), new[] { "fries", "veggie_box" }, true,
                new[] { "crispy" }),
        };
        for (var i = 1; i < items; i++)
            list.Add(new ReachableItem("b" + new string('0', 62) + i.ToString("x"), "boil(time=medium;potato)", "boiled potato " + Word(i),
                Array.Empty<string>(), new[] { "potato_soup" }, i == 1, Array.Empty<string>()));
        WriteReachable(list);
    }

    private static string Word(int i) => new[] { "one", "two", "three", "four", "five", "six", "seven", "eight", "nine" }[(i - 1) % 9];

    public string Dir { get; }
    public string ReachablePath => Path.Combine(Dir, "reachable.v1.json");
    public string CachePath => Path.Combine(Dir, "name-cache.jsonl");
    public string ReviewPath => Path.Combine(Dir, "review.tsv");
    public string BookPath => Path.Combine(Dir, "ItemNames.json");

    public StubUpstreamHandler Stub { get; } = new();
    public CapturingLoggerProvider Logs { get; } = new();
    public StringWriter Out { get; } = new();
    public StringWriter Err { get; } = new();
    public IModerator Moderator { get; set; } = new PlaceholderDenyListModerator(new[] { "forbiddenword" });

    public void WriteReachable(IReadOnlyList<ReachableItem> items) => File.WriteAllText(ReachablePath, ReachableItemsText.Format(items));

    public static IConfiguration Config(bool upstream = true)
    {
        var values = new Dictionary<string, string?> { [GatewayOptions.ModelKey] = GatewayFactory.Model };
        if (upstream)
        {
            values[GatewayOptions.UpstreamBaseUrlKey] = GatewayFactory.BaseUrl;
            values[GatewayOptions.UpstreamApiKeyKey] = GatewayFactory.ApiKey;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    public void Services(IServiceCollection services)
    {
        services.AddHttpClient(OpenAiCompatibleClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Stub);
        services.AddSingleton(Moderator);
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
    }

    public Task<BakeCommand.Result> BakeAsync(int? limit = null, bool dryRun = false, IConfiguration? config = null) =>
        BakeCommand.RunAsync(new BakeCommand.Settings(ReachablePath, CachePath, ReviewPath, limit, dryRun), config ?? Config(), Services,
            Out, Err, CancellationToken.None);

    public Task<PublishCommand.Result> PublishAsync(IConfiguration? config = null) =>
        PublishCommand.RunAsync(new PublishCommand.Settings(ReachablePath, ReviewPath, BookPath), config ?? Config(upstream: false), Services,
            Out, Err, CancellationToken.None);

    public IReadOnlyList<ReviewRow> Sheet() => ReviewSheet.Load(ReviewPath);

    /// <summary>Sets the decision (and optionally the name) of the row of <paramref name="plainName"/>.</summary>
    public void Decide(string plainName, string decision, string? name = null, string? description = null)
    {
        var rows = Sheet().Select(r => r.PlainName == plainName
            ? r with { Decision = decision, Name = name ?? r.Name, Description = description ?? r.Description }
            : r).ToList();
        File.WriteAllText(ReviewPath, ReviewSheet.Format(rows));
    }

    public void Dispose()
    {
        if (Dir.Length > 0 && Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true);
    }
}

/// <summary>Spec 056b AC4 and AC8: the bake.</summary>
public class BakeCommandTests
{
    private static string Content(string name, string description = "A warm plate from the town kitchen.") =>
        "{\"name\":\"" + name + "\",\"description\":\"" + description + "\"}";

    [Fact]
    public async Task ABakeOfThreeItemsMakesThreeCallsAndASecondBakeNone()
    {
        using var f = new OfflineFixture();
        var first = await f.BakeAsync();
        Assert.Equal(BakeCommand.ExitOk, first.ExitCode);
        Assert.Equal(3, f.Stub.CallCount);
        Assert.Equal(3, first.Counts.Generated);
        Assert.Equal(3, File.ReadAllLines(f.CachePath).Length);
        var rows = f.Sheet();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal("", r.Decision));
        Assert.All(rows, r => Assert.Equal("Golden Spud Sticks", r.Name));

        var second = await f.BakeAsync();
        Assert.Equal(BakeCommand.ExitOk, second.ExitCode);
        Assert.Equal(3, f.Stub.CallCount);
        Assert.Equal(3, second.Counts.Cached);
        Assert.Equal(0, second.Counts.Generated);
        Assert.Contains("items=3 cached=3 generated=0", f.Out.ToString());
    }

    [Fact]
    public async Task LimitOneMakesOneCall()
    {
        using var f = new OfflineFixture();
        var result = await f.BakeAsync(limit: 1);
        Assert.Equal(BakeCommand.ExitOk, result.ExitCode);
        Assert.Equal(1, f.Stub.CallCount);
        Assert.True(result.Counts.LimitReached);
        Assert.Single(f.Sheet());

        await f.BakeAsync(limit: 1);
        Assert.Equal(2, f.Stub.CallCount);
        Assert.Equal(2, f.Sheet().Count);
    }

    [Fact]
    public async Task SchemaInvalidVeganAndModeratorRejectionsAreCountedAndNotCached()
    {
        using var f = new OfflineFixture();
        // Items go in hash order: the fries fixture (2ab1...), then b0...01, then b0...02.
        f.Stub.EnqueueContent("{\"name\":\"x\"}");
        f.Stub.EnqueueContent("{\"name\":\"x\"}");
        f.Stub.EnqueueContent(Content("Forbiddenword Bites"));
        f.Stub.EnqueueContent(Content("Cheese Bites"));
        var result = await f.BakeAsync();
        Assert.Equal(BakeCommand.ExitOk, result.ExitCode);
        Assert.Equal(1, result.Counts.SchemaInvalid);
        Assert.Equal(1, result.Counts.ModeratorRejected);
        Assert.Equal(1, result.Counts.VeganRejected);
        Assert.Equal(0, result.Counts.Generated);
        Assert.Equal(4, f.Stub.CallCount); // the invalid output is asked twice (spec 006: two attempts)
        Assert.Equal("", File.Exists(f.CachePath) ? File.ReadAllText(f.CachePath) : "");
        Assert.Empty(f.Sheet());
        Assert.Contains("schema-invalid=1 vegan-rejected=1 moderator-rejected=1", f.Out.ToString());

        var again = await f.BakeAsync(); // nothing was cached: all three are asked again
        Assert.Equal(3, again.Counts.Generated);
    }

    [Fact]
    public async Task DryRunMakesNoCallAndNeedsOnlyTheModel()
    {
        using var f = new OfflineFixture();
        var dry = await f.BakeAsync(dryRun: true, config: OfflineFixture.Config(upstream: false));
        Assert.Equal(BakeCommand.ExitOk, dry.ExitCode);
        Assert.Equal(0, f.Stub.CallCount);
        Assert.Equal(3, dry.Counts.WouldAsk);
        Assert.False(File.Exists(f.ReviewPath));

        await f.BakeAsync(limit: 2);
        var after = await f.BakeAsync(dryRun: true);
        Assert.Equal(2, f.Stub.CallCount);
        Assert.Equal(2, after.Counts.Cached);
        Assert.Equal(1, after.Counts.WouldAsk);
        Assert.Contains("cached=2 would-ask=1", f.Out.ToString());
    }

    [Fact]
    public async Task FiveUpstreamErrorsInARowStopWithExit3()
    {
        using var f = new OfflineFixture(items: 7);
        f.Stub.Default = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var result = await f.BakeAsync();
        Assert.Equal(BakeCommand.ExitTooManyErrors, result.ExitCode);
        Assert.True(result.Counts.StoppedOnErrors);
        Assert.Equal(5, result.Counts.Errors);
        Assert.Equal(10, f.Stub.CallCount); // five items, two attempts each
    }

    [Fact]
    public async Task AnErrorStreakIsResetByASuccess()
    {
        using var f = new OfflineFixture(items: 7);
        for (var i = 0; i < 8; i++) f.Stub.EnqueueStatus(HttpStatusCode.InternalServerError); // four items fail
        var result = await f.BakeAsync(); // then the fifth succeeds, and the rest
        Assert.Equal(BakeCommand.ExitOk, result.ExitCode);
        Assert.Equal(4, result.Counts.Errors);
        Assert.Equal(3, result.Counts.Generated);
    }

    [Fact]
    public async Task AnItemFailingTheRequestRulesIsSkipped()
    {
        using var f = new OfflineFixture();
        f.WriteReachable(new[]
        {
            new ReachableItem(GatewayFactory.FriesHash, GatewayFactory.FriesTree, "Falafel (4)", Array.Empty<string>(), new[] { "falafel" },
                true, Array.Empty<string>()),
        });
        var result = await f.BakeAsync();
        Assert.Equal(1, result.Counts.Skipped);
        Assert.Equal(0, f.Stub.CallCount);
        Assert.Contains("skipped (request rules) key prefixes: ", f.Out.ToString());
    }

    [Fact]
    public async Task AC8_OutputAndLogsHoldNoKeyNorPromptAndNoServiceTokenIsNeeded()
    {
        using var f = new OfflineFixture();
        f.Stub.EnqueueContent("{\"name\":\"x\"}");
        var result = await f.BakeAsync(); // the config has no GATEWAY_SERVICE_TOKEN
        Assert.Equal(BakeCommand.ExitOk, result.ExitCode);
        Assert.Equal(4, f.Stub.CallCount); // one invalid answer and its retry, then two more items
        Assert.Equal(3, result.Counts.Generated);
        var all = f.Out + "\n" + f.Err + "\n" + f.Logs.AllText;
        Assert.NotEmpty(f.Logs.Entries);
        Assert.DoesNotContain(GatewayFactory.ApiKey, all);
        Assert.DoesNotContain(ItemNamePrompt.SystemText, all);
        Assert.DoesNotContain("cozy, low-poly town", all);
        Assert.DoesNotContain("Plain name:", all);
        Assert.DoesNotContain("Golden Spud Sticks", f.Out + "\n" + f.Err);
        Assert.DoesNotContain("Bearer", all);
        // The key reached the upstream only, as the Authorization header (never in a body).
        Assert.DoesNotContain(GatewayFactory.ApiKey, string.Join("\n", f.Stub.Bodies));
    }

    [Fact]
    public async Task AMissingVariableFailsNamingItWithoutEchoingValues()
    {
        using var f = new OfflineFixture();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [GatewayOptions.UpstreamBaseUrlKey] = GatewayFactory.BaseUrl,
            [GatewayOptions.UpstreamApiKeyKey] = GatewayFactory.ApiKey,
        }).Build();
        var result = await f.BakeAsync(config: config);
        Assert.Equal(BakeCommand.ExitBadSetup, result.ExitCode);
        Assert.Contains(GatewayOptions.ModelKey, f.Err.ToString());
        Assert.DoesNotContain(GatewayFactory.ApiKey, f.Err.ToString());
        Assert.Equal(0, f.Stub.CallCount);

        var dry = await f.BakeAsync(dryRun: true, config: config);
        Assert.Equal(BakeCommand.ExitBadSetup, dry.ExitCode);
    }

    [Fact]
    public async Task AMissingReachableFileIsBadSetup()
    {
        using var f = new OfflineFixture();
        File.Delete(f.ReachablePath);
        Assert.Equal(BakeCommand.ExitBadSetup, (await f.BakeAsync()).ExitCode);
        Assert.Contains("item-list.sh", f.Err.ToString());
    }

    [Fact]
    public void TheCommittedReachableFileIsUpToDateWithTheSim()
    {
        var root = RepoRoot();
        var path = Path.Combine(root, "ai-gateway", "offline", "reachable.v1.json");
        var factory = new ItemFactory(KitchenItems.CreateItemCatalog(), OperationCatalog.CreateDefault());
        var expected = ReachableItemsText.Format(ReachableItems.Enumerate(factory, MenuRoutes.Default, ReachLimits.Default));
        Assert.True(File.ReadAllText(path) == expected,
            "reachable set changed: update the pin and rerun scripts/item-list.sh and bake-names.sh (spec 056)");
        var parsed = ReachableFile.Parse(expected);
        Assert.Equal(ReachableItemsText.SetHash(ReachableItems.Enumerate(factory, MenuRoutes.Default, ReachLimits.Default)), parsed.SetHash);
        Assert.All(parsed.Items, i => Assert.True(Worktown.Gateway.Api.RequestValidator.IsValid(
            new Worktown.Gateway.Api.ItemNameRequest(i.Hash, i.CanonicalTree, i.FallbackName, i.Tags)), i.CanonicalTree));
    }

    [Fact]
    public void TheReachableParserRejectsATamperedFile()
    {
        using var f = new OfflineFixture();
        var text = File.ReadAllText(f.ReachablePath);
        Assert.Throws<InvalidDataException>(() => ReachableFile.Parse(text.Replace("b" + new string('0', 62) + "1", "d" + new string('0', 63))));
        Assert.Throws<InvalidDataException>(() => ReachableFile.Parse(text.Replace("worktown-reachable-items", "other")));
        Assert.Throws<InvalidDataException>(() => ReachableFile.Parse("{"));
        Assert.Equal(3, ReachableFile.Parse(text).Items.Count);
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ai-gateway", "Gateway.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }
}
