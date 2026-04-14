using BocikPG.Soundboard;
using DSharpPlus.Entities;

namespace BocikPG.SoundStats;

public static class SoundStatsEmbedBuilder
{
    /// <summary>
    /// Builds a statistics embed based on the provided filters.
    /// </summary>
    /// <param name="statsService">SoundStatsService to query data.</param>
    /// <param name="guildId">Current guild ID.</param>
    /// <param name="userId">Optional user ID filter.</param>
    /// <param name="sound">Optional sound name filter.</param>
    /// <param name="source">Optional source filter.</param>
    /// <param name="limit">Number of results for top sounds (ignored if sound is specified).</param>
    /// <param name="userMention">Optional mention string for the user (e.g., "<@123>") – used in descriptions.</param>
    /// <param name="userAvatarUrl">Optional avatar URL for the user.</param>
    /// <param name="userName">Optional display name for the user – used in titles (fallback to mention if null).</param>
    /// <returns>Tuple containing the embed, success flag, and an optional error message.</returns>
    public static (DiscordEmbed? Embed, bool Success, string? ErrorMessage) BuildStatsEmbed(
        SoundStatsService statsService,
        ulong guildId,
        ulong? userId,
        string? sound,
        string? source,
        int limit,
        string? userMention = null,
        string? userAvatarUrl = null,
        string? userName = null)
    {
        bool isSpecificSound = !string.IsNullOrEmpty(sound);
        bool isSpecificUser = userId.HasValue;
        bool isSpecificSource = !string.IsNullOrEmpty(source);

        // Use display name in title; fallback to mention if name not provided
        string displayName = userName ?? userMention ?? "this user";

        // Case: specific sound -> show total count
        if (isSpecificSound)
        {
            var count = statsService.GetPlayCount(guildId, userId, sound, source);
            var builder = new DiscordEmbedBuilder()
                .WithTitle($"🔢 Play count: {sound}")
                .WithDescription($"Played **{count}** time{(count == 1 ? "" : "s")}")
                .WithColor(DiscordColor.Green)
                .WithTimestamp(DateTimeOffset.UtcNow);

            if (isSpecificUser && userMention != null)
                builder.WithDescription($"{userMention} played **{sound}** {count} time{(count == 1 ? "" : "s")}.");
            if (isSpecificSource)
                builder.WithDescription(builder.Description + $"\nSource: **{source}**");

            return (builder.Build(), true, null);
        }

        // Case: no specific sound -> get top sounds
        var topSounds = statsService.GetTopSounds(guildId, userId, source, limit);
        if (topSounds.Count == 0)
        {
            string errorMsg = "No play statistics found";
            if (isSpecificUser && userMention != null) errorMsg += $" for {userMention}";
            if (isSpecificSource) errorMsg += $" with source **{source}**";
            return (null, false, errorMsg + ".");
        }

        // Build title – use displayName (without mention) for cleaner look
        string title = "🎵 Most Played Sounds";
        if (isSpecificUser) title = $"🔊 Most played sounds by {displayName}";
        if (isSpecificSource) title = $"🎵 Most played sounds – Source: {source}";
        if (isSpecificUser && isSpecificSource) title = $"🔊 Most played by {displayName} (source: {source})";

        var embedBuilder = new DiscordEmbedBuilder()
            .WithTitle(title)
            .WithColor(isSpecificUser ? DiscordColor.Blurple : DiscordColor.Gold)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithDescription(string.Join("\n", topSounds.Select((kvp, i) => $"{i + 1}. **{kvp.Key}** – {kvp.Value} plays")));

        if (isSpecificUser && !string.IsNullOrEmpty(userAvatarUrl))
            embedBuilder.WithThumbnail(userAvatarUrl);

        return (embedBuilder.Build(), true, null);
    }
}