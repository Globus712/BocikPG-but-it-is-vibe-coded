using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;

namespace BocikPG.Soundboard;

public class SoundboardBoardService
{
	private readonly SoundboardService _soundboardService;
	private readonly SoundboardMessageStore _store;
	private readonly ILogger<SoundboardBoardService> _logger;

	private const int Columns = 3;
	private const int RowsPerMessage = 5;
	public const int ButtonsPerMessage = Columns * RowsPerMessage;

	public SoundboardBoardService(
		SoundboardService soundboardService,
		SoundboardMessageStore store,
		ILogger<SoundboardBoardService> logger)
	{
		_soundboardService = soundboardService;
		_store = store;
		_logger = logger;
	}

	/// <summary>
	/// Creates soundboard messages in the given channel for the given guild.
	/// Returns false if soundboard already exists or is empty.
	/// </summary>
	public async Task<CreateResult> CreateAsync(ulong guildId, DiscordChannel channel)
	{
		if (_store.GuildHasSoundboard(guildId))
			return CreateResult.AlreadyExists;

		var sounds = _soundboardService.GetAllSounds();
		if (sounds.Count == 0)
			return CreateResult.Empty;

		var pages = BuildPages(sounds);

		for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
		{
			var sent = await channel.SendMessageAsync(
				BuildPageMessage(pages[pageIndex], isFirst: pageIndex == 0));
			_store.Set(guildId, pageIndex, sent);
		}

		var stopSent = await channel.SendMessageAsync(BuildStopMessage());
		_store.Set(guildId, pages.Count, stopSent);

		return CreateResult.Ok;
	}

	/// <summary>
	/// Updates existing soundboard messages in-place.
	/// Returns false if no soundboard exists for the guild.
	/// </summary>
	public async Task<bool> UpdateAsync(ulong guildId)
	{
		if (!_store.GuildHasSoundboard(guildId))
			return false;

		var sounds = _soundboardService.GetAllSounds();
		var pages = BuildPages(sounds);
		int previousCount = _store.PageCount(guildId);
		int totalMessages = pages.Count + 1;

		for (int pageIndex = 0; pageIndex < totalMessages; pageIndex++)
		{
			bool isStop = pageIndex == pages.Count;
			var existing = await _store.GetAsync(guildId, pageIndex);

			var builder = isStop
				? BuildStopMessage()
				: BuildPageMessage(pages[pageIndex], isFirst: pageIndex == 0);

			if (existing is not null)
			{
				var newButtons = GetButtons(builder);
				var oldButtons = GetButtons(existing);

				if (!newButtons.SetEquals(oldButtons))
					_ = await existing.ModifyAsync(builder);
			}
			else
			{
				var anchor = await _store.GetAsync(guildId, 0);
				if (anchor is null) continue;
				var sent = await anchor.Channel.SendMessageAsync(builder);
				_store.Set(guildId, pageIndex, sent);
			}
		}

		// Delete orphaned pages
		for (int pageIndex = totalMessages; pageIndex < previousCount; pageIndex++)
		{
			var orphan = await _store.GetAsync(guildId, pageIndex);
			if (orphan is null) continue;
			try { await orphan.DeleteAsync(); }
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Could not delete orphaned soundboard page {Page}", pageIndex);
			}
		}

		if (previousCount > totalMessages)
			_store.TrimTo(guildId, totalMessages);

		return true;
	}

	public async Task UpdateAllAsync()
	{
		foreach (var guildId in _store.GetAllGuildIds())
			await UpdateAsync(guildId);
	}

	/// <summary>
	/// Deletes all soundboard messages and clears store for the guild.
	/// </summary>
	public async Task<bool> DestroyAsync(ulong guildId)
	{
		if (!_store.GuildHasSoundboard(guildId))
			return false;

		int count = _store.PageCount(guildId);
		for (int pageIndex = 0; pageIndex < count; pageIndex++)
		{
			var msg = await _store.GetAsync(guildId, pageIndex);
			if (msg is null) continue;
			try { await msg.DeleteAsync(); }
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Could not delete soundboard page {Page}", pageIndex);
			}
		}

		_store.Clear(guildId);
		return true;
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	private static List<(SoundDefinition sound, int globalIndex)[]> BuildPages(
		IReadOnlyList<SoundDefinition> sounds) =>
		sounds
			.Select((sound, i) => (sound, globalIndex: i))
			.Chunk(ButtonsPerMessage)
			.ToList();

	private static HashSet<(string, string)> GetButtons(DiscordMessageBuilder builder) =>
		builder.Components
			.OfType<DiscordActionRowComponent>()
			.SelectMany(r => r.Components)
			.OfType<DiscordButtonComponent>()
			.Select(b => (b.CustomId, b.Label))
			.ToHashSet();

	private static HashSet<(string, string)> GetButtons(DiscordMessage message) =>
		message.Components
			.OfType<DiscordActionRowComponent>()
			.SelectMany(r => r.Components)
			.OfType<DiscordButtonComponent>()
			.Select(b => (b.CustomId, b.Label))
			.ToHashSet();

	public enum CreateResult { Ok, AlreadyExists, Empty }


	public static DiscordMessageBuilder BuildPageMessage(
	(SoundDefinition sound, int globalIndex)[] pageEntries,
	bool isFirst)
	{
		var builder = new DiscordMessageBuilder();

		_ = builder.WithContent(isFirst
			? "🎵 **Soundboard** — join a voice channel and click a button to play!"
			: " ");

		var rows = pageEntries
			.Chunk(Columns)
			.Select(rowEntries =>
			{
				var buttons = rowEntries
					.Select(e => (DiscordComponent)new DiscordButtonComponent(
						DiscordButtonStyle.Primary,  
						customId: $"sound_{e.globalIndex}",
						label: e.sound.Name.Length <= 80 ? e.sound.Name : e.sound.Name[..80],
						emoji: ResolveEmoji(e.sound.Emoji)
					))
					.ToList();

				return new DiscordActionRowComponent(buttons);
			})
			.ToList();

		foreach (var row in rows)
			_ = builder.AddActionRowComponent(row);

		return builder;
	}

	public static DiscordMessageBuilder BuildStopMessage() =>
		new DiscordMessageBuilder()
			.WithContent(" ")
			.AddActionRowComponent(new DiscordActionRowComponent([
				new DiscordButtonComponent(
				DiscordButtonStyle.Danger,
				customId: "sound_stop",
				label: "STOP")
			]));

	public static DiscordComponentEmoji? ResolveEmoji(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
			return null;

		if (raw.StartsWith('<') && raw.EndsWith('>'))
		{
			var inner = raw.Trim('<', '>');
			if (inner.StartsWith('a')) inner = inner[1..];
			inner = inner.TrimStart(':');
			var parts = inner.Split(':');
			if (parts.Length == 2 && ulong.TryParse(parts[1], out var eid))
				return new DiscordComponentEmoji(eid);
		}

		if (ulong.TryParse(raw, out var snowflake))
			return new DiscordComponentEmoji(snowflake);

		return new DiscordComponentEmoji(raw.Trim(':'));
	}
}