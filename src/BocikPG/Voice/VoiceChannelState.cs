// Channel points configuration
public class ChannelPointsConfig
{
    public Dictionary<ulong, double> ChannelPoints { get; set; } = new();
}

// Voice channel state tracking
public class VoiceChannelState
{
    public ulong ChannelId { get; set; }
    public HashSet<ulong> ActiveUsers { get; set; } = new();
    public bool IsBotConnected { get; set; }
    public DateTime LastActivity { get; set; }
}