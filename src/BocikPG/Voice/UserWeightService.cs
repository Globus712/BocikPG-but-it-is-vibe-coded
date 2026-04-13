using System.Text.Json;
using BocikPG;
using BocikPG.Sync;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class UserWeightService : IReloadable
{
    private readonly string _filePath;
    private readonly ILogger<UserWeightService> _logger;
    private readonly GitSyncService _syncService;
    private Dictionary<ulong, double> _weights = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public UserWeightService(IOptions<VoiceOptions> options, ILogger<UserWeightService> logger, GitSyncService syncService)
    {
        _filePath = options.Value.WeightsFilePath;
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
        try
        {
            if (!File.Exists(_filePath))
            {
                Interlocked.Exchange(ref _weights, new Dictionary<ulong, double>());
                return;
            }

            var json = File.ReadAllText(_filePath);
            var raw = JsonSerializer.Deserialize<Dictionary<string, double>>(json) ?? new();
            var loaded = raw.ToDictionary(kv => ulong.Parse(kv.Key), kv => kv.Value);
            Interlocked.Exchange(ref _weights, loaded);
            _logger.LogInformation("Loaded {Count} user weights from {File}", _weights.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load user weights from {File}", _filePath);
        }
    }

    private async Task SaveAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            var raw = _weights.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
            var json = JsonSerializer.Serialize(raw, new JsonSerializerOptions { WriteIndented = true });
            _ = Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            await File.WriteAllTextAsync(_filePath, json);
            await _syncService.SyncFileAsync(Path.GetFullPath(_filePath), "Update user weights");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save user weights to {File}", _filePath);
        }
        finally
        {
            _ = _saveLock.Release();
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public double GetWeight(ulong userId) => _weights.GetValueOrDefault(userId, 1.0);

    public IReadOnlyDictionary<ulong, double> GetAll() => _weights;

    public async Task SetWeightAsync(ulong userId, double weight)
    {
        _weights[userId] = weight;
        await SaveAsync();
    }

    public async Task RemoveWeightAsync(ulong userId)
    {
        _ = _weights.Remove(userId);
        await SaveAsync();
    }
}