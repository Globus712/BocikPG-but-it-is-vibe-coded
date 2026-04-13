using System.Globalization;
using BocikPG;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using Microsoft.Extensions.DependencyInjection;

[Command("random")]
[RequireOwner]
public sealed class RandomResponseCommands
{
    [Command("chance")]
    public static async ValueTask SetChance(
        CommandContext context,
        [Parameter("user")] DiscordUser user,
        [Parameter("chance")] string chanceStr)
    {
        var builder = new DiscordInteractionResponseBuilder().AsEphemeral();

        double chance;
        bool isPercentage = chanceStr.Trim().EndsWith('%');

        if (isPercentage)
        {
            // Remove the '%' and trim
            var numberPart = chanceStr.Trim().TrimEnd('%');
            if (!double.TryParse(numberPart, NumberStyles.Any, CultureInfo.InvariantCulture, out double percent))
            {
                _ = builder.WithContent("❌ Invalid percentage. Use something like `5%` or `0.05`.");
                await context.RespondAsync(builder);
                return;
            }
            chance = percent / 100.0;
        }
        else
        {
            if (!double.TryParse(chanceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out chance))
            {
                _ = builder.WithContent("❌ Invalid number. Use decimal like `0.05` or `5%`.");
                await context.RespondAsync(builder);
                return;
            }
        }

        // Validate range
        if (chance < 0.0 || chance > 1.0)
        {
            _ = builder.WithContent("❌ Chance must be between 0% and 100% (or 0.0 to 1.0).");
            await context.RespondAsync(builder);
            return;
        }

        var service = context.ServiceProvider.GetRequiredService<RandomResponseService>();
        service.SetUserChance(user.Id, chance);

        // Format output nicely
        string display = (chance * 100).ToString("0.#####", CultureInfo.InvariantCulture) + "%";
        _ = builder.WithContent($"✅ Chance for {user.Mention} set to {display}");
        await context.RespondAsync(builder);
    }

    [Command("add")]
    public static async ValueTask AddResponse(
        CommandContext context,
        [Parameter("user")] DiscordUser user,
        [Parameter("text")] string text,
        [Parameter("weight")] int weight = 1)
    {
        var builder = new DiscordInteractionResponseBuilder().AsEphemeral();

        var service = context.ServiceProvider.GetRequiredService<RandomResponseService>();
        service.AddUserResponse(user.Id, text, weight);

        _ = builder.WithContent($"✅ Added response for {user.Mention}: `{text}` (weight {weight})");
        await context.RespondAsync(builder);
    }

    [Command("remove")]
    public static async ValueTask RemoveResponse(
        CommandContext context,
        [Parameter("user")] DiscordUser user,
        [Parameter("index")] int index)
    {
        var builder = new DiscordInteractionResponseBuilder().AsEphemeral();
        var service = context.ServiceProvider.GetRequiredService<RandomResponseService>();

        if (service.RemoveUserResponse(user.Id, index - 1)) // 1‑based index for users
            _ = builder.WithContent($"✅ Removed response #{index} for {user.Mention}");
        else
            _ = builder.WithContent($"⚠️ Invalid index for {user.Mention}");

        await context.RespondAsync(builder);
    }

    [Command("list")]
    public static async ValueTask ListResponses(
        CommandContext context,
        [Parameter("user")] DiscordUser user)
    {
        var builder = new DiscordInteractionResponseBuilder().AsEphemeral();
        var service = context.ServiceProvider.GetRequiredService<RandomResponseService>();

        var responses = service.GetUserResponses(user.Id);
        var chance = service.GetUserChance(user.Id);

        if (responses.Count == 0)
        {
            _ = builder.WithContent($"No custom responses for {user.Mention}. Using defaults.");
            await context.RespondAsync(builder);
            return;
        }

        var list = string.Join("\n", responses.Select((r, i) => $"`{i + 1}`. {r.Text} (weight {r.Weight})"));
        var msg = $"📋 Responses for {user.Mention} (chance: {chance:P0}):\n{list}";

        if (msg.Length > 2000)
            msg = msg[..1997] + "...";

        _ = builder.WithContent(msg);
        await context.RespondAsync(builder);
    }
}