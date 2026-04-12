using System.Text.Json;

public class KeywordService
{
    private readonly string _filePath = "Resources/Chat/Keywords.json";
    private Dictionary<string, List<ResponseEntry>> _keywords;
    private static readonly Random _random = new();

    public KeywordService()
    {
        Load();
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            _keywords = new Dictionary<string, List<ResponseEntry>>();
            Save();
            return;
        }
        var json = File.ReadAllText(_filePath);
        var data = JsonSerializer.Deserialize<KeywordResponse>(json);
        _keywords = data?.Keywords ?? new Dictionary<string, List<ResponseEntry>>();
    }

    private void Save()
    {
        var data = new KeywordResponse { Keywords = _keywords };
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, json);
    }

    // Returns a random response based on weights, or null if none
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
            {
                // If the selected response text is empty or whitespace, return null (send nothing)
                return string.IsNullOrWhiteSpace(entry.Text) ? null : entry.Text;
            }
        }
        return null;
    }

    public string? GetResponse(string message)
    {
        // Ignore empty or whitespace messages
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var lower = message.ToLowerInvariant();

        // Contains match
        foreach (var kv in _keywords)
        {
            if (lower.Contains(kv.Key))
                return PickRandomResponse(kv.Value);
        }
        return null;
    }

    // Add a weighted response to a keyword
    public void AddResponse(string keyword, string text, int weight = 1)
    {
        var key = keyword.ToLowerInvariant();

        // Ensure the keyword entry exists
        if (!_keywords.ContainsKey(key))
            _keywords[key] = new List<ResponseEntry>();

        // Check if the exact same response text already exists
        var existing = _keywords[key].FirstOrDefault(e => e.Text == text);
        if (existing != null)
        {
            // Update weight instead of adding duplicate
            existing.Weight = weight;
        }
        else
        {
            // Add new response
            _keywords[key].Add(new ResponseEntry { Text = text, Weight = weight });
        }

        Save();
    }

    // Remove a specific response (by exact text match)
    public bool RemoveResponse(string keyword, string text)
    {
        var key = keyword.ToLowerInvariant();
        if (!_keywords.TryGetValue(key, out var entries))
            return false;

        var removed = entries.RemoveAll(e => e.Text == text) > 0;
        if (removed && entries.Count == 0)
            _keywords.Remove(key);
        Save();
        return removed;
    }
    

    // Get all responses (with weights) for a keyword
    public List<ResponseEntry> GetResponses(string keyword)
    {
        var key = keyword.ToLowerInvariant();
        return _keywords.TryGetValue(key, out var entries)
            ? new List<ResponseEntry>(entries)
            : new List<ResponseEntry>();
    }

    // Get all keywords for listing
    public Dictionary<string, List<ResponseEntry>> GetAllKeywords() => new(_keywords);
}