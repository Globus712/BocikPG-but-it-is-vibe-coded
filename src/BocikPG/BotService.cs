using DSharpPlus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class BotService : IHostedService
{
    private readonly DiscordClient _discordClient;
    private readonly ILogger<BotService> _logger;

    public BotService(DiscordClient discordClient, ILogger<BotService> logger)
    {
        _discordClient = discordClient;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Discord client...");
        await _discordClient.ConnectAsync();
        _logger.LogInformation("Discord client connected.");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Discord client...");
        await _discordClient.DisconnectAsync();
        _logger.LogInformation("Discord client disconnected.");
    }
}
