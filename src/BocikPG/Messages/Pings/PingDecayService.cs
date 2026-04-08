
using BocikPG;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class PingDecayService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly PingOptions _options;
    private readonly ILogger<PingDecayService> _logger;

    public PingDecayService(
        IServiceProvider serviceProvider,
        IOptions<PingOptions> options,
        ILogger<PingDecayService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.DecayEnabled)
        {
            _logger.LogInformation("Ping decay is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromMinutes(_options.DecayIntervalMinutes);
            await Task.Delay(interval, stoppingToken);

            try
            {
                // Resolve PingHandlerService from the Discord client's DI container
                using var scope = _serviceProvider.CreateScope();
                var pingService = scope.ServiceProvider.GetRequiredService<PingHandlerService>();
                pingService.DecayPingCounts();
                _logger.LogDebug("Ping counts decayed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decay ping counts.");
            }
        }
    }
}