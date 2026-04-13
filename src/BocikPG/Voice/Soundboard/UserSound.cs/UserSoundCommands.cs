using BocikPG.Soundboard;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ArgumentModifiers;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using DSharpPlus.Entities;
using System.ComponentModel;

namespace BocikPG.UserSounds;

/// <summary>
/// Slash commands for managing per-user join/leave sound assignments.
/// </summary>
[Command("usersound")]
[RequireOwner]
[Description("Manage join/leave sounds for users.")]
public class UserSoundCommands
{
	private readonly UserSoundService _userSoundService;
	private readonly SoundboardService _soundboardService;

	public UserSoundCommands(
		UserSoundService userSoundService,
		SoundboardService soundboardService)
	{
		_userSoundService = userSoundService;
		_soundboardService = soundboardService;
	}

	// ── Set ───────────────────────────────────────────────────────────────────

	[Command("set-join")]
	[Description("Set the sound played when a user joins a voice channel.")]
	public async ValueTask SetJoinAsync(
		CommandContext ctx,
		[Description("The user to assign the sound to.")]
		DiscordUser user,
		[Description("Name of the sound to play on join.")]
		[SlashAutoCompleteProvider<SoundNameAutoCompleteProvider>]
		string soundName)
	{

		if (!SoundExists(soundName, out var notFoundMsg))
		{
			await ctx.RespondAsync(new DiscordInteractionResponseBuilder().WithContent(notFoundMsg!).AsEphemeral(true));
			return;
		}

		await _userSoundService.SetJoinSoundAsync(user.Id, soundName);
		await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
				.WithContent($"✅ Join sound for {user.Mention} set to **{soundName}**.")
				.AsEphemeral(true)
				);
	}

	[Command("set-leave")]
	[Description("Set the sound played when a user leaves a voice channel.")]
	public async ValueTask SetLeaveAsync(
		CommandContext ctx,
		[Description("The user to assign the sound to.")]
		DiscordUser user,
		[Description("Name of the sound to play on leave.")]
		[SlashAutoCompleteProvider<SoundNameAutoCompleteProvider>]
		string soundName)
	{

		if (!SoundExists(soundName, out var notFoundMsg))
		{
			await ctx.RespondAsync(new DiscordInteractionResponseBuilder().WithContent(notFoundMsg!).AsEphemeral(true));
			return;
		}

		await _userSoundService.SetLeaveSoundAsync(user.Id, soundName);
		await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
				.WithContent($"✅ Leave sound for {user.Mention} set to **{soundName}**.")
				.AsEphemeral(true)
				);
	}

	// ── Clear ─────────────────────────────────────────────────────────────────

	[Command("clear-join")]
	[Description("Remove the join sound assignment for a user (will use a random sound instead).")]
	public async ValueTask ClearJoinAsync(
		CommandContext ctx,
		[Description("The user whose join sound should be cleared.")]
		DiscordUser user)
	{
		var removed = await _userSoundService.RemoveJoinSoundAsync(user.Id);
		await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
			.WithContent(removed
			? $"✅ Join sound for {user.Mention} cleared — a random sound will be played instead."
			: $"ℹ️ {user.Mention} had no join sound assigned.")
			.AsEphemeral(true));
	}

	[Command("clear-leave")]
	[Description("Remove the leave sound assignment for a user (will use a random sound instead).")]
	public async ValueTask ClearLeaveAsync(
		CommandContext ctx,
		[Description("The user whose leave sound should be cleared.")]
		DiscordUser user)
	{
		var removed = await _userSoundService.RemoveLeaveSoundAsync(user.Id);
		await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
			.WithContent(removed
			? $"✅ Leave sound for {user.Mention} cleared — a random sound will be played instead."
			: $"ℹ️ {user.Mention} had no leave sound assigned.")
			.AsEphemeral(true));
	}

	[Command("clear-all")]
	[Description("Remove all join and leave sound assignments for a user.")]
	public async ValueTask ClearAllAsync(
		CommandContext ctx,
		[Description("The user whose sounds should be cleared.")]
		DiscordUser user)
	{
		var removedJoin = await _userSoundService.RemoveJoinSoundAsync(user.Id);
		var removedLeave = await _userSoundService.RemoveLeaveSoundAsync(user.Id);

		if (!removedJoin && !removedLeave)
		{
			await ctx.RespondAsync(new DiscordInteractionResponseBuilder().WithContent($"ℹ️ {user.Mention} had no sound assignments.").AsEphemeral(true));
			return;
		}

		var cleared = new List<string>();
		if (removedJoin) cleared.Add("join");
		if (removedLeave) cleared.Add("leave");

		await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
				.WithContent($"✅ Cleared **{string.Join(" and ", cleared)}** sound(s) for {user.Mention}.")
				.AsEphemeral(true)
				);
	}

	// ── Info ──────────────────────────────────────────────────────────────────

	[Command("info")]
	[Description("Show the join/leave sounds assigned to a user.")]
	public async ValueTask InfoAsync(
		CommandContext ctx,
		[Description("The user to look up.")]
		DiscordUser user)
	{
		var joinSound = _userSoundService.GetJoinSound(user.Id);
		var leaveSound = _userSoundService.GetLeaveSound(user.Id);

		var embed = new DiscordEmbedBuilder()
			.WithTitle($"Sound assignments for {user.Username}")
			.WithThumbnail(user.AvatarUrl)
			.WithColor(DiscordColor.Blurple)
			.AddField("🔊 Join sound", joinSound ?? "_random_", inline: true)
			.AddField("🔇 Leave sound", leaveSound ?? "_random_", inline: true)
			.Build();

		await ctx.RespondAsync(embed);
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	private bool SoundExists(string soundName, out string? errorMessage)
	{
		if (_soundboardService.GetSound(soundName) is not null)
		{
			errorMessage = null;
			return true;
		}

		errorMessage = $"❌ No sound named **{soundName}** exists. Use `/soundboard list` or check the soundboard channel.";
		return false;
	}
}