using BocikPG;
using BocikPG.Soundboard;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Lavalink4NET;
using Lavalink4NET.Players;
using Microsoft.Extensions.Logging;

public class SoundboardInteractionHandler : IEventHandler<ComponentInteractionCreatedEventArgs>
{
    private readonly SoundboardService _soundboardService;
    private readonly SoundPlayerService _soundPlayerService;
    private readonly IAudioService _audioService;
    private readonly ILogger<SoundboardInteractionHandler> _logger;

    public SoundboardInteractionHandler(
        SoundboardService soundboardService,
        SoundPlayerService soundPlayerService,
        IAudioService audioService,
        ILogger<SoundboardInteractionHandler> logger)
    {
        _soundboardService = soundboardService;
        _soundPlayerService = soundPlayerService;
        _audioService = audioService;
        _logger = logger;
    }

    public async Task HandleEventAsync(DiscordClient sender, ComponentInteractionCreatedEventArgs args)
    {
        var customId = args.Interaction.Data.CustomId;
        if (!customId.StartsWith("sound_")) return;

        if (customId == "sound_stop")
        {
            await HandleStopAsync(args);
            return;
        }

        if (!int.TryParse(customId["sound_".Length..], out var soundIndex))
            return;

        await HandleSoundPlaybackAsync(args, soundIndex);
    }

    // ── Stop ──────────────────────────────────────────────────────────────────

    private async Task HandleStopAsync(ComponentInteractionCreatedEventArgs args)
    {
        await args.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);

        var player = await _audioService.Players.GetPlayerAsync<LavalinkPlayer>(args.Interaction.Guild!.Id);
        if (player is not null && player.State == PlayerState.Playing)
        {
            await player.StopAsync();
            _logger.LogDebug("Stopped playback in guild {GuildId}", args.Interaction.Guild.Id);
        }
    }

    // ── Sound playback ────────────────────────────────────────────────────────

    private async Task HandleSoundPlaybackAsync(ComponentInteractionCreatedEventArgs args, int soundIndex)
    {
        var allSounds = _soundboardService.GetAllSounds();
        if (soundIndex < 0 || soundIndex >= allSounds.Count)
        {
            await args.Interaction.CreateResponseAsync(
                DiscordInteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent("❌ Sound not found. Try running `/soundboard update`.")
                    .AsEphemeral());
            return;
        }

        await args.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);

        var member = await args.Interaction.Guild!.GetMemberAsync(args.Interaction.User.Id);
        var voiceChannelId = member?.VoiceState?.ChannelId;
        if (voiceChannelId is null)
        {
            await SendEphemeralFollowupAsync(args.Interaction, "❌ You are not in a voice channel.");
            return;
        }

        var sound = allSounds[soundIndex];
        var result = await _soundPlayerService.PlaySoundAsync(
            args.Interaction.Guild.Id, voiceChannelId.Value, sound);

        var errorMessage = result switch
        {
            SoundPlayerService.PlayResult.Ok              => null,
            SoundPlayerService.PlayResult.FileMissing     => $"❌ Sound file not found: `{sound.Filename}`",
            SoundPlayerService.PlayResult.PlayerUnavailable => "❌ Could not connect to voice channel.",
            SoundPlayerService.PlayResult.Timeout         => "❌ Connection timed out. Please try again.",
            _                                             => "❌ Failed to play sound. Check logs for details."
        };

        if (errorMessage is not null)
            await SendEphemeralFollowupAsync(args.Interaction, errorMessage);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task SendEphemeralFollowupAsync(DiscordInteraction interaction, string message)
    {
        try
        {
            await interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                .WithContent(message)
                .AsEphemeral());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send ephemeral error message: {Message}", message);
        }
    }
}
