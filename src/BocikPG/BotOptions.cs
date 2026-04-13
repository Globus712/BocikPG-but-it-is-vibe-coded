namespace BocikPG;

public class BotOptions
{
    public string Token { get; set; } = "";
    public ulong OwnerId { get; set; } = 0;
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
    public int DecayIntervalMinutes { get; set; } = 60;
    public int ServerTimeoutMinutes { get; set; } = 0;
    public string PersonalizedResponsesFilePath { get; set; } = "Resources/Chat/personalized_responses.json";
}

public class ChatOptions
{
    public string KeywordsFilePath { get; set; } = "Resources/Chat/keywords.json";
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
    public string SoundBaseUrl { get; set; } = "";
    public string MessageStorageFile { get; set; } = "Resources/Soundboard/soundboard_messages.json";

    /// <summary>
    /// Path (relative to AppContext.BaseDirectory) for the per-user
    /// join/leave sound assignment data.
    /// </summary>
    public string UserSoundsFile { get; set; } = "Resources/Soundboard/user_sounds.json";
}

public class GitSyncOptions
{
    public string RepoPath { get; set; } = AppContext.BaseDirectory;
    public string PersonalAccessToken { get; set; } = "";
    public string AuthorName { get; set; } = "BocikPG Bot";
    public string AuthorEmail { get; set; } = "bot@bocikpg.local";
    public string Branch { get; set; } = "main";
    public ulong OwnerId { get; set; } = 0;
    public bool Enabled { get; set; } = false;
}
