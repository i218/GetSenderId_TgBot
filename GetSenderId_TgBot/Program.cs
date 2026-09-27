namespace GetSenderId_TgBot;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            MessageBox.Show(
                "Перед запуском задайте переменную окружения TELEGRAM_BOT_TOKEN.",
                "GetSenderId Telegram Bot",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Environment.ExitCode = 1;
            return;
        }

        var showOnStart = args.Contains("--show", StringComparer.OrdinalIgnoreCase);
        using var context = new TrayApplicationContext(token, showOnStart);
        Application.Run(context);
        Environment.ExitCode = context.ExitCode;
    }
}
