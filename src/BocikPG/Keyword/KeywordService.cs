using System.Text.Json;

public class KeywordService
{
    private readonly string _filePath = "Keywords.json";
    private Dictionary<string, string> _keywords;

    public KeywordService()
    {
        Load();
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            _keywords = new Dictionary<string, string>();
            Save();
            return;
        }
        var json = File.ReadAllText(_filePath);
        var data = JsonSerializer.Deserialize<KeywordResponse>(json);
        _keywords = data?.Keywords ?? new Dictionary<string, string>();
    }

    private void Save()
    {
        var data = new KeywordResponse { Keywords = _keywords };
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }

    public string? GetResponse(string message)
    {
        var lower = message.ToLowerInvariant();
        // Check exact match
        if (_keywords.TryGetValue(lower, out var response))
            return response;
        // Optional: check if message contains any keyword
        foreach (var kv in _keywords)
        {
            if (lower.Contains(kv.Key))
                return kv.Value;
        }
        return null;
    }

    public void AddKeyword(string keyword, string response)
    {
        _keywords[keyword.ToLowerInvariant()] = response;
        Save();
    }

    public bool RemoveKeyword(string keyword)
    {
        var removed = _keywords.Remove(keyword.ToLowerInvariant());
        if (removed) Save();
        return removed;
    }

    public Dictionary<string, string> GetAllKeywords() => new(_keywords);
}