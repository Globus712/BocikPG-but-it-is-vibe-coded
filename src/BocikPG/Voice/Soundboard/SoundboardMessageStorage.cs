using System.Text.Json;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BocikPG.Soundboard;

/// <summary>
/// Persists soundboard message IDs per guild to disk so updates survive restarts.
/// Storage format: { "guildId": { "pageIndex": { "channelId": ulong, "messageId": ulong } } }
/// </summary>
public class SoundboardMessageStore
{
    // In-memory: guildId -> pageIndex -> (channelId, messageId)
    private Dictionary<ulong, Dictionary<int, StoredMessage>> _data = new();

    private readonly DiscordClient _client;
    private readonly string        _filePath;
    private readonly ILogger<SoundboardMessageStore> _logger;

    public record StoredMessage(ulong ChannelId, ulong MessageId);

    public SoundboardMessageStore(
        DiscordClient client,
        IOptions<SoundboardOptions> options,
        ILogger<SoundboardMessageStore> logger)
    {
        _client   = client;
        _filePath = Path.Combine(AppContext.BaseDirectory, options.Value.MessageStorageFile);
        _logger   = logger;
        Load();
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
        _data.Remove(guildId);
        Save();
    }

    /// <summary>Removes stored page entries above <paramref name="keepPages"/> for a guild.</summary>
    public void TrimTo(ulong guildId, int keepPages)
    {
        if (!_data.TryGetValue(guildId, out var pages)) return;
        var toRemove = pages.Keys.Where(k => k >= keepPages).ToList();
        foreach (var key in toRemove)
            pages.Remove(key);
        Save();
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            _logger.LogInformation("No soundboard message store found at {Path}, starting fresh.", _filePath);
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            // Deserialize as string keys then convert to ulong
            var raw = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, StoredMessage>>>(json);
            if (raw is null) return;

            _data = raw.ToDictionary(
                outer => ulong.Parse(outer.Key),
                outer => outer.Value.ToDictionary(
                    inner => int.Parse(inner.Key),
                    inner => inner.Value
                )
            );
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
            // Serialize with string keys (JSON only supports string keys)
            var raw = _data.ToDictionary(
                outer => outer.Key.ToString(),
                outer => outer.Value.ToDictionary(
                    inner => inner.Key.ToString(),
                    inner => inner.Value
                )
            );

            var json = JsonSerializer.Serialize(raw, new JsonSerializerOptions { WriteIndented = true });
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save soundboard message store to {Path}", _filePath);
        }
    }
}