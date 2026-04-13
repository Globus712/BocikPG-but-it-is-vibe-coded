using BocikPG;
using BocikPG.Soundboard;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Options;

public sealed class MessageCreatedHandler : IEventHandler<MessageCreatedEventArgs>
{
    private readonly KeywordService _keywordService;
    private readonly PingHandlerService _pingService;
    private readonly RandomResponseService _randomResponseService;
    private readonly SoundboardUploadHandler _soundboardUploadHandler;
    private readonly bool _ttsEnabled;

    public MessageCreatedHandler(
        KeywordService keywordService,
        PingHandlerService pingService,
        RandomResponseService randomResponseService,
        SoundboardUploadHandler soundboardUploadHandler,
        IOptions<PingOptions> pingOptions)
    {
        _keywordService = keywordService;
        _pingService = pingService;
        _randomResponseService = randomResponseService;
        _soundboardUploadHandler = soundboardUploadHandler;
        _ttsEnabled = pingOptions.Value.TtsEnabled;
    }

    public async Task HandleEventAsync(DiscordClient sender, MessageCreatedEventArgs args)
    {
        if (args.Author.IsCurrent) return;

        // 1. Soundboard upload
        if (await _soundboardUploadHandler.TryHandleAsync(sender, args)) return;

        // 2. Keyword response
        var keywordResponse = _keywordService.GetResponse(args.Message.Content);
        if (keywordResponse != null)
        {
            _ = await args.Message.RespondAsync(keywordResponse);
            return;
        }

        // 3. Random response (chance based)
        var randomResponse = _randomResponseService.GetRandomResponse(args.Author.Id);
        if (randomResponse != null)
        {
            _ = await args.Message.RespondAsync(randomResponse);
            return;
        }

        // 4. Bot mention response
        if (args.Message.MentionedUsers?.Any(u => u.Id == sender.CurrentUser.Id) == true)
        {
            DiscordMember? member = null;
            if (args.Guild != null)
            {
                try { member = await args.Guild.GetMemberAsync(args.Author.Id); }
                catch { member = null; }
            }

            var response = await _pingService.CanPingAsync(args.Author.Id, member);
            if (response != null)
            {
                var builder = new DiscordMessageBuilder()
                    .WithContent(response)
                    .WithTTS(_ttsEnabled);
                _ = await args.Message.RespondAsync(builder);
            }
        }
    }
}