using Worktown.Gateway.Cache;
using Xunit;

namespace Worktown.Gateway.Tests;

/// <summary>Spec 056b AC5: the JSON-lines cache of the offline bake.</summary>
public sealed class FileContentCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "worktown-filecache-" + Guid.NewGuid().ToString("N"));

    public FileContentCacheTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (_dir.Length > 0 && Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string CachePath => Path.Combine(_dir, "name-cache.jsonl");

    private static CachedContent Entry(string payload) => new("item_name", GatewayFactory.FriesHash, GatewayFactory.FriesTree,
        new string('0', 64), payload, "item_name.s1.p1", GatewayFactory.Model, DateTimeOffset.UnixEpoch);

    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task EntriesSurviveARestart()
    {
        var cache = new FileContentCache(CachePath);
        await cache.SetAsync("b", Entry("{\"n\":2}"), None);
        await cache.SetAsync("a", Entry("{\"n\":1}"), None);

        var again = new FileContentCache(CachePath);
        Assert.Equal(2, again.Count);
        var a = await again.GetAsync("a", None);
        Assert.Equal(Entry("{\"n\":1}"), a);
        Assert.Equal("{\"n\":2}", (await again.GetAsync("b", None))!.PayloadJson);
        Assert.Null(await again.GetAsync("c", None));
        Assert.Equal(2, File.ReadAllLines(CachePath).Length);
    }

    [Fact]
    public async Task AMissingFileIsAnEmptyCache()
    {
        var cache = new FileContentCache(CachePath);
        Assert.Equal(0, cache.Count);
        Assert.False(File.Exists(CachePath));
        Assert.Null(await cache.GetAsync("a", None));
    }

    [Fact]
    public async Task FirstWriteWinsAlsoAfterARestart()
    {
        var cache = new FileContentCache(CachePath);
        await cache.SetAsync("k", Entry("{\"first\":1}"), None);
        await cache.SetAsync("k", Entry("{\"second\":2}"), None);
        Assert.Equal("{\"first\":1}", (await cache.GetAsync("k", None))!.PayloadJson);
        Assert.Single(File.ReadAllLines(CachePath));

        // A duplicate key in the file (two bakes appending at once) keeps the first line.
        File.AppendAllText(CachePath, File.ReadAllText(CachePath).Replace("first", "other"));
        var again = new FileContentCache(CachePath);
        Assert.Equal("{\"first\":1}", (await again.GetAsync("k", None))!.PayloadJson);
    }

    [Fact]
    public async Task RemoveThenSetWorksAndRewritesSorted()
    {
        var cache = new FileContentCache(CachePath);
        await cache.SetAsync("c", Entry("{\"n\":3}"), None);
        await cache.SetAsync("a", Entry("{\"n\":1}"), None);
        await cache.SetAsync("b", Entry("{\"n\":2}"), None);

        Assert.True(cache.Remove("b"));
        Assert.False(cache.Remove("b"));
        Assert.Null(await cache.GetAsync("b", None));
        var lines = File.ReadAllLines(CachePath);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("{\"key\":\"a\"", lines[0]);
        Assert.StartsWith("{\"key\":\"c\"", lines[1]);
        Assert.False(File.Exists(CachePath + ".tmp"));

        await cache.SetAsync("b", Entry("{\"n\":22}"), None);
        Assert.Equal("{\"n\":22}", (await cache.GetAsync("b", None))!.PayloadJson);
        var again = new FileContentCache(CachePath);
        Assert.Equal("{\"n\":22}", (await again.GetAsync("b", None))!.PayloadJson);
        Assert.Equal(3, again.Count);
    }

    [Fact]
    public async Task ACrashMidRewriteKeepsTheOldFileAndTheEntry()
    {
        var files = new FakeFiles();
        var cache = new FileContentCache("cache.jsonl", files);
        await cache.SetAsync("a", Entry("{\"n\":1}"), None);
        await cache.SetAsync("b", Entry("{\"n\":2}"), None);
        var before = files.Files["cache.jsonl"];

        files.FailReplace = true;
        Assert.Throws<IOException>(() => cache.Remove("a"));
        Assert.Equal(before, files.Files["cache.jsonl"]);
        Assert.NotNull(await cache.GetAsync("a", None));

        files.FailReplace = false;
        files.FailWrite = true;
        Assert.Throws<IOException>(() => cache.Remove("a"));
        Assert.Equal(before, files.Files["cache.jsonl"]);
        Assert.NotNull(await cache.GetAsync("a", None));

        var reloaded = new FileContentCache("cache.jsonl", files);
        Assert.Equal(2, reloaded.Count);
    }

    [Fact]
    public async Task AFailedAppendAddsNothing()
    {
        var files = new FakeFiles { FailAppend = true };
        var cache = new FileContentCache("cache.jsonl", files);
        await Assert.ThrowsAsync<IOException>(() => cache.SetAsync("a", Entry("{}"), None));
        Assert.Null(await cache.GetAsync("a", None));
    }

    [Fact]
    public async Task ATornLastLineIsIgnoredAndTheNextWriteRewritesTheFile()
    {
        var cache = new FileContentCache(CachePath);
        await cache.SetAsync("a", Entry("{\"n\":1}"), None);
        File.AppendAllText(CachePath, "{\"key\":\"b\",\"contentT"); // a crash mid-append

        var again = new FileContentCache(CachePath);
        Assert.Equal(1, again.Count);
        await again.SetAsync("c", Entry("{\"n\":3}"), None);
        var lines = File.ReadAllLines(CachePath);
        Assert.Equal(2, lines.Length);
        Assert.Equal(2, new FileContentCache(CachePath).Count);
    }

    [Fact]
    public void ABadLineInTheMiddleStopsTheLoad()
    {
        File.WriteAllText(CachePath, "not json\n{}\n");
        Assert.Throws<InvalidDataException>(() => new FileContentCache(CachePath));
    }

    private sealed class FakeFiles : FileContentCache.IFileOps
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);
        public bool FailWrite { get; set; }
        public bool FailReplace { get; set; }
        public bool FailAppend { get; set; }

        public bool Exists(string path) => Files.ContainsKey(path);

        public string Read(string path) => Files[path];

        public void Append(string path, string text)
        {
            if (FailAppend) throw new IOException("disk full");
            Files[path] = (Files.TryGetValue(path, out var old) ? old : "") + text;
        }

        public void Write(string path, string text)
        {
            if (FailWrite) throw new IOException("crash while writing");
            Files[path] = text;
        }

        public void Replace(string source, string destination)
        {
            if (FailReplace) throw new IOException("crash before the replace");
            Files[destination] = Files[source];
            Files.Remove(source);
        }
    }
}
