using BocikPG;
using DSharpPlus.Commands;
using DSharpPlus.Entities;
using Lavalink4NET;
using Lavalink4NET.Players.Queued;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.ComponentModel;

namespace BocikPG.Soundboard;

[Command("soundboard")]
[Description("Soundboard commands.")]
public class SoundboardCommands
{
    private const int Columns = 3;
    private const int RowsPerMessage = 5;
    private const int ButtonsPerMessage = Columns * RowsPerMessage; // 15

    private readonly SoundboardService _soundboardService;
    private readonly SoundboardMessageStore _store;
    private readonly SoundboardBoardService _boardService;
    private readonly ILogger<SoundboardCommands> _logger;

    public SoundboardCommands(
        SoundboardBoardService boardService,
        ILogger<SoundboardCommands> logger,
        SoundboardService soundboardService)
    {
        _soundboardService = soundboardService;
        _boardService = boardService;
        _logger = logger;
    }

    // ── /soundboard create ────────────────────────────────────────────────────

    [Command("create")]
    public async ValueTask CreateAsync(CommandContext ctx)
    {
        var result = await _boardService.CreateAsync(ctx.Guild!.Id, ctx.Channel);

        var message = result switch
        {
            SoundboardBoardService.CreateResult.AlreadyExists => "⚠️ This server already has a soundboard. Use `/soundboard update` to refresh it, or `/soundboard destroy` to remove it first.",
            SoundboardBoardService.CreateResult.Empty => "⚠️ Soundboard is empty. Add entries to the soundboard JSON file!",
            _ => "🎵 Creating soundboard..."
        };



        await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
            .WithContent(message)
            .AsEphemeral());

        if (result != SoundboardBoardService.CreateResult.Ok) return;

        _ = await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("✅ Soundboard created!"));
        _ = Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(async _ =>
        {
            try { await ctx.DeleteResponseAsync(); }
            catch { }
        });
    }

    [Command("update")]
    public async ValueTask UpdateAsync(CommandContext ctx)
    {
        await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
            .WithContent("🔄 Updating soundboard...")
            .AsEphemeral());

        var ok = await _boardService.UpdateAsync(ctx.Guild!.Id);
        if (!ok)
        {
            _ = await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                .WithContent("⚠️ No soundboard found for this server. Use `/soundboard create` first."));
            return;
        }

        _ = await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("✅ Soundboard updated!"));
        _ = Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(async _ =>
        {
            try { await ctx.DeleteResponseAsync(); }
            catch { }
        });
    }

    [Command("destroy")]
    public async ValueTask DestroyAsync(CommandContext ctx)
    {
        var ok = await _boardService.DestroyAsync(ctx.Guild!.Id);

        await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
            .WithContent(ok ? "🗑️ Soundboard removed." : "⚠️ No soundboard found for this server.")
            .AsEphemeral());
    }

    [Command("emotes")]
    [Description("Lists all emotes used in the soundboard.")]
    public async ValueTask EmotesAsync(CommandContext ctx)
    {
        await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
            .WithContent("📋 Fetching soundboard emotes...")
            .AsEphemeral());

        var sounds = _soundboardService.GetAllSounds();
        if (sounds.Count == 0)
        {
            _ = await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                .WithContent("⚠️ Soundboard is empty."));
            return;
        }

        foreach (var (sound, i) in sounds.Select((s, i) => (s, i)))
        {
            DiscordComponentEmoji? emoji = null;
            if (ulong.TryParse(sound.Emoji, out var emojiId))
                emoji = new DiscordComponentEmoji(emojiId);
            else if (!string.IsNullOrWhiteSpace(sound.Emoji))
                emoji = new DiscordComponentEmoji(DiscordEmoji.FromUnicode(sound.Emoji));

            var button = new DiscordButtonComponent(
                DiscordButtonStyle.Primary,
                customId: $"sound_{i}",
                label: sound.Name,
                emoji: emoji);

            var builder = new DiscordMessageBuilder()
                .WithContent(" ")
                .AddActionRowComponent(new DiscordActionRowComponent([button]));

            try
            {
                await ctx.Channel.SendMessageAsync(builder);
            }
            catch
            {
                await ctx.Channel.SendMessageAsync(sound.Name);
            }
        }

        _ = await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("✅ Done!"));
    }

    private static IEnumerable<string> ChunkString(string text, int maxLength)
    {
        for (int i = 0; i < text.Length; i += maxLength)
            yield return text.Substring(i, Math.Min(maxLength, text.Length - i));
    }

}