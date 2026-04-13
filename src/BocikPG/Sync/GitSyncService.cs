using System.Text;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BocikPG.Sync;

/// <summary>
/// Core service for syncing bot data files with a GitHub repository.
/// Call SyncFileAsync after any service saves a file.
/// </summary>
public class GitSyncService
{
    private readonly GitSyncOptions _options;
    private readonly DiscordClient _client;
    private readonly ILogger<GitSyncService> _logger;

    // Tracks pending conflict resolutions: token -> PendingConflict
    private readonly Dictionary<string, PendingConflict> _pendingConflicts = new();

    // Lock to serialise git operations — LibGit2Sharp Repository is not thread-safe
    private readonly SemaphoreSlim _gitLock = new(1, 1);

    public record PendingConflict(string FilePath, string? RemoteContent, string LocalContent);
    public record PullResult(bool Success, string Message, bool HasConflict);

    public GitSyncService(
        IOptions<GitSyncOptions> options,
        DiscordClient client,
        ILogger<GitSyncService> logger)
    {
        _options = options.Value;
        _client = client;
        _logger = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task SyncFileAsync(string absoluteFilePath, string commitMessage)
    {
        if (!_options.Enabled) return;

        await Task.Run(async () =>
        {
            await _gitLock.WaitAsync();
            try
            {
                using var repo = new Repository(_options.RepoPath);

                var relativePath = Path.GetRelativePath(_options.RepoPath, absoluteFilePath)
                    .Replace('\\', '/');

                Commands.Stage(repo, relativePath);

                var status = repo.RetrieveStatus(relativePath);
                if (status == FileStatus.Unaltered)
                {
                    _logger.LogDebug("No changes to sync for {File}", relativePath);
                    return;
                }

                var sig = new Signature(_options.AuthorName, _options.AuthorEmail, DateTimeOffset.Now);
                repo.Commit(commitMessage, sig, sig, new CommitOptions { AllowEmptyCommit = false });

                var pushed = TryPush(repo);
                if (!pushed)
                {
                    _logger.LogWarning("Push rejected for {File} — conflict detected, fetching remote.", relativePath);
                    await HandleConflictAsync(repo, relativePath, absoluteFilePath);
                }
            }
            catch (EmptyCommitException)
            {
                _logger.LogDebug("Nothing new to commit for {File}", absoluteFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in Git sync for {File}", absoluteFilePath);
            }
            finally
            {
                _gitLock.Release();
            }
        });
    }

    public Task<string> PullAsync()
    {
        return Task.Run(async () =>
        {
            await _gitLock.WaitAsync();
            try
            {
                using var repo = new Repository(_options.RepoPath);
                var sig = new Signature(_options.AuthorName, _options.AuthorEmail, DateTimeOffset.Now);
                var result = Commands.Pull(repo, sig, new PullOptions
                {
                    FetchOptions = MakeFetchOptions(),
                    MergeOptions = new MergeOptions { FastForwardStrategy = FastForwardStrategy.Default }
                });

                return result.Status switch
                {
                    MergeStatus.UpToDate    => "✅ Already up to date — no changes pulled.",
                    MergeStatus.FastForward => $"✅ Pulled successfully. HEAD is now `{result.Commit?.Sha[..7]}`.",
                    MergeStatus.Conflicts   => "⚠️ Pull completed but there are merge conflicts. Manual intervention needed.",
                    _                       => $"ℹ️ Pull result: {result.Status}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Git pull failed");
                return $"❌ Pull failed: {ex.Message}";
            }
            finally
            {
                _gitLock.Release();
            }
        });
    }

    public Task<PullResult> PullWithConflictDetectionAsync()
    {
        return Task.Run(async () =>
        {
            await _gitLock.WaitAsync();
            try
            {
                using var repo = new Repository(_options.RepoPath);
                var sig = new Signature(_options.AuthorName, _options.AuthorEmail, DateTimeOffset.Now);

                MergeResult result;
                try
                {
                    result = Commands.Pull(repo, sig, new PullOptions
                    {
                        FetchOptions = MakeFetchOptions(),
                        MergeOptions = new MergeOptions { FastForwardStrategy = FastForwardStrategy.Default }
                    });
                }
                catch (CheckoutConflictException ex)
                {
                    // Local files have uncommitted modifications that would be overwritten
                    // by the incoming remote changes. Git refuses to proceed.
                    _logger.LogWarning("Checkout conflict during pull: {Message}", ex.Message);

                    // CheckoutConflictException doesn't expose a file list — inspect the
                    // working directory status to find every modified/new file that isn't
                    // yet committed. These are exactly the files git was unable to update.
                    var conflictingPaths = repo.RetrieveStatus(new StatusOptions
                        {
                            IncludeUntracked = false,
                            RecurseUntrackedDirs = false
                        })
                        .Where(e => e.State.HasFlag(FileStatus.ModifiedInWorkdir)
                                 || e.State.HasFlag(FileStatus.NewInWorkdir))
                        .Select(e => e.FilePath)
                        .ToList();

                    // Fallback: if status inspection found nothing (unlikely but defensive),
                    // extract paths from the exception message — they appear one per line
                    // after the first line in the format libgit2 uses.
                    if (conflictingPaths.Count == 0)
                    {
                        conflictingPaths = ex.Message
                            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                            .Skip(1)
                            .Select(l => l.Trim())
                            .Where(l => l.Length > 0)
                            .ToList();
                    }

                    _logger.LogInformation("Conflicting paths: {Paths}", string.Join(", ", conflictingPaths));

                    // Release the lock before the async DM work so we don't hold it
                    // while awaiting Discord API calls (which can be slow).
                    _gitLock.Release();
                    await HandleCheckoutConflictsAsync(repo, conflictingPaths);
                    // Re-acquire so the finally block's Release() is balanced.
                    await _gitLock.WaitAsync();

                    return new PullResult(false,
                        $"⚠️ Pull blocked — {conflictingPaths.Count} local file(s) have uncommitted changes that conflict with remote. Check your DMs to resolve.",
                        HasConflict: true);
                }

                return result.Status switch
                {
                    MergeStatus.UpToDate    => new PullResult(true,  "✅ Already up to date — no changes pulled.", false),
                    MergeStatus.FastForward => new PullResult(true,  $"✅ Pulled successfully. HEAD is now `{result.Commit?.Sha[..7]}`.", false),
                    MergeStatus.Conflicts   => new PullResult(false, "⚠️ Pull resulted in merge conflicts. Choose 'Force Pull' to overwrite local changes with remote.", true),
                    _                       => new PullResult(false, $"ℹ️ Pull result: {result.Status}", false)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Git pull failed");
                return new PullResult(false, $"❌ Pull failed: {ex.Message}", false);
            }
            finally
            {
                _gitLock.Release();
            }
        });
    }

    public Task<string> ForcePullAsync()
    {
        return Task.Run(async () =>
        {
            await _gitLock.WaitAsync();
            try
            {
                using var repo = new Repository(_options.RepoPath);

                var remote = repo.Network.Remotes["origin"];
                Commands.Fetch(repo, remote.Name,
                    new[] { $"+refs/heads/{_options.Branch}:refs/remotes/origin/{_options.Branch}" },
                    MakeFetchOptions(), null);

                var remoteBranch = repo.Branches[$"origin/{_options.Branch}"];
                if (remoteBranch is null)
                    return "❌ Remote branch not found. Ensure the repository is cloned and the remote exists.";

                repo.Reset(ResetMode.Hard, remoteBranch.Tip);
                _logger.LogInformation("Force pull completed. Local branch reset to {Sha}", remoteBranch.Tip.Sha[..7]);
                return $"✅ Force pull successful. HEAD is now `{remoteBranch.Tip.Sha[..7]}`.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Force pull failed");
                return $"❌ Force pull failed: {ex.Message}";
            }
            finally
            {
                _gitLock.Release();
            }
        });
    }

    public async Task<bool> ResolveConflictAsync(string token, bool keepLocal)
    {
        if (!_pendingConflicts.TryGetValue(token, out var conflict))
        {
            _logger.LogWarning("Conflict token {Token} not found.", token);
            return false;
        }

        await _gitLock.WaitAsync();
        try
        {
            var absolutePath = Path.Combine(_options.RepoPath, conflict.FilePath);
            using var repo = new Repository(_options.RepoPath);
            var relativePath = conflict.FilePath.Replace('\\', '/');
            var sig = new Signature(_options.AuthorName, _options.AuthorEmail, DateTimeOffset.Now);

            if (keepLocal)
            {
                // In the checkout-conflict case the local change is NOT yet committed,
                // so we must stage + commit before force-pushing.
                // In the push-rejection case it's already committed — TryCommit will
                // get an EmptyCommitException, which it swallows, and we just force-push.
                Commands.Stage(repo, relativePath);
                TryCommit(repo, $"conflict: keep local version of {Path.GetFileName(conflict.FilePath)}", sig);
                TryPush(repo, force: true);
                _logger.LogInformation("Kept local version of {File} — committed and force-pushed", conflict.FilePath);
            }
            else
            {
                var remoteBranch = repo.Branches[$"origin/{_options.Branch}"];
                if (remoteBranch is null)
                {
                    _logger.LogError("Cannot accept remote: origin/{Branch} not found", _options.Branch);
                    return false;
                }

                repo.Reset(ResetMode.Hard, remoteBranch.Tip);

                if (conflict.RemoteContent is null)
                {
                    if (File.Exists(absolutePath))
                        File.Delete(absolutePath);

                    _logger.LogInformation("Accepted remote deletion of {File} — reset to remote tip", conflict.FilePath);
                }
                else
                {
                    await File.WriteAllTextAsync(absolutePath, conflict.RemoteContent);
                    Commands.Stage(repo, relativePath);
                    TryCommit(repo, $"conflict: accept remote version of {Path.GetFileName(conflict.FilePath)}", sig);
                    TryPush(repo);
                    _logger.LogInformation("Accepted remote version of {File} and pushed", conflict.FilePath);
                }
            }

            _pendingConflicts.Remove(token);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve conflict for token {Token}", token);
            return false;
        }
        finally
        {
            _gitLock.Release();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void TryCommit(Repository repo, string message, Signature sig)
    {
        try
        {
            repo.Commit(message, sig, sig);
        }
        catch (EmptyCommitException)
        {
            _logger.LogInformation("Nothing to commit — file already in desired state.");
        }
    }

    private bool TryPush(Repository repo, bool force = false)
    {
        try
        {
            var branch = repo.Branches[_options.Branch];
            var pushOptions = new PushOptions
            {
                CredentialsProvider = MakeCredentials(),
                OnPushStatusError = e => _logger.LogError("Push status error: {Msg}", e.Message)
            };

            if (force)
            {
                var remote = repo.Network.Remotes["origin"];
                repo.Network.Push(remote, $"+{branch.CanonicalName}:{branch.CanonicalName}", pushOptions);
            }
            else
            {
                repo.Network.Push(branch, pushOptions);
            }

            _logger.LogInformation("Push successful (force={Force})", force);
            return true;
        }
        catch (NonFastForwardException) when (!force)
        {
            _logger.LogWarning("Push rejected — non-fast-forward. Conflict handling required.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Push failed unexpectedly");
            return false;
        }
    }

    /// <summary>
    /// Called after a push rejection. The local file is already committed;
    /// we just need to fetch the remote version and surface the conflict via DM.
    /// </summary>
    private async Task HandleConflictAsync(Repository repo, string relativePath, string absoluteFilePath)
    {
        _logger.LogInformation("Handling push-rejection conflict for {FilePath}", relativePath);

        var remote = repo.Network.Remotes["origin"];
        Commands.Fetch(repo, remote.Name,
            new[] { $"+refs/heads/{_options.Branch}:refs/remotes/origin/{_options.Branch}" },
            MakeFetchOptions(), null);

        var remoteRef = repo.Branches[$"origin/{_options.Branch}"];
        var remoteCommit = remoteRef?.Tip;
        if (remoteCommit is null)
        {
            _logger.LogWarning("Could not resolve remote tip for branch origin/{Branch}", _options.Branch);
            return;
        }

        string? remoteContent = null;
        var remoteTreeEntry = remoteCommit[relativePath];
        if (remoteTreeEntry?.Target is Blob remoteBlob)
            remoteContent = remoteBlob.GetContentText();

        string localContent;
        try
        {
            localContent = await File.ReadAllTextAsync(absoluteFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read local file {Path}", absoluteFilePath);
            return;
        }

        await StorePendingConflictAndNotifyAsync(relativePath, remoteContent, localContent);
    }

    /// <summary>
    /// Called when a pull is blocked by a <see cref="CheckoutConflictException"/>.
    /// The local files are modified but not yet committed. For each conflicting file,
    /// reads the local disk version and the incoming remote version, then DMs buttons.
    /// </summary>
    private async Task HandleCheckoutConflictsAsync(Repository repo, IReadOnlyList<string> conflictingPaths)
    {
        var remote = repo.Network.Remotes["origin"];
        Commands.Fetch(repo, remote.Name,
            new[] { $"+refs/heads/{_options.Branch}:refs/remotes/origin/{_options.Branch}" },
            MakeFetchOptions(), null);

        var remoteRef = repo.Branches[$"origin/{_options.Branch}"];
        var remoteCommit = remoteRef?.Tip;
        if (remoteCommit is null)
        {
            _logger.LogWarning("Could not resolve remote tip for branch origin/{Branch}", _options.Branch);
            return;
        }

        foreach (var relativePath in conflictingPaths)
        {
            var absolutePath = Path.Combine(_options.RepoPath, relativePath);

            string? remoteContent = null;
            var remoteTreeEntry = remoteCommit[relativePath];
            if (remoteTreeEntry?.Target is Blob remoteBlob)
                remoteContent = remoteBlob.GetContentText();

            string localContent;
            try
            {
                localContent = await File.ReadAllTextAsync(absolutePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read local conflicting file {Path}", absolutePath);
                continue;
            }

            await StorePendingConflictAndNotifyAsync(relativePath, remoteContent, localContent);
        }
    }

    /// <summary>
    /// Stores a <see cref="PendingConflict"/> and sends the owner a DM with resolution buttons.
    /// Shared by both conflict-detection paths.
    /// </summary>
    private async Task StorePendingConflictAndNotifyAsync(
        string relativePath,
        string? remoteContent,
        string localContent)
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        _pendingConflicts[token] = new PendingConflict(relativePath, remoteContent, localContent);
        _logger.LogInformation("Conflict token {Token} stored for {File}", token, relativePath);

        if (_options.OwnerId == 0)
        {
            _logger.LogWarning("OwnerId not configured — cannot send conflict DM.");
            return;
        }

        try
        {
            var owner = await _client.GetUserAsync(_options.OwnerId, true);
            if (owner is null)
            {
                _logger.LogError("Could not fetch owner user with ID {OwnerId}", _options.OwnerId);
                return;
            }

            var dm = await owner.CreateDmChannelAsync();
            if (dm is null)
            {
                _logger.LogError("Could not create DM channel with owner {OwnerId}", _options.OwnerId);
                return;
            }

            _logger.LogInformation("Sending conflict DM to {Username}", owner.Username);
            var fileName = Path.GetFileName(relativePath);

            // Message 1: Context
            await dm.SendMessageAsync(new DiscordMessageBuilder()
                .WithContent(
                    $"⚠️ **Sync conflict detected!**\n" +
                    $"File: `{relativePath}`\n\n" +
                    $"Choose how to resolve the conflict using the buttons below.\n" +
                    $"*(Token: `{token}`)*"));

            // Message 2: Local file
            using (var localStream = new MemoryStream(Encoding.UTF8.GetBytes(localContent)))
            {
                await dm.SendMessageAsync(new DiscordMessageBuilder()
                    .WithContent($"📁 **Local version** of `{fileName}`:")
                    .AddFile($"LOCAL_{fileName}", localStream));
            }

            // Message 3: Remote file (or deletion notice)
            if (remoteContent is not null)
            {
                using var remoteStream = new MemoryStream(Encoding.UTF8.GetBytes(remoteContent));
                await dm.SendMessageAsync(new DiscordMessageBuilder()
                    .WithContent($"☁️ **Remote version** of `{fileName}` (from GitHub):")
                    .AddFile($"REMOTE_{fileName}", remoteStream));
            }
            else
            {
                await dm.SendMessageAsync(new DiscordMessageBuilder()
                    .WithContent($"☁️ **Remote file `{fileName}` does not exist on GitHub.**"));
            }

            // Message 4: Action buttons
            var buttons = new List<DiscordButtonComponent>
            {
                new(DiscordButtonStyle.Primary,
                    $"sync_keep_local_{token}",
                    "Keep Local",
                    emoji: new DiscordComponentEmoji("💾")),
                new(remoteContent is not null
                        ? DiscordButtonStyle.Secondary
                        : DiscordButtonStyle.Danger,
                    $"sync_keep_remote_{token}",
                    remoteContent is not null ? "Keep Remote (GitHub)" : "Delete Local File",
                    emoji: new DiscordComponentEmoji(remoteContent is not null ? "☁️" : "🗑️"))
            };

            await dm.SendMessageAsync(new DiscordMessageBuilder()
                .WithContent("🔽 **Choose an action:**")
                .AddActionRowComponent(buttons.ToArray()));

            _logger.LogInformation("Conflict DM sent successfully for {File}", relativePath);
        }
        catch (DiscordException dex)
        {
            _logger.LogError(dex, "Discord API error sending conflict DM: {ErrorMessage}", dex.JsonMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send conflict DM: {Message}", ex.Message);
        }
    }

    private FetchOptions MakeFetchOptions() => new()
    {
        CredentialsProvider = MakeCredentials()
    };

    private CredentialsHandler MakeCredentials() =>
        (_, _, _) => new UsernamePasswordCredentials
        {
            Username = "x-access-token",
            Password = _options.PersonalAccessToken
        };
}