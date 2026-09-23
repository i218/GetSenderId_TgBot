using System.Diagnostics;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace GetSenderId_TgBot;

internal static class Program
{
    private const int RestartRequestedExitCode = 23;
    private static readonly CancellationTokenSource Shutdown = new();
    private static int _exitCode;

    private static async Task<int> Main()
    {
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            Shutdown.Cancel();
        };

        var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine("TELEGRAM_BOT_TOKEN is not set.");
            return 1;
        }

        var bot = new Bot(token);
        bot.Start(Shutdown.Token);

        using var panelClient = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:5088"),
            Timeout = TimeSpan.FromSeconds(3)
        };
        using var reporter = new ServerPanelReporter(panelClient, HandlePanelCommand);
        var reporterTask = reporter.RunAsync(Shutdown.Token);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, Shutdown.Token);
        }
        catch (OperationCanceledException) when (Shutdown.IsCancellationRequested)
        {
        }

        await reporterTask;
        return Volatile.Read(ref _exitCode);
    }

    private static void HandlePanelCommand(PanelCommand command)
    {
        switch (command.Action?.Trim().ToLowerInvariant())
        {
            case "start":
                Console.WriteLine("ServerPanel start command ignored: the bot is already running.");
                break;

            case "stop":
                Console.WriteLine("ServerPanel requested a graceful stop.");
                Shutdown.Cancel();
                break;

            case "restart":
                if (TryStartReplacementProcess())
                {
                    Console.WriteLine("ServerPanel requested a restart.");
                    Interlocked.Exchange(ref _exitCode, RestartRequestedExitCode);
                    Shutdown.Cancel();
                }
                else
                {
                    Console.Error.WriteLine(
                        "ServerPanel restart was not applied because the bot is not running from its own executable.");
                }
                break;

            default:
                Console.Error.WriteLine("ServerPanel command ignored: action is not allowed.");
                break;
        }
    }

    private static bool TryStartReplacementProcess()
    {
        var processPath = Environment.ProcessPath;
        var expectedName = typeof(Program).Assembly.GetName().Name;

        if (string.IsNullOrWhiteSpace(processPath) ||
            !File.Exists(processPath) ||
            !string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                expectedName,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = processPath,
                WorkingDirectory = Environment.CurrentDirectory,
                UseShellExecute = false
            });
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}

internal sealed class Bot
{
    private const long PollingErrorLogIntervalMilliseconds = 30_000;
    private static long _nextPollingErrorLogAt;
    private readonly TelegramBotClient _client;

    internal Bot(string token)
    {
        _client = new TelegramBotClient(token);
    }

    internal void Start(CancellationToken cancellationToken)
    {
        _client.StartReceiving(UpdateHandler, PollingErrorHandler, cancellationToken: cancellationToken);
    }

    private static Task PollingErrorHandler(
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
            // Exception messages may contain transport details. Log only the type so the bot token
            // and Telegram metadata cannot accidentally reach console collectors or ServerPanel.
            Console.Error.WriteLine($"Telegram polling error: {exception.GetType().Name}");
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

    private async Task SendChatId(Message message, CancellationToken cancellationToken)
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

    private async Task SendUserId(Message message, CancellationToken cancellationToken)
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
