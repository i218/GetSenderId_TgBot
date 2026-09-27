namespace GetSenderId_TgBot;

internal sealed class StatusForm : Form
{
    private readonly Label _botStatus;
    private readonly Label _panelStatus;

    internal StatusForm(Icon icon)
    {
        Text = "GetSenderId Telegram Bot";
        Icon = icon;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(460, 200);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;

        var title = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Location = new Point(24, 22),
            Text = "GetSenderId Telegram Bot"
        };

        _botStatus = new Label
        {
            AutoSize = true,
            Location = new Point(24, 62),
            Text = "Telegram: бот запущен"
        };

        _panelStatus = new Label
        {
            AutoSize = true,
            Location = new Point(24, 89),
            Text = "ServerPanel: подключение…"
        };

        var hint = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Location = new Point(24, 124),
            Text = "Крестик и сворачивание прячут окно в область уведомлений."
        };

        var hideButton = new Button
        {
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            AutoSize = true,
            Location = new Point(320, 157),
            Text = "Скрыть в трей"
        };
        hideButton.Click += (_, _) => HideToTray();

        Controls.AddRange([title, _botStatus, _panelStatus, hint, hideButton]);
        AcceptButton = hideButton;
    }

    internal void SetBotStatus(string status)
    {
        _botStatus.Text = $"Telegram: {status}";
    }

    internal void SetPanelAvailable(bool available)
    {
        _panelStatus.Text = available
            ? "ServerPanel: подключён"
            : "ServerPanel: недоступен, бот продолжает работу";
    }

    internal void HideToTray()
    {
        WindowState = FormWindowState.Normal;
        Hide();
    }
}
