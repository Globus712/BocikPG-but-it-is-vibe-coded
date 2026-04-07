using BocikPG;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Options;

public sealed class MessageCreatedHandler : IEventHandler<MessageCreatedEventArgs>
{
    private readonly KeywordService _keywordService;
    private readonly PingHandlerService _pingService;
    private readonly RandomResponseService _randomResponseService;
    private readonly bool _ttsEnabled;

    public MessageCreatedHandler(
        KeywordService keywordService,
        PingHandlerService pingService,
        RandomResponseService randomResponseService,
        IOptions<PingOptions> pingOptions)
    {
        _keywordService = keywordService;
        _pingService = pingService;
        _randomResponseService = randomResponseService;
        _ttsEnabled = pingOptions.Value.TtsEnabled;
    }

    public async Task HandleEventAsync(DiscordClient sender, MessageCreatedEventArgs args)
    {
        if (args.Author.IsCurrent) return;

        // 1. Keyword response
        var keywordResponse = _keywordService.GetResponse(args.Message.Content);
        if (keywordResponse != null)
        {
            await args.Message.RespondAsync(keywordResponse);
            return;
        }

        // 2. Random response (chance based)
        var randomResponse = _randomResponseService.GetRandomResponse(args.Author.Id);
        if (randomResponse != null)
        {
            await args.Message.RespondAsync(randomResponse);
            return;  // stop here, or allow both? Your choice.
        }

        // 3. Bot mention response
        if (args.Message.MentionedUsers?.Any(u => u.Id == sender.CurrentUser.Id) == true)
        {
            // Get the member (null if DM)
            // Get DiscordMember (null if DM or if fetch fails)
            DiscordMember? member = null;
            if (args.Guild != null)
            {
                try
                {
                    member = await args.Guild.GetMemberAsync(args.Author.Id);
                }
                catch
                {
                    // User not in guild? Shouldn't happen, but just in case.
                    member = null;
                }
            }

            var response = await _pingService.CanPingAsync(args.Author.Id, member);
            if (response != null)
            {
                var builder = new DiscordMessageBuilder()
                    .WithContent(response)
                    .WithTTS(_ttsEnabled);
                await args.Message.RespondAsync(builder);
            }
        }
    }
}