using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;

namespace BocikPG.Sync;

/// <summary>
/// Handles the Keep Local / Keep Remote / Force Pull buttons.
/// </summary>
public class SyncConflictHandler : IEventHandler<ComponentInteractionCreatedEventArgs>
{
    private readonly GitSyncService _syncService;
    private readonly SyncReloadCoordinator _reloadCoordinator;
    private readonly ILogger<SyncConflictHandler> _logger;

    public SyncConflictHandler(
        GitSyncService syncService,
        SyncReloadCoordinator reloadCoordinator,
        ILogger<SyncConflictHandler> logger)
    {
        _syncService = syncService;
        _reloadCoordinator = reloadCoordinator;
        _logger = logger;
    }

    public async Task HandleEventAsync(DiscordClient sender, ComponentInteractionCreatedEventArgs args)
    {
        var customId = args.Interaction.Data.CustomId;

        if (customId.StartsWith("sync_keep_local_") || customId.StartsWith("sync_keep_remote_"))
        {
            await HandleConflictResolutionAsync(args);
            return;
        }

        if (customId == "sync_force_pull")
        {
            await HandleForcePullAsync(args);
            return;
        }
    }

    private async Task HandleForcePullAsync(ComponentInteractionCreatedEventArgs args)
    {
        await args.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);

        var result = await _syncService.ForcePullAsync();

        // Force pull replaces local files with remote — reload all services
        if (result.StartsWith("✅"))
            await _reloadCoordinator.ReloadAllAsync();

        var builder = new DiscordWebhookBuilder().WithContent(result);
        builder.ClearComponents();
        await args.Interaction.EditOriginalResponseAsync(builder);
    }

    private async Task HandleConflictResolutionAsync(ComponentInteractionCreatedEventArgs args)
    {
        var customId = args.Interaction.Data.CustomId;
        bool isLocal = customId.StartsWith("sync_keep_local_");

        var token = isLocal
            ? customId["sync_keep_local_".Length..]
            : customId["sync_keep_remote_".Length..];

        await args.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);

        var resolved = await _syncService.ResolveConflictAsync(token, keepLocal: isLocal);

        if (resolved)
        {
            // "Keep remote" changes files on disk; "Keep local" does not touch disk
            // but a reload is cheap and keeps state consistent either way.
            await _reloadCoordinator.ReloadAllAsync();
        }

        var resultText = resolved
            ? isLocal
                ? "✅ Conflict resolved — **local version** force-pushed to GitHub."
                : "✅ Conflict resolved — **remote (GitHub) version** applied locally."
            : "❌ Could not resolve conflict — token not found or already resolved.";

        var builder = new DiscordWebhookBuilder()
            .WithContent((args.Message?.Content ?? "") + $"\n\n{resultText}");
        builder.ClearComponents();

        await args.Interaction.EditOriginalResponseAsync(builder);
    }
}