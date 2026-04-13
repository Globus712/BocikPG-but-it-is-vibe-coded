using Microsoft.Extensions.Logging;

namespace BocikPG.Sync;

/// <summary>
/// Calls <see cref="IReloadable.ReloadAsync"/> on every registered service after a successful pull.
/// </summary>
public class SyncReloadCoordinator
{
    private readonly IEnumerable<IReloadable> _reloadables;
    private readonly ILogger<SyncReloadCoordinator> _logger;

    public SyncReloadCoordinator(IEnumerable<IReloadable> reloadables, ILogger<SyncReloadCoordinator> logger)
    {
        _reloadables = reloadables;
        _logger = logger;
    }

    public async Task ReloadAllAsync()
    {
        foreach (var service in _reloadables)
        {
            try
            {
                await service.ReloadAsync();
                _logger.LogInformation("Reloaded {Service}", service.GetType().Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reload {Service}", service.GetType().Name);
            }
        }
    }
}
