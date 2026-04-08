using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;

public class VoiceEventHandler : IEventHandler<VoiceStateUpdatedEventArgs>  // Changed
{
    private readonly VoiceChannelService _voiceService;
    private readonly ILogger<VoiceEventHandler> _logger;

    public VoiceEventHandler(VoiceChannelService voiceService, ILogger<VoiceEventHandler> logger)
    {
        _voiceService = voiceService;
        _logger = logger;
    }

    public async Task HandleEventAsync(DiscordClient sender, VoiceStateUpdatedEventArgs args)  // Changed
    {
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