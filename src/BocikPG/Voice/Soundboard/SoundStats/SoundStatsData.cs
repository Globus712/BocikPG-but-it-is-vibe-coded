namespace BocikPG.Soundboard;

/// <summary>
/// Persisted stats file structure.
/// Layout: GuildId (string) → UserId (string) → "{Source}:{SoundName}" → play count.
/// UserId "0" is used when no context is available.
/// </summary>
public class SoundStatsData
{
    /// <summary>
    /// Outer key: guild ID as string.
    /// Middle key: user ID as string ("0" = unknown).
    /// Inner key: "{TriggerSource}:{SoundName}" e.g. "Soundboard:airhorn".
    /// Value: total play count.
    /// </summary>
    public Dictionary<string, Dictionary<string, Dictionary<string, long>>> Counts { get; set; } = new();
}
