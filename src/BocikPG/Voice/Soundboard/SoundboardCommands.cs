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
    private const int Columns           = 3;
    private const int RowsPerMessage    = 5;
    private const int ButtonsPerMessage = Columns * RowsPerMessage; // 15

    private readonly SoundboardService          _soundboardService;
    private readonly SoundboardMessageStore     _store;
    private readonly ILogger<SoundboardCommands> _logger;

    public SoundboardCommands(
        SoundboardService soundboardService,
        SoundboardMessageStore store,
        ILogger<SoundboardCommands> logger)
    {
        _soundboardService = soundboardService;
        _store             = store;
        _logger            = logger;
    }

    // ── /soundboard create ────────────────────────────────────────────────────

    [Command("create")]
    [Description("Post the soundboard in this channel.")]
    public async ValueTask CreateAsync(CommandContext ctx)
    {
        var guildId = ctx.Guild!.Id;

        if (_store.GuildHasSoundboard(guildId))
        {
            await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
                .WithContent("⚠️ This server already has a soundboard. Use `/soundboard update` to refresh it, or `/soundboard destroy` to remove it first.")
                .AsEphemeral());
            return;
        }

        var sounds = _soundboardService.GetAllSounds();

        if (sounds.Count == 0)
        {
            await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
                .WithContent("⚠️ Soundboard is empty. Add entries to the soundboard JSON file!")
                .AsEphemeral());
            return;
        }

        var pages = sounds
            .Select((sound, i) => (sound, globalIndex: i))
            .Chunk(ButtonsPerMessage)
            .ToList();

        // Respond with a status message, send pages, then delete the ack after 5 seconds
        await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
            .WithContent("🎵 Creating soundboard...")
            .AsEphemeral());

        for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            var builder = BuildPageMessage(pages[pageIndex], isFirst: pageIndex == 0, isLast: pageIndex == pages.Count - 1);
            var sent    = await ctx.Channel.SendMessageAsync(builder);
            _store.Set(guildId, pageIndex, sent);
        }

        await ctx.EditResponseAsync(new DiscordWebhookBuilder()
            .WithContent("✅ Soundboard created!"));

        _ = Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(async _ =>
        {
            try { await ctx.DeleteResponseAsync(); }
            catch { /* interaction may have already expired */ }
        });
    }

    // ── /soundboard update ────────────────────────────────────────────────────

    [Command("update")]
    [Description("Reload sounds and update the existing soundboard messages in-place.")]
    public async ValueTask UpdateAsync(CommandContext ctx)
    {
        var guildId = ctx.Guild!.Id;

        if (!_store.GuildHasSoundboard(guildId))
        {
            await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
                .WithContent("⚠️ No soundboard found for this server. Use `/soundboard create` first.")
                .AsEphemeral());
            return;
        }

        var sounds = _soundboardService.GetAllSounds();
        var pages  = sounds
            .Select((sound, i) => (sound, globalIndex: i))
            .Chunk(ButtonsPerMessage)
            .ToList();

        // Respond with a status message, then delete it after 5 seconds
        await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
            .WithContent("🔄 Updating soundboard...")
            .AsEphemeral());

        // Capture stored page count BEFORE we add new pages below
        int previousPageCount = _store.PageCount(guildId);

        for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            var pageData = pages[pageIndex];
            var existing = await _store.GetAsync(guildId, pageIndex);

            if (existing is not null)
            {
                var builder = BuildPageMessage(pageData, isFirst: pageIndex == 0, isLast: pageIndex == pages.Count - 1);
                await existing.ModifyAsync(builder);
            }
            else
            {
                // New page (soundboard grew) — send as a plain channel message
                var builder = BuildPageMessage(pageData, isFirst: pageIndex == 0, isLast: pageIndex == pages.Count - 1);
                var sent    = await ctx.Channel.SendMessageAsync(builder);
                _store.Set(guildId, pageIndex, sent);
            }
        }

        // Delete orphaned pages (soundboard shrank)
        for (int pageIndex = pages.Count; pageIndex < previousPageCount; pageIndex++)
        {
            var orphan = await _store.GetAsync(guildId, pageIndex);
            if (orphan is null) continue;
            try { await orphan.DeleteAsync(); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete orphaned soundboard message page {Page}", pageIndex);
            }
        }

        // Trim store entries for deleted pages
        if (previousPageCount > pages.Count)
            _store.TrimTo(guildId, pages.Count);

        await ctx.EditResponseAsync(new DiscordWebhookBuilder()
            .WithContent("✅ Soundboard updated!"));

        _ = Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(async _ =>
        {
            try { await ctx.DeleteResponseAsync(); }
            catch { /* interaction may have already expired */ }
        });
    }

    // ── /soundboard destroy ───────────────────────────────────────────────────

    [Command("destroy")]
    [Description("Delete all soundboard messages and clear stored data for this server.")]
    public async ValueTask DestroyAsync(CommandContext ctx)
    {
        var guildId = ctx.Guild!.Id;

        if (!_store.GuildHasSoundboard(guildId))
        {
            await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
                .WithContent("⚠️ No soundboard found for this server.")
                .AsEphemeral());
            return;
        }

        int count = _store.PageCount(guildId);
        for (int pageIndex = 0; pageIndex < count; pageIndex++)
        {
            var msg = await _store.GetAsync(guildId, pageIndex);
            if (msg is null) continue;
            try { await msg.DeleteAsync(); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete soundboard message page {Page}", pageIndex);
            }
        }

        _store.Clear(guildId);

        await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
            .WithContent("🗑️ Soundboard removed.")
            .AsEphemeral());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    internal static DiscordMessageBuilder BuildPageMessage(
        (SoundDefinition sound, int globalIndex)[] pageEntries,
        bool isFirst,
        bool isLast = false)
    {
        var builder = new DiscordMessageBuilder();

        builder.WithContent(isFirst
            ? "🎵 **Soundboard** — join a voice channel and click a button to play!"
            : " ");

        var rows = pageEntries
            .Chunk(Columns)
            .Select(rowEntries =>
            {
                var buttons = rowEntries
                    .Select(e => (DiscordComponent)new DiscordButtonComponent(
                        DiscordButtonStyle.Secondary,
                        customId: $"sound_{e.globalIndex}",
                        label: e.sound.Name.Length <= 80 ? e.sound.Name : e.sound.Name[..80],
                        emoji: ResolveEmoji(e.sound.Emoji)
                    ))
                    .ToList();

                return new DiscordActionRowComponent(buttons);
            })
            .ToList();

        foreach (var row in rows)
            builder.AddActionRowComponent(row);

        // Stop button on the last page only
        if (isLast)
            builder.AddActionRowComponent(new DiscordActionRowComponent([
                new DiscordButtonComponent(
                    DiscordButtonStyle.Danger,
                    customId: "sound_stop",
                    label: "Stop",
                    emoji: new DiscordComponentEmoji("⏹️"))
            ]));

        return builder;
    }

    internal static DiscordComponentEmoji? ResolveEmoji(string? raw)
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