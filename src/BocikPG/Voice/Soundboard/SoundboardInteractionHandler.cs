using BocikPG;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Lavalink4NET;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class SoundboardInteractionHandler : IEventHandler<ComponentInteractionCreatedEventArgs>
{
    private readonly SoundboardService _soundboardService;
    private readonly IAudioService _audioService;
    private readonly SoundboardOptions _options;
    private readonly ILogger<SoundboardInteractionHandler> _logger;

    public SoundboardInteractionHandler(
        SoundboardService soundboardService,
        IAudioService audioService,
        IOptions<SoundboardOptions> options,
        ILogger<SoundboardInteractionHandler> logger)
    {
        _soundboardService = soundboardService;
        _audioService = audioService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleEventAsync(DiscordClient sender, ComponentInteractionCreatedEventArgs args)
    {
        var customId = args.Interaction.Data.CustomId;
        if (!customId.StartsWith("sound_")) return;

        // Handle stop button
        if (customId == "sound_stop")
        {
            await HandleStopAsync(args);
            return;
        }

        // Handle sound playback
        if (!int.TryParse(customId["sound_".Length..], out var soundIndex))
            return;

        await HandleSoundPlaybackAsync(args, soundIndex);
    }

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

        var sound = allSounds[soundIndex];
        await args.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);

        // Verify user is in a voice channel
        var member = await args.Interaction.Guild!.GetMemberAsync(args.Interaction.User.Id);
        var voiceChannelId = member?.VoiceState?.ChannelId;
        if (voiceChannelId is null)
        {
            await SendEphemeralErrorAsync(args.Interaction, "❌ You are not in a voice channel.");
            return;
        }

        // Validate sound file exists
        var filePath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, _options.SoundFilesPath, sound.Filename));
        if (!File.Exists(filePath))
        {
            await SendEphemeralErrorAsync(args.Interaction,
                $"❌ Sound file not found: `{sound.Filename}`\nExpected at: `{filePath}`");
            return;
        }

        try
        {
            // Try to get existing player
            var player = await _audioService.Players.GetPlayerAsync<LavalinkPlayer>(args.Interaction.Guild.Id);

            // If no valid player exists, retrieve (join) a new one
            if (player is null || player.State is PlayerState.Destroyed)
            {
                _logger.LogDebug("No connected player found for guild {GuildId}, retrieving new one", args.Interaction.Guild.Id);

                var result = await _audioService.Players.RetrieveAsync<LavalinkPlayer, LavalinkPlayerOptions>(
                    args.Interaction.Guild.Id,
                    voiceChannelId.Value,
                    PlayerFactory.Default,
                    Options.Create(new LavalinkPlayerOptions()),
                    new PlayerRetrieveOptions(
                        ChannelBehavior: PlayerChannelBehavior.Join,
                        VoiceStateBehavior: MemberVoiceStateBehavior.AlwaysRequired));

                if (!result.IsSuccess)
                {
                    _logger.LogWarning("Failed to retrieve player for guild {GuildId}: {Status}", args.Interaction.Guild.Id, result.Status);
                    await SendEphemeralErrorAsync(args.Interaction, "❌ Could not connect to voice channel.");
                    return;
                }

                player = result.Player;
            }

            // Play the sound
            await player.PlayFileAsync(new FileInfo(filePath));
            _logger.LogDebug("Playing sound '{SoundName}' in guild {GuildId}", sound.Name, args.Interaction.Guild.Id);
        }
        catch (TimeoutException)
        {
            // Player retrieval timed out – destroy stale session so user can retry
            var stale = await _audioService.Players.GetPlayerAsync<LavalinkPlayer>(args.Interaction.Guild.Id);
            if (stale is not null)
            {
                await stale.DisconnectAsync();
                _logger.LogWarning("Destroyed stale player for guild {GuildId} after timeout", args.Interaction.Guild.Id);
            }

            await SendEphemeralErrorAsync(args.Interaction, "❌ Connection timed out. Please try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to play sound '{SoundName}' in guild {GuildId}", sound.Name, args.Interaction.Guild.Id);
            await SendEphemeralErrorAsync(args.Interaction, "❌ Failed to play sound. Check logs for details.");
        }
    }

    private async Task SendEphemeralErrorAsync(DiscordInteraction interaction, string message)
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