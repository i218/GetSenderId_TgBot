using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramMessage = Telegram.Bot.Types.Message;

namespace GetSenderId_TgBot;

internal sealed class Bot : IDisposable
{
    private const long PollingErrorLogIntervalMilliseconds = 30_000;

    private readonly TelegramBotClient _client;
    private readonly Action<string> _reportHealth;
    private readonly Action<string> _reportStatus;
    private CancellationTokenSource? _pollingCancellation;
    private int _pollingConflictDetected;
    private long _nextPollingErrorLogAt;

    internal Bot(string token, Action<string> reportStatus, Action<string> reportHealth)
    {
        _client = new TelegramBotClient(token);
        _reportStatus = reportStatus;
        _reportHealth = reportHealth;
    }

    internal void Start(CancellationToken cancellationToken)
    {
        _pollingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _client.StartReceiving(
            UpdateHandler,
            PollingErrorHandler,
            cancellationToken: _pollingCancellation.Token);
    }

    public void Dispose()
    {
        var pollingCancellation = Interlocked.Exchange(ref _pollingCancellation, null);
        pollingCancellation?.Cancel();
        pollingCancellation?.Dispose();
    }

    private Task PollingErrorHandler(
        ITelegramBotClient client,
        Exception exception,
        HandleErrorSource source,
        CancellationToken cancellationToken)
    {
        if (exception is ApiRequestException { ErrorCode: 409 })
        {
            if (Interlocked.Exchange(ref _pollingConflictDetected, 1) == 0)
            {
                _reportStatus("конфликт: токен занят другим экземпляром");
                _reportHealth("Conflict: another instance is polling Telegram");
            }

            // Continuing to retry getUpdates would make two instances continuously terminate
            // each other's long-poll request. Keep the process visible for diagnostics, but
            // yield Telegram polling to the other instance until this process is restarted.
            StopPolling();
            return Task.CompletedTask;
        }

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

    private void StopPolling()
    {
        var pollingCancellation = Volatile.Read(ref _pollingCancellation);
        if (pollingCancellation is null)
        {
            return;
        }

        try
        {
            pollingCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Application shutdown won the race with the polling error callback.
        }
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
