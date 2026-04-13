using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using Microsoft.Extensions.DependencyInjection;

// Add the [Command] attribute to the class to make it a root command group
[Command("keyword")]
public sealed class KeywordCommands
{
    // Command: /keyword add <keyword> <response> [weight]
    [Command("add")]
    public static async ValueTask AddResponse(
        CommandContext context,
        [Parameter("keyword")] string keyword,
        [Parameter("response")] string response,
        [Parameter("weight")] int weight = 1)
    {
        // ... (command logic remains the same) ...
        var service = context.ServiceProvider.GetRequiredService<KeywordService>();
        service.AddResponse(keyword, response, weight);
        var builder = new DiscordInteractionResponseBuilder()
            .WithContent($"✅ Added response for `{keyword}` (weight {weight}): {response}")
            .AsEphemeral();
        await context.RespondAsync(builder);
    }

    // Command: /keyword addsilent <keyword> [weight]
    [Command("addsilent")]
    public static async ValueTask AddSilentResponse(
        CommandContext context,
        [Parameter("keyword")] string keyword,
        [Parameter("weight")] int weight = 1)
    {
        // ... (command logic remains the same) ...
        var service = context.ServiceProvider.GetRequiredService<KeywordService>();
        service.AddResponse(keyword, "", weight);
        var builder = new DiscordInteractionResponseBuilder()
            .WithContent($"✅ Added *silent* response for `{keyword}` with weight {weight}.")
            .AsEphemeral();
        await context.RespondAsync(builder);
    }

    // Command: /keyword remove <keyword> <response>
    [Command("remove")]
    public static async ValueTask RemoveResponse(
        CommandContext context,
        [Parameter("keyword")] string keyword,
        [Parameter("response")] string response)
    {
        // ... (command logic remains the same) ...
        var service = context.ServiceProvider.GetRequiredService<KeywordService>();
        var builder = new DiscordInteractionResponseBuilder().AsEphemeral();

        if (service.RemoveResponse(keyword, response))
            _ = builder.WithContent($"❌ Removed response from `{keyword}`: {response}");
        else
            _ = builder.WithContent($"⚠️ Response not found for `{keyword}`.");

        await context.RespondAsync(builder);
    }

    // Command: /keyword removesilent <keyword>
    [Command("removesilent")]
    public static async ValueTask RemoveSilentResponse(
        CommandContext context,
        [Parameter("keyword")] string keyword)
    {
        // ... (command logic remains the same) ...
        var service = context.ServiceProvider.GetRequiredService<KeywordService>();
        var builder = new DiscordInteractionResponseBuilder().AsEphemeral();

        if (service.RemoveResponse(keyword, ""))
            _ = builder.WithContent($"❌ Removed a silent response for keyword `{keyword}`.");
        else
            _ = builder.WithContent($"⚠️ No silent response found for keyword `{keyword}`.");

        await context.RespondAsync(builder);
    }

    // Command: /keyword list <keyword>
    [Command("list")]
    public static async ValueTask ListResponses(
        CommandContext context,
        [Parameter("keyword")] string keyword)
    {
        // ... (command logic remains the same) ...
        var service = context.ServiceProvider.GetRequiredService<KeywordService>();
        var responses = service.GetResponses(keyword);
        var builder = new DiscordInteractionResponseBuilder().AsEphemeral();

        if (responses.Count == 0)
        {
            _ = builder.WithContent($"No responses found for keyword `{keyword}`.");
        }
        else
        {
            var list = string.Join("\n", responses.Select((r, i) =>
                $"{i + 1}. {(string.IsNullOrWhiteSpace(r.Text) ? "(silent)" : $"\"{r.Text}\"")} (weight: {r.Weight})"));
            _ = builder.WithContent($"📋 Responses for `{keyword}`:\n{list}");
        }

        await context.RespondAsync(builder);
    }

    // Command: /keyword listall
    [Command("listall")]
    public static async ValueTask ListKeywords(CommandContext context)
    {
        // ... (command logic remains the same) ...
        var service = context.ServiceProvider.GetRequiredService<KeywordService>();
        var all = service.GetAllKeywords();
        var builder = new DiscordInteractionResponseBuilder().AsEphemeral();

        if (all.Count == 0)
        {
            _ = builder.WithContent("No keywords defined.");
        }
        else
        {
            var list = string.Join("\n", all.Select(kv => $"**{kv.Key}** ({kv.Value.Count} responses)"));
            _ = builder.WithContent($"📋 Current keywords:\n{list}");
        }

        await context.RespondAsync(builder);
    }
}