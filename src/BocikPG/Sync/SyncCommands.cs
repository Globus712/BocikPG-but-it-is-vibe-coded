using DSharpPlus.Commands;
using DSharpPlus.Entities;
using Microsoft.Extensions.Options;
using System.ComponentModel;

namespace BocikPG.Sync;

[Command("sync")]
[Description("GitHub cloud sync commands.")]
public class SyncCommands
{
    private readonly GitSyncService _syncService;
    private readonly GitSyncOptions _options;
    private readonly SyncReloadCoordinator _reloadCoordinator;

    public SyncCommands(
        GitSyncService syncService,
        IOptions<GitSyncOptions> options,
        SyncReloadCoordinator reloadCoordinator)
    {
        _syncService = syncService;
        _options = options.Value;
        _reloadCoordinator = reloadCoordinator;
    }

    // ── /sync pull ────────────────────────────────────────────────────────────

    [Command("pull")]
    [Description("Pull latest data from GitHub and reload all services.")]
    public async ValueTask PullAsync(CommandContext ctx)
    {
        if (!_options.Enabled)
        {
            await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
                .WithContent("⚠️ Cloud sync is disabled. Set `Sync:Enabled = true` in appsettings.")
                .AsEphemeral());
            return;
        }

        await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
            .WithContent("☁️ Pulling from GitHub...")
            .AsEphemeral());

        var result = await _syncService.PullWithConflictDetectionAsync();

        if (result.Success)
            await _reloadCoordinator.ReloadAllAsync();

        var builder = new DiscordWebhookBuilder().WithContent(result.Message);

        if (result.HasConflict)
        {
            var forceButton = new DiscordButtonComponent(
                DiscordButtonStyle.Danger,
                "sync_force_pull",
                "Force Pull (Discard Local Changes)",
                emoji: new DiscordComponentEmoji("⚠️"));

            builder.AddActionRowComponent(forceButton);
        }

        await ctx.EditResponseAsync(builder);

        if (!result.HasConflict)
        {
            _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(async _ =>
            {
                try { await ctx.DeleteResponseAsync(); }
                catch { }
            });
        }
    }

    // ── /sync status ──────────────────────────────────────────────────────────

    [Command("status")]
    [Description("Show whether cloud sync is enabled and configured.")]
    public async ValueTask StatusAsync(CommandContext ctx)
    {
        var status = _options.Enabled
            ? $"✅ **Sync enabled**\n" +
              $"Branch: `{_options.Branch}`\n" +
              $"Repo path: `{_options.RepoPath}`\n" +
              $"PAT configured: {(_options.PersonalAccessToken.Length > 0 ? "yes" : "**no ⚠️**")}\n" +
              $"Owner ID: `{_options.OwnerId}`"
            : "❌ **Sync is disabled.** Set `Sync:Enabled = true` in appsettings.json to enable.";

        await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
            .WithContent(status)
            .AsEphemeral());
    }
}