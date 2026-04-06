using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using Microsoft.Extensions.DependencyInjection;

public sealed class KeywordCommands
{
    [Command("addkeyword")]
    public static async ValueTask AddKeyword(
        CommandContext context,
        [Parameter("keyword")] string keyword,
        [Parameter("response")] string response)
    {
        var service = context.ServiceProvider.GetRequiredService<KeywordService>();
        service.AddKeyword(keyword, response);
        await context.RespondAsync($"✅ Keyword `{keyword}` added with response: {response}");
    }

    [Command("removekeyword")]
    public static async ValueTask RemoveKeyword(
        CommandContext context,
        [Parameter("keyword")] string keyword)
    {
        var service = context.ServiceProvider.GetRequiredService<KeywordService>();
        if (service.RemoveKeyword(keyword))
            await context.RespondAsync($"❌ Keyword `{keyword}` removed.");
        else
            await context.RespondAsync($"⚠️ Keyword `{keyword}` not found.");
    }

    [Command("listkeywords")]
    public static async ValueTask ListKeywords(CommandContext context)
    {
        var service = context.ServiceProvider.GetRequiredService<KeywordService>();
        var all = service.GetAllKeywords();
        if (all.Count == 0)
        {
            await context.RespondAsync("No keywords defined.");
            return;
        }
        var list = string.Join("\n", all.Select(kv => $"**{kv.Key}** → {kv.Value}"));
        await context.RespondAsync($"📋 Current keywords:\n{list}");
    }
}