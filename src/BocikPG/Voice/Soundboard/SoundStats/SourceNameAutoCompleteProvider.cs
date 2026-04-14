using BocikPG.Soundboard;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using DSharpPlus.Entities;

namespace BocikPG.SoundStats;

/// <summary>
/// Provides autocomplete suggestions for source names based on recorded stats.
/// </summary>
public class SourceNameAutoCompleteProvider : IAutoCompleteProvider
{
	private readonly SoundStatsService _statsService;

	public SourceNameAutoCompleteProvider(SoundStatsService statsService)
	{
		_statsService = statsService;
	}

	public async ValueTask<IEnumerable<DiscordAutoCompleteChoice>> AutoCompleteAsync(
		AutoCompleteContext context)
	{
		// The guild is guaranteed because the command is [RequireGuild]
		var guild = context.Interaction.Guild;
		if (guild == null)
			return Array.Empty<DiscordAutoCompleteChoice>();

		var input = context.UserInput?.ToString() ?? string.Empty;
		var sources = _statsService.GetAvailableSources(guild.Id);

		var matches = sources
			.Where(s => s.Contains(input, StringComparison.OrdinalIgnoreCase))
			.Take(25)
			.Select(s => new DiscordAutoCompleteChoice(s, s));

		return matches;
	}
}