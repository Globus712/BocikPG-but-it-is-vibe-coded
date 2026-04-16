using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using Microsoft.Extensions.DependencyInjection;

[Command("pings")]
public sealed class PingCommands
{
    [Command("add")]
    public static async ValueTask AddPersonalizedResponse(
        CommandContext context,
        [Parameter("user")] DiscordUser user,
        [Parameter("response")] string response)
    {
        var service = context.ServiceProvider.GetRequiredService<PingHandlerService>();
        service.SetPersonalizedResponse(user.Id, response);
        
        var builder = new DiscordInteractionResponseBuilder()
            .WithContent($"✅ Personalized response set for {user.Mention}: {response}")
            .AsEphemeral();
        await context.RespondAsync(builder);
    }

    [Command("remove")]
    public static async ValueTask RemovePersonalizedResponse(
        CommandContext context,
        [Parameter("user")] DiscordUser user)
    {
        var service = context.ServiceProvider.GetRequiredService<PingHandlerService>();
        var builder = new DiscordInteractionResponseBuilder().AsEphemeral();
        
        if (service.RemovePersonalizedResponse(user.Id))
            _ = builder.WithContent($"❌ Removed personalized response for {user.Mention}.");
        else
            _ = builder.WithContent($"⚠️ No personalized response found for {user.Mention}.");
        
        await context.RespondAsync(builder);
    }

    [Command("list")]
    public static async ValueTask ListPersonalizedResponses(CommandContext context)
    {
        var builder = new DiscordInteractionResponseBuilder().AsEphemeral();

        var service = context.ServiceProvider.GetRequiredService<PingHandlerService>();
        var allResponses = service.GetAllPersonalizedResponses();
        
        if (allResponses.Count == 0)
        {
            _ = builder.WithContent("No personalized responses have been set.");
        }
        else
        {
            var list = string.Join("\n", allResponses.Select(kv => 
                $"<@{kv.Key}> → {kv.Value}"));
            // Truncate if too long (Discord limit 2000 characters)
            if (list.Length > 1900)
                list = list.Substring(0, 1900) + "\n... (truncated)";
            _ = builder.WithContent($"📋 Personalized responses:\n{list}");
        }
        
        await context.RespondAsync(builder);
    }
}