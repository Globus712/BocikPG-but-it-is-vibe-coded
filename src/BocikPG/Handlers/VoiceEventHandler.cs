using DSharpPlus;
using DSharpPlus.EventArgs;
using Lavalink4NET;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class VoiceEventHandler : IEventHandler<VoiceStateUpdatedEventArgs>
{
    private readonly VoiceChannelService _voiceService;
    private readonly IAudioService _audioService;
    private readonly ILogger<VoiceEventHandler> _logger;

    public VoiceEventHandler(
        VoiceChannelService voiceService,
        IAudioService audioService,
        ILogger<VoiceEventHandler> logger)
    {
        _voiceService = voiceService;
        _audioService = audioService;
        _logger = logger;
    }

    public async Task HandleEventAsync(DiscordClient sender, VoiceStateUpdatedEventArgs args)
    {
        // Pre-warm Lavalink when the bot itself joins or moves channels
        if (args.UserId == sender.CurrentUser.Id)
        {
            return; // don't run VoiceChannelService logic for bot's own events
        }

        try
        {
            await _voiceService.HandleVoiceStateUpdateAsync(args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling voice state update");
        }
    }
}