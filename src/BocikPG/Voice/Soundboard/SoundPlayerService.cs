using BocikPG;
using Lavalink4NET;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BocikPG.Soundboard;

/// <summary>
/// Shared helper that resolves a <see cref="SoundDefinition"/> by name,
/// obtains (or creates) a Lavalink player for the given guild/channel,
/// and plays the sound file.
/// </summary>
public class SoundPlayerService
{
    private readonly SoundboardService _soundboardService;
    private readonly IAudioService _audioService;
    private readonly SoundStatsService _statsService;
    private readonly SoundboardOptions _options;
    private readonly ILogger<SoundPlayerService> _logger;

    public SoundPlayerService(
        SoundboardService soundboardService,
        IAudioService audioService,
        SoundStatsService statsService,
        IOptions<SoundboardOptions> options,
        ILogger<SoundPlayerService> logger)
    {
        _soundboardService = soundboardService;
        _audioService = audioService;
        _statsService = statsService;
        _options = options.Value;
        _logger = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Plays the sound with the given name in <paramref name="voiceChannelId"/>.
    /// Returns a <see cref="PlayResult"/> describing what happened.
    /// </summary>
    public Task<PlayResult> PlayByNameAsync(
        ulong guildId, ulong voiceChannelId, string soundName, PlayContext? context = null) =>
        PlayCoreAsync(guildId, voiceChannelId,
            _soundboardService.GetSound(soundName),
            soundName,
            context);

    /// <summary>
    /// Plays a random sound from the soundboard in <paramref name="voiceChannelId"/>.
    /// Returns <see cref="PlayResult.NoSounds"/> when the board is empty.
    /// </summary>
    public Task<PlayResult> PlayRandomAsync(
        ulong guildId, ulong voiceChannelId, PlayContext? context = null)
    {
        var all = _soundboardService.GetAllSounds();
        if (all.Count == 0)
            return Task.FromResult(PlayResult.NoSounds);

        var sound = all[Random.Shared.Next(all.Count)];
        return PlayCoreAsync(guildId, voiceChannelId, sound, sound.Name, context);
    }

    /// <summary>
    /// Plays a specific <see cref="SoundDefinition"/> directly (used when the caller
    /// already holds the object, e.g. from an index-based interaction).
    /// </summary>
    public Task<PlayResult> PlaySoundAsync(
        ulong guildId, ulong voiceChannelId, SoundDefinition sound, PlayContext? context = null) =>
        PlayCoreAsync(guildId, voiceChannelId, sound, sound.Name, context);

    // ── Core ──────────────────────────────────────────────────────────────────

    private async Task<PlayResult> PlayCoreAsync(
        ulong guildId,
        ulong voiceChannelId,
        SoundDefinition? sound,
        string soundName,
        PlayContext? context)
    {
        if (sound is null)
        {
            _logger.LogWarning("Sound '{Name}' not found for guild {GuildId}", soundName, guildId);
            return PlayResult.NotFound;
        }

        var filePath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, _options.SoundFilesPath, sound.Filename));

        if (!File.Exists(filePath))
        {
            _logger.LogWarning(
                "Sound file missing for '{Name}' at {Path} (guild {GuildId})",
                sound.Name, filePath, guildId);
            return PlayResult.FileMissing;
        }

        try
        {
            var player = await ObtainPlayerAsync(guildId, voiceChannelId);
            if (player is null)
                return PlayResult.PlayerUnavailable;

            var obtained = await ObtainPlayerAsync(guildId, voiceChannelId);
            if (obtained.Status == ObtainResult.WrongChannel) return PlayResult.WrongChannel;
            if (obtained.Player is null) return PlayResult.PlayerUnavailable;

            await obtained.Player.PlayFileAsync(new FileInfo(filePath));

            _statsService.Record(guildId, sound, context);

            _logger.LogDebug(
                "Playing '{SoundName}' in guild {GuildId} channel {ChannelId}",
                sound.Name, guildId, voiceChannelId);
            return PlayResult.Ok;
        }
        catch (TimeoutException)
        {
            // Destroy the stale session so the next attempt starts fresh.
            var stale = await _audioService.Players.GetPlayerAsync<LavalinkPlayer>(guildId);
            if (stale is not null)
            {
                await stale.DisconnectAsync();
                _logger.LogWarning(
                    "Destroyed stale player for guild {GuildId} after timeout", guildId);
            }
            return PlayResult.Timeout;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to play '{SoundName}' in guild {GuildId}", sound.Name, guildId);
            return PlayResult.Error;
        }
    }

    /// <summary>
    /// Returns an existing, healthy player or joins/moves to <paramref name="channelId"/>.
    /// Returns <c>null</c> if the player could not be obtained.
    /// </summary>
    // Replace the ObtainPlayerAsync return type with a small discriminated result:
    private enum ObtainResult { Ok, WrongChannel, Unavailable }
    private record ObtainPlayerResult(LavalinkPlayer? Player, ObtainResult Status);

    private async Task<ObtainPlayerResult> ObtainPlayerAsync(ulong guildId, ulong channelId)
    {
        var player = await _audioService.Players.GetPlayerAsync<LavalinkPlayer>(guildId);

        if (player is not null && player.State is not PlayerState.Destroyed)
        {
            if (player.VoiceChannelId != channelId)
            {
                _logger.LogWarning(
                    "Guild {GuildId}: playback blocked — requester in channel {Requested}, " +
                    "bot in channel {Bot}",
                    guildId, channelId, player.VoiceChannelId);
                return new(null, ObtainResult.WrongChannel);
            }

            return new(player, ObtainResult.Ok);
        }

        _logger.LogDebug(
            "No active player for guild {GuildId}, retrieving new one for channel {ChannelId}",
            guildId, channelId);

        var result = await _audioService.Players.RetrieveAsync<LavalinkPlayer, LavalinkPlayerOptions>(
            guildId,
            channelId,
            PlayerFactory.Default,
            Options.Create(new LavalinkPlayerOptions()),
            new PlayerRetrieveOptions(
                ChannelBehavior: PlayerChannelBehavior.Join,
                VoiceStateBehavior: MemberVoiceStateBehavior.AlwaysRequired));

        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "Failed to retrieve player for guild {GuildId}: {Status}", guildId, result.Status);
            return new(null, ObtainResult.Unavailable);
        }

        return new(result.Player, ObtainResult.Ok);
    }

    // ── Result type ───────────────────────────────────────────────────────────

    public enum PlayResult
    {
        Ok,
        NotFound,
        FileMissing,
        PlayerUnavailable,
        WrongChannel,      // ← new
        Timeout,
        NoSounds,
        Error,
    }
}
