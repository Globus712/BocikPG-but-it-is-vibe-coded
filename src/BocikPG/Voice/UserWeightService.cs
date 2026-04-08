using System.Text.Json;
using BocikPG;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class UserWeightService
{
    private readonly string _filePath;
    private readonly ILogger<UserWeightService> _logger;
    private Dictionary<ulong, double> _weights = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public UserWeightService(IOptions<VoiceOptions> options, ILogger<UserWeightService> logger)
    {
        _filePath = options.Value.WeightsFilePath;
        _logger = logger;
        Load();
    }

    public double GetWeight(ulong userId) 
        => _weights.GetValueOrDefault(userId, 1.0); // default weight = 1

    public IReadOnlyDictionary<ulong, double> GetAll() => _weights;

    public async Task SetWeightAsync(ulong userId, double weight)
    {
        _weights[userId] = weight;
        await SaveAsync();
    }

    public async Task RemoveWeightAsync(ulong userId)
    {
        _weights.Remove(userId);
        await SaveAsync();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var json = File.ReadAllText(_filePath);
            // JSON keys are strings, so we deserialize as string→double then convert
            var raw = JsonSerializer.Deserialize<Dictionary<string, double>>(json) ?? new();
            _weights = raw.ToDictionary(kv => ulong.Parse(kv.Key), kv => kv.Value);
            _logger.LogInformation("Loaded {Count} user weights from {File}", _weights.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load user weights from {File}", _filePath);
        }
    }

    private async Task SaveAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var raw = _weights.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
            var json = JsonSerializer.Serialize(raw, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save user weights to {File}", _filePath);
        }
        finally
        {
            _lock.Release();
        }
    }
}