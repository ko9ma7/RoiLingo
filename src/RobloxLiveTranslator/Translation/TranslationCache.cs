using System.Collections.Concurrent;

namespace RobloxLiveTranslator.Translation;

public sealed class TranslationCache
{
    private readonly string _path;
    private readonly ConcurrentDictionary<string, CacheEntry> _entries;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private sealed record CacheEntry(string Provider, string Text, DateTimeOffset UpdatedAt);

    public TranslationCache(string directory, string fileName = "translation-cache.json")
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, fileName);
        Dictionary<string, CacheEntry> loaded = new();
        if (File.Exists(_path))
        {
            try
            {
                loaded = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(File.ReadAllText(_path)) ?? new();
            }
            catch (JsonException)
            {
                BackupCorruptCache();
            }
            catch (IOException)
            {
                // Cache is optional. A locked/unreadable cache only causes a cold start.
            }
        }
        _entries = new ConcurrentDictionary<string, CacheEntry>(loaded);
    }

    public bool TryGet(string source, string target, out string provider, out string text)
    {
        var key = Key(source, target);
        if (_entries.TryGetValue(key, out var entry))
        {
            if (TranslationTextValidator.IsUsable(source, entry.Text))
            {
                provider = entry.Provider;
                text = entry.Text;
                return true;
            }

            // Bad web-page chrome/failure text must never become sticky across sessions.
            _entries.TryRemove(key, out _);
        }
        provider = "";
        text = "";
        return false;
    }

    public async Task PutAsync(string source, string target, string provider, string text)
    {
        await _gate.WaitAsync();
        try
        {
            _entries[Key(source, target)] = new CacheEntry(provider, text, DateTimeOffset.Now);
            if (_entries.Count > 5000)
            {
                foreach (var key in _entries.OrderBy(kv => kv.Value.UpdatedAt)
                             .Take(_entries.Count - 4500).Select(kv => kv.Key).ToArray())
                    _entries.TryRemove(key, out _);
            }

            var temp = _path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(_entries));
            File.Move(temp, _path, true);
        }
        finally
        {
            _gate.Release();
        }
    }


    public async Task ClearAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _entries.Clear();
            try
            {
                if (File.Exists(_path)) File.Delete(_path);
            }
            catch (IOException)
            {
                // In-memory cache is already cleared. A locked cache file can be
                // replaced naturally on the next successful PutAsync call.
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void BackupCorruptCache()
    {
        try
        {
            var backup = _path + $".broken-{DateTimeOffset.Now:yyyyMMddHHmmss}";
            File.Copy(_path, backup, true);
        }
        catch (IOException)
        {
            // Optional backup only; translation can continue with an empty cache.
        }
    }

    private static string Key(string source, string target) => $"{target}\u001f{source.Trim().ToLowerInvariant()}";
}
