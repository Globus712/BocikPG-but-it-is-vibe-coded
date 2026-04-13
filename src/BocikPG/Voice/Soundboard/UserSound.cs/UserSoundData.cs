namespace BocikPG.Soundboard;

/// <summary>
/// Persisted data for per-user join/leave sound assignments.
/// Keys are Discord user IDs (as strings for JSON compatibility).
/// Values are sound names matching <see cref="SoundDefinition.Name"/>.
/// </summary>
public class UserSoundData
{
    /// <summary>Sound played when the user joins a voice channel.</summary>
    public Dictionary<string, string> JoinSounds { get; set; } = new();

    /// <summary>Sound played when the user leaves a voice channel.</summary>
    public Dictionary<string, string> LeaveSounds { get; set; } = new();
}
