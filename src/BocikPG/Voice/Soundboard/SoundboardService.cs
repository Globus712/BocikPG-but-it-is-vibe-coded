using System.Text.Json;
using BocikPG;
using BocikPG.Sync;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class SoundboardService : IReloadable
{
    private readonly SoundboardOptions _options;
    private readonly string _dataFilePath;
    private readonly ILogger<SoundboardService> _logger;
    private readonly GitSyncService _syncService;
    private List<SoundDefinition> _sounds = [];

    public SoundboardService(
        IOptions<SoundboardOptions> options,
        ILogger<SoundboardService> logger,
        GitSyncService syncService)
    {
        _options = options.Value;
        _dataFilePath = Path.Combine(AppContext.BaseDirectory, _options.SoundDefinitionsFile);
        _logger = logger;
        _syncService = syncService;
        LoadSounds();
    }

    // ── IReloadable ───────────────────────────────────────────────────────────

    public Task ReloadAsync()
    {
        LoadSounds();
        return Task.CompletedTask;
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private void LoadSounds()
    {
        if (!File.Exists(_dataFilePath))
        {
            _logger.LogInformation("Soundboard file not found at {Path}, starting with empty list.", _dataFilePath);
            Interlocked.Exchange(ref _sounds, []);
            _ = SaveSounds();
            return;
        }

        try
        {
            var json = File.ReadAllText(_dataFilePath);
            var data = JsonSerializer.Deserialize<SoundboardData>(json);
            Interlocked.Exchange(ref _sounds, data?.Sounds ?? []);
            _logger.LogInformation("Loaded {Count} sounds from {Path}", _sounds.Count, _dataFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load soundboard from {Path}", _dataFilePath);
            Interlocked.Exchange(ref _sounds, []);
        }
    }

    private async Task SaveSounds()
    {
        try
        {
            var data = new SoundboardData { Sounds = _sounds };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            _ = Directory.CreateDirectory(Path.GetDirectoryName(_dataFilePath)!);
            await File.WriteAllTextAsync(_dataFilePath, json);
            await _syncService.SyncFileAsync(Path.GetFullPath(_dataFilePath), "Update soundboard data");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save soundboard to {Path}", _dataFilePath);
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public IReadOnlyList<SoundDefinition> GetAllSounds() => _sounds.AsReadOnly();

    public async Task SaveAsync() => await SaveSounds();

    public SoundDefinition? GetSound(string name) =>
        _sounds.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public void AddSound(SoundDefinition sound)
    {
        _sounds.Add(sound);
        _ = SaveSounds();
    }

    public bool RemoveSound(string name)
    {
        var removed = _sounds.RemoveAll(
            s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed) _ = SaveSounds();
        return removed;
    }

    public List<DiscordActionRowComponent> BuildActionRows()
    {
        const int columns = 3;
        const int maxRows = 5;

        var buttons = _sounds.Select(sound => new DiscordButtonComponent(
            DiscordButtonStyle.Primary,
            customId: $"sound_{sound.Name}",
            label: sound.Name,
            emoji: ResolveEmoji(sound.Emoji)
        )).ToList();

        var rows = new List<DiscordActionRowComponent>();
        for (int i = 0; i < buttons.Count && rows.Count < maxRows; i += columns)
        {
            var slice = buttons.Skip(i).Take(columns)
                               .Cast<DiscordComponent>()
                               .ToList();
            rows.Add(new DiscordActionRowComponent(slice));
        }

        return rows;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DiscordComponentEmoji? ResolveEmoji(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (raw.StartsWith('<') && raw.EndsWith('>'))
        {
            var inner = raw.Trim('<', '>');
            if (inner.StartsWith('a')) inner = inner[1..];
            inner = inner.TrimStart(':');
            var parts = inner.Split(':');
            if (parts.Length == 2 && ulong.TryParse(parts[1], out var eid))
                return new DiscordComponentEmoji(eid);
        }

        if (ulong.TryParse(raw, out var snowflake))
            return new DiscordComponentEmoji(snowflake);

        return new DiscordComponentEmoji(raw.Trim(':'));
    }
}