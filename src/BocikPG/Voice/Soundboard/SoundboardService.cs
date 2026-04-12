using System.Text.Json;
using BocikPG;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class SoundboardService
{
    private readonly SoundboardOptions _options;
    private readonly string            _dataFilePath;
    private readonly ILogger<SoundboardService> _logger;
    private List<SoundDefinition>      _sounds = [];

    public SoundboardService(
        IOptions<SoundboardOptions> options,
        ILogger<SoundboardService> logger)
    {
        _options      = options.Value;
        _dataFilePath = Path.Combine(AppContext.BaseDirectory, _options.SoundDefinitionsFile);
        _logger       = logger;
        LoadSounds();
    }

    // ── Data access ───────────────────────────────────────────────────────────

    public IReadOnlyList<SoundDefinition> GetAllSounds() => _sounds.AsReadOnly();

    public SoundDefinition? GetSound(string name) =>
        _sounds.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public void AddSound(SoundDefinition sound)
    {
        _sounds.Add(sound);
        SaveSounds();
    }

    public bool RemoveSound(string name)
    {
        var removed = _sounds.RemoveAll(
            s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed) SaveSounds();
        return removed;
    }

    // ── Component builder ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds Discord action rows (3 columns, max 5 rows = 15 buttons per call).
    /// Used by SoundboardCommands to render the soundboard message.
    /// </summary>
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

    // ── Persistence ───────────────────────────────────────────────────────────

    private void LoadSounds()
    {
        if (!File.Exists(_dataFilePath))
        {
            _logger.LogInformation(
                "Soundboard file not found at {Path}, starting with empty list.", _dataFilePath);
            _sounds = [];
            SaveSounds();
            return;
        }

        try
        {
            var json = File.ReadAllText(_dataFilePath);
            var data = JsonSerializer.Deserialize<SoundboardData>(json);
            _sounds = data?.Sounds ?? [];
            _logger.LogInformation("Loaded {Count} sounds from {Path}", _sounds.Count, _dataFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load soundboard from {Path}", _dataFilePath);
            _sounds = [];
        }
    }

    private void SaveSounds()
    {
        try
        {
            var data = new SoundboardData { Sounds = _sounds };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            Directory.CreateDirectory(Path.GetDirectoryName(_dataFilePath)!);
            File.WriteAllText(_dataFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save soundboard to {Path}", _dataFilePath);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves an emoji string to DiscordComponentEmoji without using FromUnicode.
    /// Supports:
    ///   - Unicode emoji:              "😂"
    ///   - Custom emoji (full format): "&lt;:name:123456789&gt;" or "&lt;a:name:123456789&gt;"
    ///   - Raw snowflake ID:           "123456789012345678"
    /// </summary>
    private static DiscordComponentEmoji? ResolveEmoji(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // <:name:id> or <a:name:id>
        if (raw.StartsWith('<') && raw.EndsWith('>'))
        {
            var inner = raw.Trim('<', '>');
            if (inner.StartsWith('a')) inner = inner[1..];
            inner = inner.TrimStart(':');
            var parts = inner.Split(':');
            if (parts.Length == 2 && ulong.TryParse(parts[1], out var eid))
                return new DiscordComponentEmoji(eid);
        }

        // Raw snowflake ID
        if (ulong.TryParse(raw, out var snowflake))
            return new DiscordComponentEmoji(snowflake);

        // Unicode emoji — constructor accepts the string directly
        return new DiscordComponentEmoji(raw.Trim(':'));
    }
}