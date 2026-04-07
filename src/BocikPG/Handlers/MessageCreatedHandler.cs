using DSharpPlus;
using DSharpPlus.EventArgs;

public sealed class MessageCreatedHandler : IEventHandler<MessageCreatedEventArgs>
{
    private readonly KeywordService _keywordService;

    public MessageCreatedHandler(KeywordService keywordService)
    {
        _keywordService = keywordService;
    }

    public async Task HandleEventAsync(DiscordClient sender, MessageCreatedEventArgs args)
    {
        if (args.Author.IsCurrent) return;
        var response = _keywordService.GetResponse(args.Message.Content);
        if (response != null)
            await args.Message.RespondAsync(response);
    }
}