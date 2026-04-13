using System.Collections.Concurrent;
using System.Text.Json;
using BocikPG.Sync;
using Microsoft.Extensions.Options;

namespace BocikPG;

public sealed class RandomResponseService : IReloadable
{
    private readonly Random _random = new();
    private readonly RandomResponseOptions _options;
    private readonly string _filePath;
    private readonly GitSyncService _syncService;
    private ConcurrentDictionary<ulong, UserRandomConfig> _userConfigs = new();

    public RandomResponseService(IOptions<RandomResponseOptions> options, GitSyncService syncService)
    {
        _options = options.Value;
        _filePath = _options.StorageFile;
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
            Interlocked.Exchange(ref _userConfigs, new ConcurrentDictionary<ulong, UserRandomConfig>());
            _ = SaveAsync();
            return;
        }

        var json = File.ReadAllText(_filePath);
        var dict = JsonSerializer.Deserialize<Dictionary<string, UserRandomConfig>>(json);
        var loaded = dict != null
            ? new ConcurrentDictionary<ulong, UserRandomConfig>(
                dict.ToDictionary(kv => ulong.Parse(kv.Key), kv => kv.Value))
            : new ConcurrentDictionary<ulong, UserRandomConfig>();

        Interlocked.Exchange(ref _userConfigs, loaded);
    }

    private async Task SaveAsync()
    {
        var dict = _userConfigs.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
        var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
        _ = Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await File.WriteAllTextAsync(_filePath, json); // was File.WriteAllText — fixed

        await _syncService.SyncFileAsync(Path.GetFullPath(_filePath), "Update random response");
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public string? GetRandomResponse(ulong userId)
    {
        if (!_options.GlobalEnabled) return null;

        var userConfig = _userConfigs.GetOrAdd(userId, _ => new UserRandomConfig
        {
            Chance = _options.DefaultChance,
            Responses = _options.DefaultResponses.ToList(),
            Enabled = true
        });

        if (!userConfig.Enabled) return null;

        var roll = _random.NextDouble();
        if (roll >= userConfig.Chance) return null;

        var totalWeight = userConfig.Responses.Sum(r => r.Weight);
        if (totalWeight == 0) return null;

        var target = _random.Next(totalWeight);
        var cumulative = 0;
        foreach (var resp in userConfig.Responses)
        {
            cumulative += resp.Weight;
            if (target < cumulative)
                return resp.Text;
        }

        return null;
    }

    public void SetUserChance(ulong userId, double chance)
    {
        var config = _userConfigs.GetOrAdd(userId, _ => new UserRandomConfig
        {
            Chance = _options.DefaultChance,
            Responses = _options.DefaultResponses.ToList(),
            Enabled = true
        });
        config.Chance = Math.Clamp(chance, 0, 1);
        _ = SaveAsync();
    }

    public void AddUserResponse(ulong userId, string text, int weight)
    {
        var config = _userConfigs.GetOrAdd(userId, _ => new UserRandomConfig
        {
            Chance = _options.DefaultChance,
            Responses = _options.DefaultResponses.ToList(),
            Enabled = true
        });
        config.Responses.Add(new WeightedResponse { Text = text, Weight = weight });
        _ = SaveAsync();
    }

    public bool RemoveUserResponse(ulong userId, int index)
    {
        if (_userConfigs.TryGetValue(userId, out var config) && index >= 0 && index < config.Responses.Count)
        {
            config.Responses.RemoveAt(index);
            _ = SaveAsync();
            return true;
        }
        return false;
    }

    public List<WeightedResponse> GetUserResponses(ulong userId)
    {
        if (_userConfigs.TryGetValue(userId, out var config))
            return config.Responses.ToList();
        return _options.DefaultResponses.ToList();
    }

    public double GetUserChance(ulong userId)
    {
        if (_userConfigs.TryGetValue(userId, out var config))
            return config.Chance;
        return _options.DefaultChance;
    }

    public void EnableUser(ulong userId, bool enabled)
    {
        var config = _userConfigs.GetOrAdd(userId, _ => new UserRandomConfig
        {
            Chance = _options.DefaultChance,
            Responses = _options.DefaultResponses.ToList(),
            Enabled = true
        });
        config.Enabled = enabled;
        _ = SaveAsync();
    }
}