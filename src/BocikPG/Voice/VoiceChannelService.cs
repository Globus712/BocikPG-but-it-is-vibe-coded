using BocikPG;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Net.Abstractions;
using DSharpPlus.Net.Gateway;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class VoiceChannelService
{
    private readonly DiscordClient _discordClient;
    private readonly UserWeightService _weightService;
    private readonly VoiceOptions _options;
    private readonly ILogger<VoiceChannelService> _logger;

    public VoiceChannelService(
        DiscordClient discordClient,
        UserWeightService weightService,
        IOptions<VoiceOptions> options,
        ILogger<VoiceChannelService> logger)
    {
        _discordClient = discordClient;
        _weightService = weightService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleVoiceStateUpdateAsync(VoiceStateUpdatedEventArgs args)
    {
        if (!_options.AutoJoinEnabled) return;
        if (!args.GuildId.HasValue) return;
        if (args.UserId == _discordClient.CurrentUser.Id) return;

        await EvaluateAsync(args.GuildId.Value);
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
                await SendVoiceStateUpdateAsync(guildId, null);
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

        await SendVoiceStateUpdateAsync(guildId, bestChannelId);
    }

    private Task SendVoiceStateUpdateAsync(ulong guildId, ulong? channelId)
    {
        // This is exactly what VoiceNext does internally
        var payload = new
        {
            guild_id = guildId.ToString(),
            channel_id = channelId?.ToString(),
            self_mute = false,
            self_deaf = false
        };

#pragma warning disable DSP0004 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        return _discordClient.SendPayloadAsync(GatewayOpCode.VoiceStateUpdate, payload, guildId);
#pragma warning restore DSP0004 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    }
}