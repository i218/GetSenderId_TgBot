using System.Diagnostics;
using System.Globalization;

namespace GetSenderId_TgBot;

internal static class Program
{
    private const string RestartAfterArgument = "--restart-after";

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        WaitForRestartPredecessor(args);

        if (!SingleInstanceLease.TryAcquire(out var instanceLease) || instanceLease is null)
        {
            return;
        }

        using (instanceLease)
        {
            RunPrimaryInstance(args, instanceLease.ActivationEvent);
        }
    }

    private static void RunPrimaryInstance(string[] args, EventWaitHandle activationEvent)
    {
        var token = Environment.GetEnvironmentVariable("GETSENDERID_TGBOT_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            MessageBox.Show(
                "Перед запуском задайте переменную окружения GETSENDERID_TGBOT_TOKEN.",
                "GetSenderId Telegram Bot",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Environment.ExitCode = 1;
            return;
        }

        var showOnStart = args.Contains("--show", StringComparer.OrdinalIgnoreCase);
        using var context = new TrayApplicationContext(token, activationEvent, showOnStart);
        Application.Run(context);
        Environment.ExitCode = context.ExitCode;
    }

    private static void WaitForRestartPredecessor(string[] args)
    {
        var argumentIndex = Array.FindIndex(
            args,
            argument => string.Equals(argument, RestartAfterArgument, StringComparison.OrdinalIgnoreCase));
        if (argumentIndex < 0 || argumentIndex + 1 >= args.Length ||
            !int.TryParse(args[argumentIndex + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var processId) ||
            processId == Environment.ProcessId)
        {
            return;
        }

        try
        {
            using var predecessor = Process.GetProcessById(processId);
            predecessor.WaitForExit(30_000);
        }
        catch (ArgumentException)
        {
            // The predecessor already exited before the replacement opened its handle.
        }
    }
}
