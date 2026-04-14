namespace BocikPG.Soundboard;

public enum SoundTriggerSource
{
    Soundboard,
    JoinSound,
    LeaveSound,
    Random,
}

/// <summary>
/// Carries caller-side context into <see cref="SoundPlayerService"/> so that
/// play events can be attributed to a specific user and trigger source.
/// </summary>
public record PlayContext(ulong UserId, SoundTriggerSource Source);
