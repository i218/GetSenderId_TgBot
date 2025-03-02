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
            if (updateArg.Type is not UpdateType.Message || updateArg.Message?.ForwardOrigin is null) return;
            var msg = updateArg.Message;
            var chat = msg.Chat;

            switch (msg.ForwardOrigin.Type)
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
            var usernameText = username is null ? "" : $"\n<b>@:</b> @{username}";
            await _client.SendMessage(chat, $"Forwarded from: \n<b>User Id:</b> <code>{idText}</code> {usernameText}", ParseMode.Html);
        }

    }
}
