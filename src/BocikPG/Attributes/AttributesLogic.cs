using BocikPG;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using Microsoft.Extensions.Options;
using System.Threading.Tasks;

public class RequireOwnerCheck : IContextCheck<RequireOwnerAttribute>
{
    private readonly ulong _ownerId;

    public RequireOwnerCheck(IOptions<BotOptions> options)
    {
        _ownerId = options.Value.OwnerId;
    }

    public ValueTask<string?> ExecuteCheckAsync(RequireOwnerAttribute attribute, CommandContext context)
    {
        if (context.User.Id == _ownerId)
            return ValueTask.FromResult<string?>(null); // allowed

        return ValueTask.FromResult<string?>("⛔ This command is only available to the bot owner.");
    }
}