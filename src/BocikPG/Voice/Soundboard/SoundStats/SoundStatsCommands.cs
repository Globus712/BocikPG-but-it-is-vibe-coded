using BocikPG.Soundboard;
using BocikPG.UserSounds;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ArgumentModifiers;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using DSharpPlus.Entities;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace BocikPG.SoundStats;

[Description("Show sound play statistics (most played, per user, per sound).")]
[RequireGuild]
public class SoundStatsCommands
{
	private readonly SoundStatsService _statsService;
	private readonly SoundboardService _soundboardService;

	public SoundStatsCommands(SoundStatsService statsService, SoundboardService soundboardService)
	{
		_statsService = statsService;
		_soundboardService = soundboardService;
	}

	[Command("soundstats")]
	[Description("Get sound play statistics with optional filters (user, sound, source).")]
	public async ValueTask StatsAsync(
		CommandContext ctx,
		[Description("Filter by a specific user.")] DiscordUser? user = null,
		[Description("Filter by a specific sound name.")]
		[SlashAutoCompleteProvider<SoundNameAutoCompleteProvider>] string? sound = null,
		[Description("Filter by source (e.g., Soundboard, JoinSound).")]
		[SlashAutoCompleteProvider<SourceNameAutoCompleteProvider>] string? source = null,
		[Description("Number of results to show (max 25).")][MinMaxValue(1, 25)] int limit = 10)
	{
		var guildId = ctx.Guild!.Id;
		var userId = user?.Id;
		var userMention = user?.Mention;
		var userAvatar = user?.AvatarUrl;
		var userName = user?.GlobalName;

		var (embed, success, errorMsg) = SoundStatsEmbedBuilder.BuildStatsEmbed(
			_statsService, guildId, userId, sound, source, limit, userMention, userAvatar, userName);

		if (!success)
		{
			await ctx.RespondAsync(new DiscordInteractionResponseBuilder().WithContent(errorMsg ?? "An error occurred.").AsEphemeral(true));
			return;
		}

		// After building embed, create a compact custom ID
		var customId = BuildCustomId(guildId, userId, sound, source, limit);
		var button = new DiscordButtonComponent(DiscordButtonStyle.Primary, customId, "📢 Share Publicly");
		var response = new DiscordInteractionResponseBuilder()
			.AddEmbed(embed!)
			.AddActionRowComponent(button)
			.AsEphemeral(true);

		await ctx.RespondAsync(response);
	}

	[Command("soundstatsflush")]
	[Description("Force a manual flush of sound stats to disk and Git.")]
	[RequireOwner]
	public async ValueTask FlushAsync(CommandContext ctx)
	{
		await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
	  		.WithContent("🔄 Flushing sound stats...")
	  		.AsEphemeral(true));
		await _statsService.ForceFlushAsync($"Manual flush by {ctx.User.Username}");
		await ctx.EditResponseAsync(new DiscordInteractionResponseBuilder()
			.WithContent("✅ Sound stats flushed successfully.")
			.AsEphemeral(true));
	}

	private class StatsButtonPayload
	{
		public ulong GuildId { get; set; }
		public ulong? UserId { get; set; }
		public string? Sound { get; set; }
		public string? Source { get; set; }
		public int Limit { get; set; }
	}



	private static string BuildCustomId(ulong guildId, ulong? userId, string? sound, string? source, int limit)
	{
		// Format: guild|user|limit|sound|source
		// Use "0" for null user, empty string for null sound/source
		var userStr = userId?.ToString() ?? "0";
		var soundStr = sound ?? "";
		var sourceStr = source ?? "";
		return $"stats_{guildId}|{userStr}|{limit}|{soundStr}|{sourceStr}";
	}
}