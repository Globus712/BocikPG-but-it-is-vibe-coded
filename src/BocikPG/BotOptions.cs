namespace BocikPG;

public class BotOptions
{
    public string Token { get; set; } = "";
    public ulong OwnerId { get; set; } = 0;   // Bot owner's Discord user ID
}

public class LavalinkOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 2333;
    public string Password { get; set; } = "youshallnotpass";
}

public class PingOptions
{
    public bool TtsEnabled { get; set; } = false;
    public int MaxPings { get; set; } = 5;
    public int TimeoutSeconds { get; set; } = 60;
    public string DefaultResponse { get; set; } = "You pinged me! Stop that!";
    public ulong OwnerId { get; set; } = 0;
    public string MaxPingsMessage { get; set; } = "You've pinged me {0} times. I'm ignoring you for {1} seconds.";
    public string WarningMessage { get; set; } = "Warning! You have {0} more ping(s) before being ignored for {1} seconds.";
    public bool DecayEnabled { get; set; } = true;
    public int DecayIntervalMinutes { get; set; } = 60;   // default 1 hour
    public int ServerTimeoutMinutes { get; set; } = 0;
}

public class VoiceOptions
{
    public bool AutoJoinEnabled { get; set; } = true;
    public string WeightsFilePath { get; set; } = "Resources/Voice/user_weights.json";
}

public class SoundboardOptions
{
    public string SoundFilesPath { get; set; } = "Resources/Soundboard/Sounds";
    public string SoundDefinitionsFile { get; set; } = "Resources/Soundboard/soundboard.json";
    public string SoundBaseUrl { get; set; } = ""; // optional HTTP base URL
    public string MessageStorageFile { get; set; } = "Resources/Soundboard/soundboard_messages.json";
}