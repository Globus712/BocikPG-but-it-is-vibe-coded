using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

[Command("voice")]
[RequireOwner]
public sealed class VoiceCommands
{
    [Command("setweight")]
    [System.ComponentModel.Description("Set a user's channel priority weight")]
    public static async ValueTask SetUserWeight(
        CommandContext context,
        [Parameter("user")] DiscordUser user,
        [Parameter("weight")] string weightStr)
    {
        var builder = new DiscordInteractionResponseBuilder().AsEphemeral();

        if (!double.TryParse(weightStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double weight) || weight < 0)
        {
            _ = builder.WithContent("❌ Invalid weight. Use a positive number like `1.5`.");
            await context.RespondAsync(builder);
            return;
        }

        var service = context.ServiceProvider.GetRequiredService<UserWeightService>();
        await service.SetWeightAsync(user.Id, weight);

        _ = builder.WithContent($"✅ Weight for {user.Mention} set to **{weight}**.");
        await context.RespondAsync(builder);
    }

    [Command("removeweight")]
    [System.ComponentModel.Description("Remove a user's custom weight (resets to default 1.0)")]
    public static async ValueTask RemoveUserWeight(
        CommandContext context,
        [Parameter("user")] DiscordUser user)
    {
        var service = context.ServiceProvider.GetRequiredService<UserWeightService>();
        await service.RemoveWeightAsync(user.Id);

        var builder = new DiscordInteractionResponseBuilder().AsEphemeral();
        _ = builder.WithContent($"✅ Weight for {user.Mention} reset to default (1.0).");
        await context.RespondAsync(builder);
    }

    [Command("listweights")]
    [System.ComponentModel.Description("List all custom user weights")]
    public static async ValueTask ListWeights(CommandContext context)
    {
        var service = context.ServiceProvider.GetRequiredService<UserWeightService>();
        var all = service.GetAll();

        var builder = new DiscordInteractionResponseBuilder().AsEphemeral();

        if (all.Count == 0)
        {
            _ = builder.WithContent("No custom weights set. All users default to **1.0**.");
        }
        else
        {
            var list = string.Join("\n", all.Select(kv => $"<@{kv.Key}> → **{kv.Value}**"));
            _ = builder.WithContent($"📋 User weights:\n{list}");
        }

        await context.RespondAsync(builder);
    }
}