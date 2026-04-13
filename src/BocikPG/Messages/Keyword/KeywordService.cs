using System.Text.Json;
using BocikPG;
using BocikPG.Sync;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class KeywordService : IReloadable
{
    private readonly string _filePath;
    private readonly ILogger<KeywordService> _logger;
    private readonly GitSyncService _syncService;
    private Dictionary<string, List<ResponseEntry>> _keywords = new();
    private static readonly Random _random = new();

    public KeywordService(IOptions<ChatOptions> options, ILogger<KeywordService> logger, GitSyncService syncService)
    {
        _filePath = options.Value.KeywordsFilePath;
        _logger = logger;
        _syncService = syncService;
        Load();
    }

    // ── IReloadable ───────────────────────────────────────────────────────────

    public Task ReloadAsync()
    {
        Load();
        return Task.CompletedTask;
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            _keywords = new Dictionary<string, List<ResponseEntry>>();
            _ = SaveAsync();
            return;
        }

        var json = File.ReadAllText(_filePath);
        var data = JsonSerializer.Deserialize<KeywordResponse>(json);
        // Swap atomically so concurrent readers always see a complete dictionary
        Interlocked.Exchange(ref _keywords, data?.Keywords ?? new Dictionary<string, List<ResponseEntry>>());
    }

    private async Task SaveAsync()
    {
        var data = new KeywordResponse { Keywords = _keywords };
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        _ = Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

        await File.WriteAllTextAsync(_filePath, json);
        await _syncService.SyncFileAsync(Path.GetFullPath(_filePath), "Update keywords");
    }

    // ── Public API ────────────────────────────────────────────────────────────

    private string? PickRandomResponse(List<ResponseEntry> responses)
    {
        if (responses == null || responses.Count == 0)
            return null;

        int totalWeight = responses.Sum(r => r.Weight);
        if (totalWeight <= 0) return null;

        int roll = _random.Next(totalWeight);
        int cumulative = 0;
        foreach (var entry in responses)
        {
            cumulative += entry.Weight;
            if (roll < cumulative)
                return string.IsNullOrWhiteSpace(entry.Text) ? null : entry.Text;
        }
        return null;
    }

    public string? GetResponse(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var lower = message.ToLowerInvariant();
        foreach (var kv in _keywords)
        {
            if (lower.Contains(kv.Key))
                return PickRandomResponse(kv.Value);
        }
        return null;
    }

    public void AddResponse(string keyword, string text, int weight = 1)
    {
        var key = keyword.ToLowerInvariant();

        if (!_keywords.ContainsKey(key))
            _keywords[key] = new List<ResponseEntry>();

        var existing = _keywords[key].FirstOrDefault(e => e.Text == text);
        if (existing != null)
            existing.Weight = weight;
        else
            _keywords[key].Add(new ResponseEntry { Text = text, Weight = weight });

        _ = SaveAsync();
    }

    public bool RemoveResponse(string keyword, string text)
    {
        var key = keyword.ToLowerInvariant();
        if (!_keywords.TryGetValue(key, out var entries))
            return false;

        var removed = entries.RemoveAll(e => e.Text == text) > 0;
        if (removed && entries.Count == 0)
            _ = _keywords.Remove(key);

        _ = SaveAsync();
        return removed;
    }

    public List<ResponseEntry> GetResponses(string keyword)
    {
        var key = keyword.ToLowerInvariant();
        return _keywords.TryGetValue(key, out var entries)
            ? new List<ResponseEntry>(entries)
            : new List<ResponseEntry>();
    }

    public Dictionary<string, List<ResponseEntry>> GetAllKeywords() => new(_keywords);
}