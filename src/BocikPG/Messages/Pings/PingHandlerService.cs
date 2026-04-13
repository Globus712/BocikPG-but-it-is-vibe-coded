using System.Collections.Concurrent;
using System.Text.Json;
using BocikPG;
using BocikPG.Sync;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class PingHandlerService : IReloadable
{
    private readonly string _filePath;
    private readonly PingOptions _options;
    private readonly ILogger<PingHandlerService>? _logger;
    private readonly GitSyncService _syncService;
    private Dictionary<ulong, string> _personalizedResponses = new();
    private ConcurrentDictionary<ulong, (int count, DateTime timeoutUntil)> _userPingState = new();
    public bool DecayEnabled { get; set; } = true;
    public int DecayIntervalMinutes { get; set; } = 60;

    public PingHandlerService(IOptions<PingOptions> options, GitSyncService syncService, ILogger<PingHandlerService>? logger = null)
    {
        _options = options.Value;
        _logger = logger;
        _syncService = syncService;
        _filePath = _options.PersonalizedResponsesFilePath;
        LoadPersonalizedResponses();
    }

    // ── IReloadable ───────────────────────────────────────────────────────────

    public Task ReloadAsync()
    {
        LoadPersonalizedResponses();
        // Runtime ping state (_userPingState) is intentionally not reset on reload —
        // it tracks live cooldowns unrelated to the persisted file.
        return Task.CompletedTask;
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private void LoadPersonalizedResponses()
    {
        if (!File.Exists(_filePath))
        {
            _personalizedResponses = new();
            _ = SaveAsync();
            return;
        }

        var json = File.ReadAllText(_filePath);
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        var loaded = dict?.ToDictionary(kv => ulong.Parse(kv.Key), kv => kv.Value) ?? new();
        Interlocked.Exchange(ref _personalizedResponses, loaded);
    }

    private async Task SaveAsync()
    {
        try
        {
            var dict = _personalizedResponses.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
            var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });

            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            await File.WriteAllTextAsync(_filePath, json);
            await _syncService.SyncFileAsync(Path.GetFullPath(_filePath), "Update personalized ping responses");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save personalized responses");
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public string GetResponseForUser(ulong userId)
    {
        if (_personalizedResponses.TryGetValue(userId, out var custom))
            return custom;
        return _options.DefaultResponse;
    }

    public async Task<string?> CanPingAsync(ulong userId, DiscordMember? member)
    {
        if (_userPingState.TryGetValue(userId, out var state) && DateTime.UtcNow < state.timeoutUntil)
            return null;

        var newState = _userPingState.AddOrUpdate(userId,
            (1, DateTime.MinValue),
            (_, old) => (old.count + 1, old.timeoutUntil));

        if (newState.count == _options.MaxPings - 1)
            return string.Format(_options.WarningMessage, 1, _options.TimeoutSeconds);

        if (newState.count >= _options.MaxPings)
        {
            var timeoutUntil = DateTime.UtcNow.AddSeconds(_options.TimeoutSeconds);
            _userPingState[userId] = (0, timeoutUntil);

            if (_options.ServerTimeoutMinutes > 0 && member != null)
            {
                try
                {
                    var until = DateTimeOffset.UtcNow.AddMinutes(_options.ServerTimeoutMinutes);
                    await member.TimeoutAsync(until, "Exceeded max ping limit.");
                    _logger?.LogInformation("Timed out user {UserId} for {Minutes} minutes.", userId, _options.ServerTimeoutMinutes);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to timeout user {UserId}. Missing permissions?", userId);
                }
            }

            return string.Format(_options.MaxPingsMessage, _options.MaxPings, _options.TimeoutSeconds);
        }

        return GetResponseForUser(userId);
    }

    public void SetPersonalizedResponse(ulong userId, string response)
    {
        _personalizedResponses[userId] = response;
        _ = SaveAsync();
    }

    public bool RemovePersonalizedResponse(ulong userId)
    {
        var removed = _personalizedResponses.Remove(userId);
        if (removed) _ = SaveAsync();
        return removed;
    }

    public Dictionary<ulong, string> GetAllPersonalizedResponses() =>
        new Dictionary<ulong, string>(_personalizedResponses);

    public void DecayPingCounts()
    {
        foreach (var userId in _userPingState.Keys.ToList())
        {
            if (!_userPingState.TryGetValue(userId, out var state)) continue;
            if (state.timeoutUntil > DateTime.UtcNow) continue;

            int newCount = Math.Max(0, state.count - 1);
            if (newCount == 0)
                _ = _userPingState.TryRemove(userId, out _);
            else
                _ = _userPingState.AddOrUpdate(userId, (newCount, DateTime.MinValue), (_, _) => (newCount, DateTime.MinValue));
        }
    }
}