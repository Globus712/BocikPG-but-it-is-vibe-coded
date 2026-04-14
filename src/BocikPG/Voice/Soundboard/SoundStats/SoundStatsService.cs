using System.Text.Json;
using BocikPG.Sync;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BocikPG.Soundboard;

/// <summary>
/// Tracks per-guild, per-user, per-sound play counts in memory and periodically
/// flushes them to disk + Git. Also implements <see cref="IReloadable"/> so the
/// sync coordinator can reload the file after a pull.
/// </summary>
public class SoundStatsService : IHostedService, IReloadable
{
    private readonly SoundStatsOptions _options;
    private readonly GitSyncService _syncService;
    private readonly ILogger<SoundStatsService> _logger;

    private readonly string _dataFilePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private SoundStatsData _data = new();
    private Timer? _flushTimer;

    public SoundStatsService(
        IOptions<SoundStatsOptions> options,
        GitSyncService syncService,
        ILogger<SoundStatsService> logger)
    {
        _options = options.Value;
        _syncService = syncService;
        _logger = logger;
        _dataFilePath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, _options.StatsFile));

        LoadData();
    }

    // ── IHostedService ────────────────────────────────────────────────────────

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromMinutes(_options.FlushIntervalMinutes);
        _flushTimer = new Timer(
            _ => _ = FlushAsync("Periodic stats flush"),
            null,
            interval,
            interval);

        _logger.LogInformation(
            "SoundStatsService started — flushing every {Minutes} minutes.",
            _options.FlushIntervalMinutes);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_flushTimer is not null)
            await _flushTimer.DisposeAsync();

        _logger.LogInformation("SoundStatsService stopping — flushing stats on shutdown.");
        await FlushAsync("Shutdown stats flush");
    }

    // ── IReloadable ───────────────────────────────────────────────────────────

    public Task ReloadAsync()
    {
        LoadData();
        return Task.CompletedTask;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Increments the play count for the given sound and context.
    /// Thread-safe; non-blocking (uses lock-free interlocked on inner dict).
    /// </summary>
    public void Record(ulong guildId, SoundDefinition sound, PlayContext? context)
    {
        var guild = guildId.ToString();
        var user = context?.UserId.ToString() ?? "0";
        var source = context?.Source.ToString() ?? "Unknown";
        var key = $"{source}:{sound.Name}";

        lock (_data.Counts)
        {
            if (!_data.Counts.TryGetValue(guild, out var byUser))
                _data.Counts[guild] = byUser = new Dictionary<string, Dictionary<string, long>>();

            if (!byUser.TryGetValue(user, out var byKey))
                byUser[user] = byKey = new Dictionary<string, long>();

            byKey[key] = byKey.TryGetValue(key, out var current) ? current + 1 : 1;
        }
    }

    // Add this method to SoundStatsService class

    /// <summary>
    /// Forces an immediate flush of in-memory stats to disk and Git sync.
    /// </summary>
    /// <param name="reason">Reason for the flush (used in commit message).</param>
    public async Task ForceFlushAsync(string reason = "Manual flush")
    {
        await FlushAsync(reason);
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private void LoadData()
    {
        if (!File.Exists(_dataFilePath))
        {
            _logger.LogInformation(
                "Stats file not found at {Path}, starting with empty data.", _dataFilePath);
            _data = new SoundStatsData();
            return;
        }

        try
        {
            var json = File.ReadAllText(_dataFilePath);
            _data = JsonSerializer.Deserialize<SoundStatsData>(json) ?? new SoundStatsData();
            _logger.LogInformation("Loaded sound stats from {Path}.", _dataFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load sound stats from {Path}", _dataFilePath);
            _data = new SoundStatsData();
        }
    }

    private async Task FlushAsync(string commitMessage)
    {
        await _lock.WaitAsync();
        try
        {
            SoundStatsData snapshot;
            lock (_data.Counts)
            {
                // Deep-copy so we don't hold the inner lock while doing I/O.
                snapshot = DeepCopy(_data);
            }

            _ = Directory.CreateDirectory(Path.GetDirectoryName(_dataFilePath)!);
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_dataFilePath, json);

            await _syncService.SyncFileAsync(_dataFilePath, commitMessage);
            _logger.LogDebug("Sound stats flushed to {Path}.", _dataFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush sound stats.");
        }
        finally
        {
            _lock.Release();
        }
    }


    private static SoundStatsData DeepCopy(SoundStatsData source)
    {
        var copy = new SoundStatsData();
        foreach (var (guild, byUser) in source.Counts)
        {
            var userCopy = new Dictionary<string, Dictionary<string, long>>();
            foreach (var (user, byKey) in byUser)
                userCopy[user] = new Dictionary<string, long>(byKey);
            copy.Counts[guild] = userCopy;
        }
        return copy;
    }

    /// <summary>
    /// Returns all unique source names found in the stats for a guild.
    /// </summary>
    public List<string> GetAvailableSources(ulong guildId)
    {
        var guildKey = guildId.ToString();
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        lock (_data.Counts)
        {
            if (!_data.Counts.TryGetValue(guildKey, out var byUser))
                return new List<string>();

            foreach (var byKey in byUser.Values)
            {
                foreach (var key in byKey.Keys)
                {
                    var colonIndex = key.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        var source = key[..colonIndex];
                        sources.Add(source);
                    }
                }
            }
        }

        return sources.OrderBy(s => s).ToList();
    }

    // Add to SoundStatsService class

    /// <summary>
    /// Gets play count for a specific combination of guild, user, sound, and source.
    /// All parameters are optional – if null, they act as wildcards.
    /// </summary>
    public long GetPlayCount(ulong guildId, ulong? userId = null, string? soundName = null, string? source = null)
    {
        var guildKey = guildId.ToString();
        var userKey = userId?.ToString() ?? "0";
        long total = 0;

        lock (_data.Counts)
        {
            if (!_data.Counts.TryGetValue(guildKey, out var byUser))
                return 0;

            IEnumerable<Dictionary<string, long>> usersToCheck;
            if (userId.HasValue)
            {
                if (byUser.TryGetValue(userKey, out var userData))
                    usersToCheck = new[] { userData };
                else
                    usersToCheck = Enumerable.Empty<Dictionary<string, long>>();
            }
            else
            {
                usersToCheck = byUser.Values;
            }

            foreach (var byKey in usersToCheck)
            {
                foreach (var (key, count) in byKey)
                {
                    var colonIndex = key.IndexOf(':');
                    var keySource = colonIndex > 0 ? key[..colonIndex] : null;
                    var keySound = colonIndex > 0 ? key[(colonIndex + 1)..] : key;

                    bool sourceMatches = source == null || string.Equals(keySource, source, StringComparison.OrdinalIgnoreCase);
                    bool soundMatches = soundName == null || string.Equals(keySound, soundName, StringComparison.OrdinalIgnoreCase);

                    if (sourceMatches && soundMatches)
                        total += count;
                }
            }
        }
        return total;
    }
    /// <summary>
    /// Gets top sounds based on filters. Returns list of (soundName, playCount).
    /// </summary>
    public List<KeyValuePair<string, long>> GetTopSounds(
    ulong guildId,
    ulong? userId = null,
    string? source = null,
    int limit = 10)
    {
        var guildKey = guildId.ToString();
        var userKey = userId?.ToString() ?? "0";
        var soundTotals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        lock (_data.Counts)
        {
            if (!_data.Counts.TryGetValue(guildKey, out var byUser))
                return new List<KeyValuePair<string, long>>();

            IEnumerable<Dictionary<string, long>> usersToCheck;
            if (userId.HasValue)
            {
                if (byUser.TryGetValue(userKey, out var userData))
                    usersToCheck = new[] { userData };
                else
                    usersToCheck = Enumerable.Empty<Dictionary<string, long>>();
            }
            else
            {
                usersToCheck = byUser.Values;
            }

            foreach (var byKey in usersToCheck)
            {
                foreach (var (key, count) in byKey)
                {
                    var colonIndex = key.IndexOf(':');
                    var keySource = colonIndex > 0 ? key[..colonIndex] : null;
                    var keySound = colonIndex > 0 ? key[(colonIndex + 1)..] : key;

                    if (source == null || string.Equals(keySource, source, StringComparison.OrdinalIgnoreCase))
                    {
                        soundTotals.TryGetValue(keySound, out var current);
                        soundTotals[keySound] = current + count;
                    }
                }
            }
        }

        return soundTotals
            .OrderByDescending(kvp => kvp.Value)
            .Take(limit)
            .ToList();
    }
}
