using BocikPG.Soundboard;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;

namespace BocikPG;

/// <summary>
/// Listens for voice-state changes and plays a per-user join or leave sound.
/// Falls back to a random sound when no assignment exists.
/// Ignores bot users and events where the guild cannot be determined.
/// </summary>
public class VoiceJoinLeaveHandler : IEventHandler<VoiceStateUpdatedEventArgs>
{
    private readonly UserSoundService _userSoundService;
    private readonly SoundPlayerService _soundPlayerService;
    private readonly DiscordClient _discordClient;
    private readonly ILogger<VoiceJoinLeaveHandler> _logger;

    public VoiceJoinLeaveHandler(
        UserSoundService userSoundService,
        SoundPlayerService soundPlayerService,
        DiscordClient discordClient,
        ILogger<VoiceJoinLeaveHandler> logger)
    {
        _userSoundService = userSoundService;
        _soundPlayerService = soundPlayerService;
        _discordClient = discordClient;
        _logger = logger;
    }

    public async Task HandleEventAsync(DiscordClient sender, VoiceStateUpdatedEventArgs args)
    {
        // Ignore bot accounts (including ourselves).
        if ((await sender.GetUserAsync(args.UserId)).IsBot == true) return;
        if (!args.GuildId.HasValue) return;

        var guildId = args.GuildId.Value;
        var userId  = args.UserId;

        var previousChannelId = args.Before?.ChannelId;
        var currentChannelId  = args.After?.ChannelId;

        // Determine event type. Mute/unmute/deafen changes both fields equal — skip.
        bool joined = previousChannelId is null && currentChannelId is not null;
        bool left   = previousChannelId is not null && currentChannelId is null;
        bool moved  = previousChannelId is not null && currentChannelId is not null
                      && previousChannelId != currentChannelId;

        if (!joined && !left && !moved) return;

        if (joined || moved)
        {
            var targetChannel = currentChannelId!.Value;
            var soundName = _userSoundService.GetJoinSound(userId);

            // Assigned join sound → JoinSound source; random fallback → Random source.
            PlayContext context;
            SoundPlayerService.PlayResult result;

            if (soundName is not null)
            {
                context = new PlayContext(userId, SoundTriggerSource.JoinSound);
                result  = await _soundPlayerService.PlayByNameAsync(guildId, targetChannel, soundName, context);
            }
            else
            {
                context = new PlayContext(userId, SoundTriggerSource.Random);
                result  = await _soundPlayerService.PlayRandomAsync(guildId, targetChannel, context);
            }

            LogResult(result, userId, guildId, "join", soundName);
        }
        else // left
        {
            // Play the leave sound in the same channel the bot currently occupies,
            // if it is connected. We can't join an empty channel just to play a leave sound.
            var botChannelId = await GetBotVoiceChannelAsync(guildId);
            if (botChannelId is null)
            {
                _logger.LogDebug(
                    "User {UserId} left guild {GuildId} but bot is not in a voice channel — skipping leave sound.",
                    userId, guildId);
                return;
            }

            var soundName = _userSoundService.GetLeaveSound(userId);

            PlayContext context;
            SoundPlayerService.PlayResult result;

            if (soundName is not null)
            {
                context = new PlayContext(userId, SoundTriggerSource.LeaveSound);
                result  = await _soundPlayerService.PlayByNameAsync(guildId, botChannelId.Value, soundName, context);
            }
            else
            {
                context = new PlayContext(userId, SoundTriggerSource.Random);
                result  = await _soundPlayerService.PlayRandomAsync(guildId, botChannelId.Value, context);
            }

            LogResult(result, userId, guildId, "leave", soundName);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<ulong?> GetBotVoiceChannelAsync(ulong guildId)
    {
        try
        {
            var guild = await _discordClient.GetGuildAsync(guildId);
            var botMember = await guild.GetMemberAsync(_discordClient.CurrentUser.Id);
            return botMember.VoiceState?.ChannelId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not determine bot voice channel in guild {GuildId}", guildId);
            return null;
        }
    }

    private void LogResult(
        SoundPlayerService.PlayResult result,
        ulong userId, ulong guildId,
        string eventType, string? soundName)
    {
        switch (result)
        {
            case SoundPlayerService.PlayResult.Ok:
                _logger.LogDebug(
                    "Played {EventType} sound '{Sound}' for user {UserId} in guild {GuildId}",
                    eventType, soundName ?? "<random>", userId, guildId);
                break;

            case SoundPlayerService.PlayResult.NoSounds:
                _logger.LogDebug(
                    "No sounds available for {EventType} event in guild {GuildId} — nothing played.",
                    eventType, guildId);
                break;

            case SoundPlayerService.PlayResult.NotFound:
                _logger.LogWarning(
                    "Assigned {EventType} sound '{Sound}' for user {UserId} not found in guild {GuildId}.",
                    eventType, soundName, userId, guildId);
                break;

            case SoundPlayerService.PlayResult.FileMissing:
                _logger.LogWarning(
                    "{EventType} sound file missing for sound '{Sound}', user {UserId}, guild {GuildId}.",
                    eventType, soundName, userId, guildId);
                break;

            case SoundPlayerService.PlayResult.PlayerUnavailable:
            case SoundPlayerService.PlayResult.Timeout:
            case SoundPlayerService.PlayResult.Error:
                _logger.LogWarning(
                    "Could not play {EventType} sound for user {UserId} in guild {GuildId}: {Result}",
                    eventType, userId, guildId, result);
                break;
        }
    }
}
