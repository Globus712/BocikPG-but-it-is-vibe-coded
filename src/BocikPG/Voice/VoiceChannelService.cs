using BocikPG;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Net.Abstractions;
using DSharpPlus.Net.Gateway;
using Lavalink4NET;
using Lavalink4NET.Clients;
using Lavalink4NET.Events.Players;
using Lavalink4NET.Players;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class VoiceChannelService
{
    private readonly DiscordClient _discordClient;
    private readonly UserWeightService _weightService;
    private readonly IAudioService _audioService;  // <-- add this
    private readonly VoiceOptions _options;
    private readonly ILogger<VoiceChannelService> _logger;

    public VoiceChannelService(
        DiscordClient discordClient,
        UserWeightService weightService,
        IAudioService audioService,  // <-- add parameter
        IOptions<VoiceOptions> options,
        ILogger<VoiceChannelService> logger)
    {
        _discordClient = discordClient;
        _weightService = weightService;
        _audioService = audioService;  // <-- store
        _options = options.Value;
        _logger = logger;

        _audioService.Players.PlayerDestroyed += OnPlayerDestroyedAsync;
        _ = StartWatchdogAsync(new CancellationToken());
    }

    public async Task HandleVoiceStateUpdateAsync(VoiceStateUpdatedEventArgs args)
    {
        if (!_options.AutoJoinEnabled) return;
        if (!args.GuildId.HasValue) return;
        if (args.UserId == _discordClient.CurrentUser.Id) return;

        await EvaluateAsync(args.GuildId.Value);
    }

    public async Task InitializeAllGuildsAsync()
    {
        foreach (var guild in _discordClient.Guilds.Values)
        {
            await EvaluateAsync(guild.Id);
        }
    }

    private async Task EvaluateAsync(ulong guildId)
    {
        var guild = await _discordClient.GetGuildAsync(guildId);

        ulong? bestChannelId = null;
        string? bestChannelName = null;
        double bestScore = 0;

        foreach (var channel in guild.Channels.Values)
        {
            if (channel.Type != DiscordChannelType.Voice) continue;

            double score = 0;
            foreach (var member in channel.Users)
            {
                if (member.IsBot) continue;
                score += _weightService.GetWeight(member.Id);
            }

            _logger.LogDebug("Channel {ChannelName} ({ChannelId}) score: {Score}",
                channel.Name, channel.Id, score);

            if (score > bestScore)
            {
                bestScore = score;
                bestChannelId = channel.Id;
                bestChannelName = channel.Name;
            }
        }

        var botMember = await guild.GetMemberAsync(_discordClient.CurrentUser.Id);
        var currentChannelId = botMember.VoiceState?.ChannelId;
        var currentChannel = currentChannelId.HasValue ? await guild.GetChannelAsync(currentChannelId.Value) : null;

        if (bestChannelId == null || bestScore == 0)
        {
            if (currentChannelId != null)
            {
                _logger.LogInformation(
                    "No humans in any voice channel, disconnecting from {ChannelName} ({ChannelId}).",
                    currentChannel?.Name, currentChannelId);
                var player = await _audioService.Players.GetPlayerAsync<LavalinkPlayer>(guildId);
                if (player is not null)
                {
                    await player.DisconnectAsync();
                }
            }
            else
            {
                _logger.LogDebug("No humans in any voice channel, bot is not connected — nothing to do.");
            }
            return;
        }

        if (currentChannelId == bestChannelId)
        {
            _logger.LogDebug("Already in best channel {ChannelName} ({ChannelId}), staying put.",
                bestChannelName, bestChannelId);
            return;
        }

        _logger.LogInformation("Moving to {ChannelName} ({ChannelId}) with score {Score}.",
            bestChannelName, bestChannelId, bestScore);

        await MoveToChannelAsync(guildId, bestChannelId.Value);
    }

    private async Task MoveToChannelAsync(ulong guildId, ulong channelId)
    {
        try
        {
            var result = await _audioService.Players.RetrieveAsync<LavalinkPlayer, LavalinkPlayerOptions>(
                guildId,
                channelId,
                PlayerFactory.Default,
                Microsoft.Extensions.Options.Options.Create(new LavalinkPlayerOptions()),
                new PlayerRetrieveOptions(
                    ChannelBehavior: PlayerChannelBehavior.Move,
                    VoiceStateBehavior: MemberVoiceStateBehavior.Ignore)); // ← for bot-initiated moves

            if (result.IsSuccess)
            {
                _logger.LogInformation("Moved to channel {ChannelId} using Lavalink", channelId);
            }
            else
            {
                _logger.LogWarning("Failed to move to channel {ChannelId}: {Status}", channelId, result.Status);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error moving to channel {ChannelId}", channelId);
        }
    }

    private async Task OnPlayerDestroyedAsync(object? sender, PlayerDestroyedEventArgs args)
    {
        var guildId = args.Player.GuildId;
        _logger.LogWarning("Player destroyed for guild {GuildId}, re-evaluating...", guildId);

        // Small delay so Discord has time to settle
        await Task.Delay(TimeSpan.FromSeconds(2));
        await EvaluateAsync(guildId);
    }

    // In VoiceChannelService
    public async Task StartWatchdogAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));
        while (await timer.WaitForNextTickAsync(ct))
        {
            foreach (var guild in _discordClient.Guilds.Values)
            {
                var player = await _audioService.Players
                    .GetPlayerAsync<LavalinkPlayer>(guild.Id);

                // Player is gone but bot member thinks it's connected
                var botMember = await guild.GetMemberAsync(_discordClient.CurrentUser.Id);
                if (player is null or { State: PlayerState.Destroyed }
                    && botMember.VoiceState?.ChannelId is not null)
                {
                    _logger.LogWarning(
                        "Watchdog: stale voice state in guild {GuildId}, re-evaluating", guild.Id);
                    await EvaluateAsync(guild.Id);
                }
            }
        }
    }
}
