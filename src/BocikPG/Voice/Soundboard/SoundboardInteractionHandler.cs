using BocikPG;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Lavalink4NET;
using Lavalink4NET.Players;
using Lavalink4NET.Tracks;
using Lavalink4NET.Rest.Entities.Tracks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class SoundboardInteractionHandler : IEventHandler<ComponentInteractionCreatedEventArgs>
{
    private readonly SoundboardService _soundboardService;
    private readonly IAudioService     _audioService;
    private readonly SoundboardOptions _options;
    private readonly ILogger<SoundboardInteractionHandler> _logger;

    public SoundboardInteractionHandler(
        SoundboardService soundboardService,
        IAudioService audioService,
        IOptions<SoundboardOptions> options,
        ILogger<SoundboardInteractionHandler> logger)
    {
        _soundboardService = soundboardService;
        _audioService      = audioService;
        _options           = options.Value;
        _logger            = logger;
    }

    public async Task HandleEventAsync(DiscordClient sender, ComponentInteractionCreatedEventArgs args)
    {
        var customId = args.Interaction.Data.CustomId;
        if (!customId.StartsWith("sound_")) return;

        // ── Stop button ───────────────────────────────────────────────────────
        if (customId == "sound_stop")
        {
            await args.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);

            var existingPlayer = await _audioService.Players
                .GetPlayerAsync<LavalinkPlayer>(args.Interaction.Guild!.Id);

            if (existingPlayer is not null && existingPlayer.State == PlayerState.Playing)
                await existingPlayer.StopAsync();

            return;
        }

        // ── Sound button ──────────────────────────────────────────────────────
        if (!int.TryParse(customId["sound_".Length..], out var soundIndex))
            return;

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

        // Acknowledge the interaction silently — no visible response to the user
        await args.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);

        // User must be in a voice channel
        var member       = await args.Interaction.Guild!.GetMemberAsync(args.Interaction.User.Id);
        var voiceChannelId = member?.VoiceState?.ChannelId; // ChannelId is ulong?, not .Channel

        if (voiceChannelId is null)
        {
            _ = await args.Interaction.EditOriginalResponseAsync(
                new DiscordWebhookBuilder().WithContent("❌ You are not in a voice channel."));
            return;
        }

        // Resolve sound URI — local file or HTTP base URL
        string soundUri;
        if (!string.IsNullOrEmpty(_options.SoundBaseUrl))
        {
            soundUri = $"{_options.SoundBaseUrl.TrimEnd('/')}/{sound.Filename}";
        }
        else
        {
            var filePath = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, _options.SoundFilesPath, sound.Filename));

            if (!File.Exists(filePath))
            {
                _ = await args.Interaction.EditOriginalResponseAsync(
                    new DiscordWebhookBuilder()
                        .WithContent($"❌ Sound file not found: `{sound.Filename}`\nExpected at: `{filePath}`"));
                return;
            }

            soundUri = new Uri(filePath).AbsoluteUri; // file:///...
        }

        try
        {
            // Get or create a Lavalink player for this guild
            var result = await _audioService.Players.RetrieveAsync<LavalinkPlayer, LavalinkPlayerOptions>(
                args.Interaction.Guild.Id,
                voiceChannelId.Value,
                PlayerFactory.Default,
                Microsoft.Extensions.Options.Options.Create(new LavalinkPlayerOptions()),
                new PlayerRetrieveOptions(ChannelBehavior: PlayerChannelBehavior.Join));

            if (!result.IsSuccess)
            {
                _ = await args.Interaction.EditOriginalResponseAsync(
                    new DiscordWebhookBuilder()
                        .WithContent($"❌ Could not connect to voice: {result.Status}"));
                return;
            }

            var player = result.Player;

            // Stop whatever is currently playing so the new sound starts immediately
            if (player.State == PlayerState.Playing)
                await player.StopAsync();

            var track = await _audioService.Tracks.LoadTrackAsync(soundUri, TrackSearchMode.None);

            if (track is null)
            {
                _ = await args.Interaction.EditOriginalResponseAsync(
                    new DiscordWebhookBuilder()
                        .WithContent($"❌ Lavalink couldn't load `{sound.Filename}`. " +
                                     "Make sure the **Local** source is enabled in your Lavalink `application.yml`."));
                return;
            }

            await player.PlayAsync(track);

            // Volume override if not 100%
            if (sound.Volume != 100)
                await player.SetVolumeAsync(sound.Volume / 100f);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to play sound {SoundName}", sound.Name);
            // Can't send ephemeral after DeferredMessageUpdate — log only
        }
    }
}