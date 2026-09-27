using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramMessage = Telegram.Bot.Types.Message;

namespace GetSenderId_TgBot;

internal sealed class Bot
{
    private const long PollingErrorLogIntervalMilliseconds = 30_000;

    private readonly TelegramBotClient _client;
    private readonly Action<string> _reportStatus;
    private long _nextPollingErrorLogAt;

    internal Bot(string token, Action<string> reportStatus)
    {
        _client = new TelegramBotClient(token);
        _reportStatus = reportStatus;
    }

    internal void Start(CancellationToken cancellationToken)
    {
        _client.StartReceiving(UpdateHandler, PollingErrorHandler, cancellationToken: cancellationToken);
    }

    private Task PollingErrorHandler(
        ITelegramBotClient client,
        Exception exception,
        HandleErrorSource source,
        CancellationToken cancellationToken)
    {
        var now = Environment.TickCount64;
        var nextLogAt = Volatile.Read(ref _nextPollingErrorLogAt);
        if (now >= nextLogAt &&
            Interlocked.CompareExchange(
                ref _nextPollingErrorLogAt,
                now + PollingErrorLogIntervalMilliseconds,
                nextLogAt) == nextLogAt)
        {
            // Exception messages may contain transport details. Expose only the type so the bot
            // token and Telegram metadata cannot accidentally reach the UI or ServerPanel.
            _reportStatus($"Ошибка Telegram polling: {exception.GetType().Name}");
        }

        return Task.CompletedTask;
    }

    private async Task UpdateHandler(
        ITelegramBotClient client,
        Update update,
        CancellationToken cancellationToken)
    {
        var message = update.Message;
        if ((update.Type is not UpdateType.Message || message?.ForwardOrigin is null) &&
            message is not { Text: "/start" })
        {
            return;
        }

        const string startText =
            "Hi! Forward a message here and the bot will return the available sender or channel ID." +
            "\r\n\r\nIts current privacy level is structural: the bot processes updates in memory and has no message storage or user analytics. " +
            "ServerPanel receives process health only — never message text, Telegram IDs, usernames or chat data. " +
            "Telegram itself still processes messages delivered through its platform." +
            "\r\n\r\nUnforwarded messages are ignored. Source code: " +
            "<a href='https://github.com/i218/GetSenderId_TgBot'>GitHub</a>.";

        if (message.Text == "/start")
        {
            await _client.SendMessage(
                message.Chat,
                startText,
                parseMode: ParseMode.Html,
                linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                cancellationToken: cancellationToken);
            return;
        }

        switch (message.ForwardOrigin?.Type)
        {
            case MessageOriginType.HiddenUser:
                await _client.SendMessage(
                    message.Chat,
                    "User has <b>disabled forwarding</b> in settings, so getting the ID is impossible.",
                    ParseMode.Html,
                    cancellationToken: cancellationToken);
                break;

            case MessageOriginType.User:
                await SendUserId(message, cancellationToken);
                break;

            case MessageOriginType.Chat:
                await SendChatId(message, cancellationToken);
                break;
        }
    }

    private async Task SendChatId(TelegramMessage message, CancellationToken cancellationToken)
    {
        if (message.ForwardFromChat is null)
        {
            return;
        }

        await _client.SendMessage(
            message.Chat,
            $"Forwarded from:\n<b>Channel Id:</b> <code>{message.ForwardFromChat.Id}</code>",
            ParseMode.Html,
            cancellationToken: cancellationToken);
    }

    private async Task SendUserId(TelegramMessage message, CancellationToken cancellationToken)
    {
        if (message.ForwardFrom is null)
        {
            return;
        }

        var usernameText = message.ForwardFrom.Username is null
            ? string.Empty
            : $"\n<b>UserTag:</b> @{message.ForwardFrom.Username}";

        await _client.SendMessage(
            message.Chat,
            $"Forwarded from:\n<b>User Id:</b> <code>{message.ForwardFrom.Id}</code>{usernameText}",
            ParseMode.Html,
            cancellationToken: cancellationToken);
    }
}
