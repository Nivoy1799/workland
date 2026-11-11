using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Worktown.Gateway.Cache;

/// <summary>
/// Spec 056b: a content cache in one JSON-lines file (the offline name bake's <c>ai-gateway/offline/name-cache.jsonl</c>):
/// one line per <see cref="CachedContent"/> plus its key. The file is loaded once at start, a new entry is appended,
/// first write wins (as <see cref="IContentCache"/>), and <see cref="Remove"/> rewrites the file sorted by key, atomically
/// (a <c>.tmp</c> file, then a replace): a failed rewrite keeps the old file and the old entries. A torn last line (no
/// line end, from a crash mid-append) is ignored; any other bad line stops the load.
/// </summary>
public sealed class FileContentCache : IContentCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly string _path;
    private readonly IFileOps _files;
    private readonly object _gate = new();
    private Dictionary<string, CachedContent> _entries;
    private bool _tornTail; // the file ends in a torn line: the next write rewrites it whole

    public FileContentCache(string path)
        : this(path, new SystemFileOps())
    {
    }

    internal FileContentCache(string path, IFileOps files)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _entries = Load(path, files, out _tornTail);
    }

    /// <summary>Entries in the cache.</summary>
    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    public Task<CachedContent?> GetAsync(string key, CancellationToken ct)
    {
        lock (_gate) return Task.FromResult(_entries.TryGetValue(key, out var value) ? value : null);
    }

    public Task SetAsync(string key, CachedContent value, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            if (_entries.ContainsKey(key)) return Task.CompletedTask;
            if (_tornTail)
            {
                Rewrite(new Dictionary<string, CachedContent>(_entries, StringComparer.Ordinal) { [key] = value });
                _tornTail = false;
            }
            else
            {
                _files.Append(_path, Line(key, value) + "\n");
            }
            _entries.Add(key, value);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes the entry and rewrites the file (sorted by key, <c>.tmp</c> then replace). False when there is no entry.
    /// A failed write throws and keeps the file and the entry.
    /// </summary>
    public bool Remove(string key)
    {
        lock (_gate)
        {
            if (!_entries.ContainsKey(key)) return false;
            var remaining = new Dictionary<string, CachedContent>(_entries, StringComparer.Ordinal);
            remaining.Remove(key);
            Rewrite(remaining);
            _entries = remaining;
            _tornTail = false;
            return true;
        }
    }

    /// <summary>Writes every entry, sorted by key, to a <c>.tmp</c> file, then moves it over the cache file.</summary>
    private void Rewrite(Dictionary<string, CachedContent> entries)
    {
        var keys = new List<string>(entries.Keys);
        keys.Sort(StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var k in keys) sb.Append(Line(k, entries[k])).Append('\n');
        var tmp = _path + ".tmp";
        _files.Write(tmp, sb.ToString());
        _files.Replace(tmp, _path);
    }

    private static Dictionary<string, CachedContent> Load(string path, IFileOps files, out bool tornTail)
    {
        var entries = new Dictionary<string, CachedContent>(StringComparer.Ordinal);
        tornTail = false;
        if (!files.Exists(path)) return entries;
        var text = files.Read(path);
        tornTail = text.Length > 0 && !text.EndsWith('\n');
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0) continue;
            var last = i == lines.Length - 1; // no line end after it: a torn append
            FileLine? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<FileLine>(line, JsonOptions);
            }
            catch (JsonException)
            {
                if (last) continue;
                throw new InvalidDataException($"Bad cache line {i + 1} in {Path.GetFileName(path)}.");
            }
            if (parsed is null || !parsed.IsComplete)
            {
                if (last) continue;
                throw new InvalidDataException($"Bad cache line {i + 1} in {Path.GetFileName(path)}.");
            }
            entries.TryAdd(parsed.Key!, parsed.ToContent()); // first write wins
        }
        return entries;
    }

    private static string Line(string key, CachedContent c) => JsonSerializer.Serialize(new FileLine
    {
        Key = key,
        ContentType = c.ContentType,
        ItemHash = c.ItemHash,
        CanonicalTree = c.CanonicalTree,
        InputHash = c.InputHash,
        PayloadJson = c.PayloadJson,
        ContentVersion = c.ContentVersion,
        Model = c.Model,
        CreatedAt = c.CreatedAt,
    }, JsonOptions);

    private sealed class FileLine
    {
        [JsonPropertyName("key")] public string? Key { get; set; }
        [JsonPropertyName("contentType")] public string? ContentType { get; set; }
        [JsonPropertyName("itemHash")] public string? ItemHash { get; set; }
        [JsonPropertyName("canonicalTree")] public string? CanonicalTree { get; set; }
        [JsonPropertyName("inputHash")] public string? InputHash { get; set; }
        [JsonPropertyName("payloadJson")] public string? PayloadJson { get; set; }
        [JsonPropertyName("contentVersion")] public string? ContentVersion { get; set; }
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; }

        [JsonIgnore]
        public bool IsComplete => !string.IsNullOrEmpty(Key) && ContentType is not null && ItemHash is not null && CanonicalTree is not null
            && InputHash is not null && PayloadJson is not null && ContentVersion is not null && Model is not null;

        public CachedContent ToContent() =>
            new(ContentType!, ItemHash!, CanonicalTree!, InputHash!, PayloadJson!, ContentVersion!, Model!, CreatedAt);
    }

    /// <summary>The file operations the cache uses (a seam for tests that fail a write).</summary>
    internal interface IFileOps
    {
        bool Exists(string path);
        string Read(string path);
        void Append(string path, string text);
        void Write(string path, string text);
        /// <summary>Moves <paramref name="source"/> over <paramref name="destination"/>.</summary>
        void Replace(string source, string destination);
    }

    private sealed class SystemFileOps : IFileOps
    {
        private static readonly UTF8Encoding Utf8 = new(false);

        public bool Exists(string path) => File.Exists(path);

        public string Read(string path) => File.ReadAllText(path, Utf8);

        public void Append(string path, string text) => File.AppendAllText(path, text, Utf8);

        public void Write(string path, string text) => File.WriteAllText(path, text, Utf8);

        public void Replace(string source, string destination) => File.Move(source, destination, overwrite: true);
    }
}
