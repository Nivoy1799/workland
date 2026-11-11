using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Worktown.NameBake;

/// <summary>
/// Spec 056b: the offline name bake (D38), run by a developer through <c>scripts/bake-names.sh</c> from the repo root.
/// Configuration comes from the environment only (GATEWAY_* as spec 006; the script loads <c>.env</c>).
/// <code>
/// bake    [--limit N] [--dry-run] [--offline DIR]    exit 0 done, 1 bad setup, 3 five upstream errors in a row
/// publish [--offline DIR] [--out FILE]               exit 0 written, 1 bad setup, 2 a check failed (nothing written)
/// </code>
/// DIR defaults to <c>ai-gateway/offline</c> (reachable.v1.json, name-cache.jsonl, review.tsv), FILE to
/// <c>game/Assets/_Project/Resources/ItemNames.json</c>.
/// </summary>
public static class Program
{
    public const string DefaultOfflineDir = "ai-gateway/offline";
    public const string DefaultBookPath = "game/Assets/_Project/Resources/ItemNames.json";
    public const string ReachableFileName = "reachable.v1.json";
    public const string CacheFileName = "name-cache.jsonl";
    public const string ReviewFileName = "review.tsv";

    private const string Usage = "Usage: name-bake bake [--limit N] [--dry-run] [--offline DIR] | publish [--offline DIR] [--out FILE]";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || (args[0] != "bake" && args[0] != "publish"))
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }
        var command = args[0];
        var offline = DefaultOfflineDir;
        var outPath = DefaultBookPath;
        int? limit = null;
        var dryRun = false;
        for (var i = 1; i < args.Length; i++)
        {
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (args[i])
            {
                case "--offline" when Next() is { } dir: offline = dir; break;
                case "--out" when command == "publish" && Next() is { } file: outPath = file; break;
                case "--limit" when command == "bake" && int.TryParse(Next(), out var n) && n >= 0: limit = n; break;
                case "--dry-run" when command == "bake": dryRun = true; break;
                default:
                    Console.Error.WriteLine(Usage);
                    return 1;
            }
        }

        var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        // Warnings only (ids and codes, never secrets or text, spec 006); the pipeline's per-item lines stay quiet.
        static void Logging(IServiceCollection services) =>
            services.AddLogging(b => b.ClearProviders().AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning));

        if (command == "bake")
        {
            var settings = new BakeCommand.Settings(Path.Combine(offline, ReachableFileName), Path.Combine(offline, CacheFileName),
                Path.Combine(offline, ReviewFileName), limit, dryRun);
            var result = await BakeCommand.RunAsync(settings, config, Logging, Console.Out, Console.Error, cts.Token).ConfigureAwait(false);
            return result.ExitCode;
        }
        var publish = new PublishCommand.Settings(Path.Combine(offline, ReachableFileName), Path.Combine(offline, ReviewFileName), outPath);
        var published = await PublishCommand.RunAsync(publish, config, Logging, Console.Out, Console.Error, cts.Token).ConfigureAwait(false);
        return published.ExitCode;
    }
}
