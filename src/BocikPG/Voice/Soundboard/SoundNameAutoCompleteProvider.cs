using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using DSharpPlus.Entities;

namespace BocikPG.UserSounds;

/// <summary>
/// Provides autocomplete suggestions for sound name arguments.
/// Filters by the current input and returns up to 25 matches
/// (Discord's autocomplete limit).
/// </summary>
public class SoundNameAutoCompleteProvider : IAutoCompleteProvider
{
    private readonly SoundboardService _soundboardService;

    public SoundNameAutoCompleteProvider(SoundboardService soundboardService)
    {
        _soundboardService = soundboardService;
    }

    public ValueTask<IEnumerable<DiscordAutoCompleteChoice>> AutoCompleteAsync(
        AutoCompleteContext context)
    {
        var input = context.UserInput?.ToString() ?? string.Empty;

        var matches = _soundboardService
            .GetAllSounds()
            .Where(s => s.Name.Contains(input, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(s => new DiscordAutoCompleteChoice(s.Name, s.Name));

        return ValueTask.FromResult(matches);
    }

}
