using System;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace GetSenderId_TgBot
{
    internal class Program
    {
        private static void Main()
        {
            var bot = new Bot();
            bot.Init();

            try { Task.Delay(-1).GetAwaiter().GetResult(); } catch (Exception e) { Console.WriteLine(e.Message); }
            Console.ReadLine();
        }
    }

    public class Bot
    {
        private readonly TelegramBotClient _client = new(Secret.BotApi);

        public void Init()
        {
            _client.StartReceiving(UpdateHandler, PollingErrorHandler);
        }

        private static Task PollingErrorHandler(ITelegramBotClient arg1, Exception excArg, HandleErrorSource arg3, CancellationToken arg4)
        {
            Console.WriteLine(excArg.Message);
            return Task.CompletedTask;
        }

        private async Task UpdateHandler(ITelegramBotClient arg1, Update updateArg, CancellationToken cancellationArg)
        {
            var msg = updateArg.Message;
            if ((updateArg.Type is not UpdateType.Message || msg?.ForwardOrigin is null) && msg is not {Text: "/start"}) return;

            var chat = msg.Chat;

            const string startText = "Hi there, this bot does literally what name says - he gives u the ID of user message was forwarded from." +
                                     "\r\n\r\nIt won't make logs, or reply to unforwarded messages, feel free to use it as an storage \ud83d\ude01" +
                                     "\r\n\r\nIt's also an open-source, u can find code <a href='https://github.com/i218/GetSenderId_TgBot'>here</a>.";

            var options = new LinkPreviewOptions { IsDisabled = true };

            if (msg.Text == "/start")
            {
                await _client.SendMessage(chat, startText, parseMode: ParseMode.Html, linkPreviewOptions: options, cancellationToken: cancellationArg);
                return;
            }

            switch (msg.ForwardOrigin?.Type)
            {
                case MessageOriginType.HiddenUser:
                    await _client.SendMessage(chat, "User has <b>disabled \'forwarding\'</b> in settings, getting id is impossible", ParseMode.Html, cancellationToken: cancellationArg);
                    break;
                case MessageOriginType.User:
                    await UserId(msg);
                    break;
                case MessageOriginType.Chat:
                    await ChatId(msg);
                    break;
                default: return;
            }

        }

        private async Task ChatId(Message msg)
        {
            var chat = msg.Chat;

            if (msg.ForwardFromChat is null) return;
            var idText = msg.ForwardFromChat.Id;
            await _client.SendMessage(chat, $"Forwarded from: \n<b>Channel Id:</b> <code>{idText}</code>", ParseMode.Html);
        }

        private async Task UserId(Message msg)
        {
            var chat = msg.Chat;
            if (msg.ForwardFrom is null) return;
            var idText = msg.ForwardFrom.Id;
            var username = msg.ForwardFrom.Username;
            var usernameText = username is null ? "" : $"\n<b>UserTag:</b> @{username}";
            await _client.SendMessage(chat, $"Forwarded from: \n<b>User Id:</b> <code>{idText}</code> {usernameText}", ParseMode.Html);
        }

    }
}
