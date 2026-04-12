using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace BocikPG;

public sealed class RandomResponseService
{
    private readonly Random _random = new();
    private readonly RandomResponseOptions _options;
    private readonly string _filePath;
    private ConcurrentDictionary<ulong, UserRandomConfig> _userConfigs = new();

    public RandomResponseService(IOptions<RandomResponseOptions> options)
    {
        _options = options.Value;
        _filePath = _options.StorageFile;
        Load();
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            _userConfigs = new ConcurrentDictionary<ulong, UserRandomConfig>();
            Save();
            return;
        }

        var json = File.ReadAllText(_filePath);
        var dict = JsonSerializer.Deserialize<Dictionary<string, UserRandomConfig>>(json);
        if (dict != null)
        {
            _userConfigs = new ConcurrentDictionary<ulong, UserRandomConfig>(
                dict.ToDictionary(kv => ulong.Parse(kv.Key), kv => kv.Value));
        }
        else
        {
            _userConfigs = new ConcurrentDictionary<ulong, UserRandomConfig>();
        }
    }

    private void Save()
    {
        var dict = _userConfigs.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
        var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, json);
    }

    /// <summary>
    /// Returns a random response if the user triggers the chance, otherwise null.
    /// </summary>
    public string? GetRandomResponse(ulong userId)
    {
        if (!_options.GlobalEnabled) return null;

        // Get user config or create default
        var userConfig = _userConfigs.GetOrAdd(userId, _ => new UserRandomConfig
        {
            Chance = _options.DefaultChance,
            Responses = _options.DefaultResponses.ToList(),
            Enabled = true
        });

        if (!userConfig.Enabled) return null;

        // Roll the dice
        var roll = _random.NextDouble();
        if (roll >= userConfig.Chance) return null;

        // Choose weighted response
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

        return null; // fallback
    }

    // Admin commands
    public void SetUserChance(ulong userId, double chance)
    {
        var config = _userConfigs.GetOrAdd(userId, _ => new UserRandomConfig
        {
            Chance = _options.DefaultChance,
            Responses = _options.DefaultResponses.ToList(),
            Enabled = true
        });
        config.Chance = Math.Clamp(chance, 0, 1);
        Save();
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
        Save();
    }

    public bool RemoveUserResponse(ulong userId, int index)
    {
        if (_userConfigs.TryGetValue(userId, out var config) && index >= 0 && index < config.Responses.Count)
        {
            config.Responses.RemoveAt(index);
            Save();
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
        Save();
    }
}