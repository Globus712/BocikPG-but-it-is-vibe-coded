using System.Text.Json;
using BocikPG;
using BocikPG.Sync;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BocikPG.Soundboard;

/// <summary>
/// Persists soundboard message IDs per guild to disk so updates survive restarts.
/// Storage format: { "guildId": { "pageIndex": { "channelId": ulong, "messageId": ulong } } }
/// </summary>
public class SoundboardMessageStore : IReloadable
{
    private Dictionary<ulong, Dictionary<int, StoredMessage>> _data = new();

    private readonly DiscordClient _client;
    private readonly string _filePath;
    private readonly ILogger<SoundboardMessageStore> _logger;
    private readonly GitSyncService _syncService;

    public record StoredMessage(ulong ChannelId, ulong MessageId);

    public SoundboardMessageStore(
        DiscordClient client,
        IOptions<SoundboardOptions> options,
        ILogger<SoundboardMessageStore> logger,
        GitSyncService syncService)
    {
        _client = client;
        _filePath = Path.Combine(AppContext.BaseDirectory, options.Value.MessageStorageFile);
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
            _logger.LogInformation("No soundboard message store found at {Path}, starting fresh.", _filePath);
            Interlocked.Exchange(ref _data, new Dictionary<ulong, Dictionary<int, StoredMessage>>());
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var raw = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, StoredMessage>>>(json);
            if (raw is null) return;

            var loaded = raw.ToDictionary(
                outer => ulong.Parse(outer.Key),
                outer => outer.Value.ToDictionary(
                    inner => int.Parse(inner.Key),
                    inner => inner.Value));

            Interlocked.Exchange(ref _data, loaded);
            _logger.LogInformation("Loaded soundboard message store for {Count} guild(s).", _data.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load soundboard message store from {Path}", _filePath);
        }
    }

    private void Save()
    {
        try
        {
            var raw = _data.ToDictionary(
                outer => outer.Key.ToString(),
                outer => outer.Value.ToDictionary(
                    inner => inner.Key.ToString(),
                    inner => inner.Value));

            var json = JsonSerializer.Serialize(raw, new JsonSerializerOptions { WriteIndented = true });
            _ = Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, json);

            _ = _syncService.SyncFileAsync(Path.GetFullPath(_filePath), "Update soundboard message store");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save soundboard message store to {Path}", _filePath);
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public bool GuildHasSoundboard(ulong guildId) =>
        _data.ContainsKey(guildId) && _data[guildId].Count > 0;

    public void Set(ulong guildId, int page, DiscordMessage message)
    {
        if (!_data.ContainsKey(guildId))
            _data[guildId] = new Dictionary<int, StoredMessage>();

        _data[guildId][page] = new StoredMessage(message.ChannelId, message.Id);
        Save();
    }

    public async Task<DiscordMessage?> GetAsync(ulong guildId, int page)
    {
        if (!_data.TryGetValue(guildId, out var pages)) return null;
        if (!pages.TryGetValue(page, out var stored)) return null;

        try
        {
            var channel = await _client.GetChannelAsync(stored.ChannelId);
            return await channel.GetMessageAsync(stored.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not fetch stored soundboard message {MessageId} in channel {ChannelId}",
                stored.MessageId, stored.ChannelId);
            return null;
        }
    }

    public int PageCount(ulong guildId) =>
        _data.TryGetValue(guildId, out var pages) ? pages.Count : 0;

    public void Clear(ulong guildId)
    {
        _ = _data.Remove(guildId);
        Save();
    }

    public void TrimTo(ulong guildId, int keepPages)
    {
        if (!_data.TryGetValue(guildId, out var pages)) return;
        var toRemove = pages.Keys.Where(k => k >= keepPages).ToList();
        foreach (var key in toRemove)
            _ = pages.Remove(key);
        Save();
    }
}