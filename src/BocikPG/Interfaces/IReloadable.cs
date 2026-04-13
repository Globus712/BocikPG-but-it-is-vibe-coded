namespace BocikPG;

/// <summary>
/// Implemented by any singleton service that owns a synced data file.
/// Called by <see cref="Sync.SyncReloadCoordinator"/> after a successful pull.
/// </summary>
public interface IReloadable
{
    Task ReloadAsync();
}