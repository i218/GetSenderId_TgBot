using System.Diagnostics;

namespace GetSenderId_TgBot;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const int RestartRequestedExitCode = 23;

    private readonly CancellationTokenSource _shutdown = new();
    private readonly HttpClient _panelClient;
    private readonly ServerPanelReporter _reporter;
    private readonly Task _reporterTask;
    private readonly Bot _bot;
    private readonly Icon _appIcon;
    private readonly ContextMenuStrip _trayMenu;
    private readonly NotifyIcon _trayIcon;
    private readonly StatusForm _statusForm;
    private bool _exitRequested;

    internal TrayApplicationContext(string token, bool showOnStart = false)
    {
        _appIcon = LoadApplicationIcon();
        _statusForm = new StatusForm(_appIcon);
        _ = _statusForm.Handle;
        _statusForm.FormClosing += StatusFormOnFormClosing;
        _statusForm.Resize += StatusFormOnResize;

        var openItem = new ToolStripMenuItem("Открыть");
        openItem.Click += (_, _) => ShowStatusWindow();
        var exitItem = new ToolStripMenuItem("Выход");
        exitItem.Click += async (_, _) => await RequestExitAsync();

        _trayMenu = new ContextMenuStrip();
        _trayMenu.Items.Add(openItem);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(exitItem);

        _trayIcon = new NotifyIcon
        {
            ContextMenuStrip = _trayMenu,
            Icon = _appIcon,
            Text = "GetSenderId Telegram Bot",
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowStatusWindow();

        _bot = new Bot(token, status => PostToUi(() => _statusForm.SetBotStatus(status)));
        _bot.Start(_shutdown.Token);

        _panelClient = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:5088"),
            Timeout = TimeSpan.FromSeconds(3)
        };
        _reporter = new ServerPanelReporter(
            _panelClient,
            HandlePanelCommand,
            available => PostToUi(() => _statusForm.SetPanelAvailable(available)));
        _reporterTask = _reporter.RunAsync(_shutdown.Token);

        if (showOnStart)
        {
            ShowStatusWindow();
        }
    }

    internal int ExitCode { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayMenu.Dispose();
            _statusForm.Dispose();
            _reporter.Dispose();
            _panelClient.Dispose();
            _shutdown.Dispose();
            _appIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Icon LoadApplicationIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : (Icon)SystemIcons.Application.Clone();
    }

    private void StatusFormOnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_exitRequested || eventArgs.CloseReason != CloseReason.UserClosing)
        {
            return;
        }

        eventArgs.Cancel = true;
        _statusForm.HideToTray();
    }

    private void StatusFormOnResize(object? sender, EventArgs eventArgs)
    {
        if (_statusForm.WindowState == FormWindowState.Minimized)
        {
            _statusForm.HideToTray();
        }
    }

    private void ShowStatusWindow()
    {
        if (_exitRequested)
        {
            return;
        }

        _statusForm.Show();
        _statusForm.WindowState = FormWindowState.Normal;
        _statusForm.Activate();
        _statusForm.BringToFront();
    }

    private PanelCommandExecution HandlePanelCommand(PanelCommand command)
    {
        switch (command.Action?.Trim().ToLowerInvariant())
        {
            case "start":
                PostToUi(() => _statusForm.SetBotStatus("бот уже запущен"));
                return new PanelCommandExecution(true, "The bot is already running.");

            case "stop":
                return new PanelCommandExecution(
                    true,
                    "Stop accepted; shutting down.",
                    () => PostToUi(() => _ = RequestExitAsync()));

            case "restart":
                if (!CanStartReplacementProcess())
                {
                    PostToUi(() => _statusForm.SetBotStatus(
                        "перезапуск доступен только из собственного .exe"));
                    return new PanelCommandExecution(
                        false,
                        "Restart is available only when running the application executable.");
                }

                return new PanelCommandExecution(
                    true,
                    "Restart accepted; launching a replacement process.",
                    () => PostToUi(RestartAfterAcknowledgement));

            default:
                PostToUi(() => _statusForm.SetBotStatus(
                    "неизвестная команда ServerPanel отклонена"));
                return new PanelCommandExecution(false, "Unsupported command action.");
        }
    }

    private void PostToUi(Action action)
    {
        if (_exitRequested || _statusForm.IsDisposed)
        {
            return;
        }

        try
        {
            _statusForm.BeginInvoke(action);
        }
        catch (InvalidOperationException) when (_exitRequested || _statusForm.IsDisposed)
        {
        }
    }

    private async Task RequestExitAsync(int exitCode = 0)
    {
        if (_exitRequested)
        {
            return;
        }

        _exitRequested = true;
        ExitCode = exitCode;
        _trayIcon.Visible = false;
        _shutdown.Cancel();

        try
        {
            await _reporterTask;
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }

        _statusForm.Close();
        ExitThread();
    }

    private void RestartAfterAcknowledgement()
    {
        if (TryStartReplacementProcess())
        {
            _ = RequestExitAsync(RestartRequestedExitCode);
            return;
        }

        _statusForm.SetBotStatus("не удалось запустить новый экземпляр");
    }

    private static bool CanStartReplacementProcess()
    {
        var processPath = Environment.ProcessPath;
        var expectedName = typeof(Program).Assembly.GetName().Name;

        return !string.IsNullOrWhiteSpace(processPath) &&
               File.Exists(processPath) &&
               string.Equals(
                   Path.GetFileNameWithoutExtension(processPath),
                   expectedName,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryStartReplacementProcess()
    {
        if (!CanStartReplacementProcess())
        {
            return false;
        }

        var processPath = Environment.ProcessPath!;

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
