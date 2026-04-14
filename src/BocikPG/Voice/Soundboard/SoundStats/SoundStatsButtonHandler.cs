using BocikPG.Soundboard;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;

namespace BocikPG.SoundStats;

public class SoundStatsButtonHandler : IEventHandler<ComponentInteractionCreatedEventArgs>
{
	private readonly SoundStatsService _statsService;
	private readonly ILogger<SoundStatsButtonHandler> _logger;

	public SoundStatsButtonHandler(SoundStatsService statsService, ILogger<SoundStatsButtonHandler> logger)
	{
		_statsService = statsService;
		_logger = logger;
	}

	public async Task HandleEventAsync(DiscordClient sender, ComponentInteractionCreatedEventArgs args)
	{
		if (!args.Interaction.Data.CustomId.StartsWith("stats_"))
			return;

		try
		{
			// Parse: stats_guildId|userId|limit|sound|source
			var parts = args.Interaction.Data.CustomId[6..].Split('|');
			if (parts.Length != 5)
			{
				await args.Interaction.CreateResponseAsync(DiscordInteractionResponseType.UpdateMessage,
							new DiscordInteractionResponseBuilder()
							.WithContent("❌ Invalid share link.")
							.AsEphemeral(true)
					);
				return;
			}

			if (!ulong.TryParse(parts[0], out var guildId))
			{
				await args.Interaction.CreateResponseAsync(DiscordInteractionResponseType.UpdateMessage,
							new DiscordInteractionResponseBuilder()
							.WithContent("❌ Invalid guild ID.")
							.AsEphemeral(true)
					);
				return;
			}

			ulong? userId = parts[1] == "0" ? null : ulong.Parse(parts[1]);
			var limit = int.Parse(parts[2]);
			var sound = string.IsNullOrEmpty(parts[3]) ? null : parts[3];
			var source = string.IsNullOrEmpty(parts[4]) ? null : parts[4];

			string? userMention = null;
			string? userAvatar = null;
			string? userName = null;
			if (userId.HasValue)
			{
				var member = await args.Interaction.Guild.GetMemberAsync(userId.Value);
				userMention = member.Mention;
				userAvatar = member.AvatarUrl;
				userName = member.DisplayName;
			}

			var (embed, success, errorMsg) = SoundStatsEmbedBuilder.BuildStatsEmbed(
				_statsService, guildId, userId, sound, source, limit,
				userMention, userAvatar, userName);

			if (!success)
			{
				await args.Interaction.CreateResponseAsync(DiscordInteractionResponseType.UpdateMessage,
							new DiscordInteractionResponseBuilder()
							.WithContent(errorMsg ?? "No statistics found.")
							.AsEphemeral(true)
					);
				return;
			}

			// Send the public embed to the channel
			await args.Interaction.Channel.SendMessageAsync(embed!);

			await args.Interaction.CreateResponseAsync(DiscordInteractionResponseType.UpdateMessage,
							new DiscordInteractionResponseBuilder()
							.WithContent("✅ Statistics shared publicly!")
							.AsEphemeral(true)
					);
		}
		catch (Exception ex)
		{
			await args.Interaction.CreateResponseAsync(DiscordInteractionResponseType.UpdateMessage,
							new DiscordInteractionResponseBuilder()
							.WithContent("❌ An error occurred while sharing statistics.")
							.AsEphemeral(true)
					);
				return;
		}
	}
}