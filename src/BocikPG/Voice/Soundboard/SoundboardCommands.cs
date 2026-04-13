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
    private readonly ILogger<SoundboardCommands> _logger;

    public SoundboardCommands(
        SoundboardService soundboardService,
        SoundboardMessageStore store,
        ILogger<SoundboardCommands> logger)
    {
        _soundboardService = soundboardService;
        _store = store;
        _logger = logger;
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
            var sent = await ctx.Channel.SendMessageAsync(builder);
            _store.Set(guildId, pageIndex, sent);
        }

        // Stop button as its own message
        var stopSent = await ctx.Channel.SendMessageAsync(BuildStopMessage());
        _store.Set(guildId, pages.Count, stopSent);

        _ = await ctx.EditResponseAsync(new DiscordWebhookBuilder()
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
        var pages = sounds
            .Select((sound, i) => (sound, globalIndex: i))
            .Chunk(ButtonsPerMessage)
            .ToList();

        // Respond with a status message, then delete it after 5 seconds
        await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
            .WithContent("🔄 Updating soundboard...")
            .AsEphemeral());

        // Capture stored page count BEFORE we add new pages below
        int previousPageCount = _store.PageCount(guildId);
        int totalMessages = pages.Count + 1; // +1 for stop message

        for (int pageIndex = 0; pageIndex < totalMessages; pageIndex++)
        {
            bool isStop = pageIndex == pages.Count;
            var existing = await _store.GetAsync(guildId, pageIndex);
            var builder = isStop ? BuildStopMessage() : BuildPageMessage(pages[pageIndex], isFirst: pageIndex == 0);

            if (existing is not null)
            {
                // diff check — skip stop message since it never changes
                if (isStop) continue;

                var newButtons = builder.Components
                    .OfType<DiscordActionRowComponent>()
                    .SelectMany(r => r.Components)
                    .OfType<DiscordButtonComponent>()
                    .Select(b => (b.CustomId, b.Label))
                    .ToHashSet();

                var existingButtons = existing.Components
                    .OfType<DiscordActionRowComponent>()
                    .SelectMany(r => r.Components)
                    .OfType<DiscordButtonComponent>()
                    .Select(b => (b.CustomId, b.Label))
                    .ToHashSet();

                if (!newButtons.SetEquals(existingButtons))
                    _ = await existing.ModifyAsync(builder);
            }
            else
            {
                var sent = await ctx.Channel.SendMessageAsync(builder);
                _store.Set(guildId, pageIndex, sent);
            }
        }

        // Delete orphaned pages (soundboard shrank) — stop message is always kept
        for (int pageIndex = totalMessages; pageIndex < previousPageCount; pageIndex++)
        {
            var orphan = await _store.GetAsync(guildId, pageIndex);
            if (orphan is null) continue;
            try { await orphan.DeleteAsync(); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete orphaned soundboard message page {Page}", pageIndex);
            }
        }

        if (previousPageCount > totalMessages)
            _store.TrimTo(guildId, totalMessages);

        // Trim store entries for deleted pages
        if (previousPageCount > pages.Count)
            _store.TrimTo(guildId, pages.Count);

        _ = await ctx.EditResponseAsync(new DiscordWebhookBuilder()
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

        _ = builder.WithContent(isFirst
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
            _ = builder.AddActionRowComponent(row);



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

    internal static DiscordMessageBuilder BuildStopMessage() =>
    new DiscordMessageBuilder()
        .WithContent(" ")
        .AddActionRowComponent(new DiscordActionRowComponent([
            new DiscordButtonComponent(
                DiscordButtonStyle.Danger,
                customId: "sound_stop",
                label: "STOP")
        ]));
}