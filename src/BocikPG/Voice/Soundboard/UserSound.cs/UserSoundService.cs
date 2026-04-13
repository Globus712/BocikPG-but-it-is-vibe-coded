using System.Text.Json;
using BocikPG.Sync;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BocikPG.Soundboard;

/// <summary>
/// Stores and persists per-user join/leave sound assignments.
/// Implements <see cref="IReloadable"/> so the Git sync coordinator
/// can reload data after a pull.
/// </summary>
public class UserSoundService : IReloadable
{
    private readonly SoundboardOptions _options;
    private readonly string _dataFilePath;
    private readonly GitSyncService _syncService;
    private readonly ILogger<UserSoundService> _logger;

    // Separate locks for the two tables so concurrent reads are safe.
    private UserSoundData _data = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public UserSoundService(
        IOptions<SoundboardOptions> options,
        GitSyncService syncService,
        ILogger<UserSoundService> logger)
    {
        _options = options.Value;
        _dataFilePath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, _options.UserSoundsFile));
        _syncService = syncService;
        _logger = logger;
        LoadData();
    }

    // ── IReloadable ───────────────────────────────────────────────────────────

    public Task ReloadAsync()
    {
        LoadData();
        return Task.CompletedTask;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the sound name assigned to <paramref name="userId"/> for joining,
    /// or <c>null</c> if none is configured.
    /// </summary>
    public string? GetJoinSound(ulong userId) =>
        _data.JoinSounds.TryGetValue(userId.ToString(), out var name) ? name : null;

    /// <summary>
    /// Returns the sound name assigned to <paramref name="userId"/> for leaving,
    /// or <c>null</c> if none is configured.
    /// </summary>
    public string? GetLeaveSound(ulong userId) =>
        _data.LeaveSounds.TryGetValue(userId.ToString(), out var name) ? name : null;

    /// <summary>Sets the join sound for a user and persists immediately.</summary>
    public Task SetJoinSoundAsync(ulong userId, string soundName)
    {
        _data.JoinSounds[userId.ToString()] = soundName;
        return SaveDataAsync("Update user join sounds");
    }

    /// <summary>Sets the leave sound for a user and persists immediately.</summary>
    public Task SetLeaveSoundAsync(ulong userId, string soundName)
    {
        _data.LeaveSounds[userId.ToString()] = soundName;
        return SaveDataAsync("Update user leave sounds");
    }

    /// <summary>
    /// Removes the join sound assignment for a user.
    /// Returns <c>true</c> if an entry existed.
    /// </summary>
    public Task<bool> RemoveJoinSoundAsync(ulong userId)
    {
        var removed = _data.JoinSounds.Remove(userId.ToString());
        if (removed) _ = SaveDataAsync("Remove user join sound");
        return Task.FromResult(removed);
    }

    /// <summary>
    /// Removes the leave sound assignment for a user.
    /// Returns <c>true</c> if an entry existed.
    /// </summary>
    public Task<bool> RemoveLeaveSoundAsync(ulong userId)
    {
        var removed = _data.LeaveSounds.Remove(userId.ToString());
        if (removed) _ = SaveDataAsync("Remove user leave sound");
        return Task.FromResult(removed);
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private void LoadData()
    {
        if (!File.Exists(_dataFilePath))
        {
            _logger.LogInformation(
                "User sounds file not found at {Path}, starting with empty data.", _dataFilePath);
            _data = new UserSoundData();
            _ = SaveDataAsync("Initialise user sounds file");
            return;
        }

        try
        {
            var json = File.ReadAllText(_dataFilePath);
            _data = JsonSerializer.Deserialize<UserSoundData>(json) ?? new UserSoundData();
            _logger.LogInformation(
                "Loaded user sounds: {JoinCount} join, {LeaveCount} leave assignments.",
                _data.JoinSounds.Count, _data.LeaveSounds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load user sounds from {Path}", _dataFilePath);
            _data = new UserSoundData();
        }
    }

    private async Task SaveDataAsync(string commitMessage)
    {
        await _saveLock.WaitAsync();
        try
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(_dataFilePath)!);
            var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_dataFilePath, json);
            await _syncService.SyncFileAsync(_dataFilePath, commitMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save user sounds to {Path}", _dataFilePath);
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
