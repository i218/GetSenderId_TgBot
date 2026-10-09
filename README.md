# GetSenderId Telegram Bot

A small Telegram bot that accepts a forwarded message and returns the available Telegram ID of its user or channel.

## Privacy boundary

- The bot processes each update in memory and has no message database or user analytics.
- Messages that have not been forwarded are ignored.
- Message text, Telegram IDs, usernames, and chat metadata are not exported.
- Telegram errors are recorded only by exception type, without exception messages or transport details.

This is a deliberately minimal data-handling design, not a claim of absolute anonymity. Telegram still delivers the update to the bot, and the bot must inspect the forwarding metadata required to return an ID.

## Setup

The token is supplied only through an application-specific environment variable:

```powershell
$env:GETSENDERID_TGBOT_TOKEN = "<bot token>"
dotnet run --project GetSenderId_TgBot
```

For regular desktop use, store it as a user-level environment variable rather than a machine-level variable:

```powershell
[Environment]::SetEnvironmentVariable(
    "GETSENDERID_TGBOT_TOKEN",
    "<bot token>",
    "User"
)
```

Restart the application after changing a persistent environment variable. `Secret.cs` is no longer required.

## System tray operation

The application starts hidden and immediately places its icon in the Windows notification area.

- Double-click the icon or select the open command from its menu to display the status window.
- The minimize button and the close button hide the window back to the tray without stopping the bot.
- Select the exit command from the tray icon menu to stop Telegram polling gracefully and terminate the process.
- The ServerPanel `stop` command also terminates the process instead of merely hiding the window.

For diagnostics, pass `--show` to display the window immediately. Without this flag, the application always starts hidden.

## ServerPanel

The client is enabled by default and uses only the loopback API:

```text
http://127.0.0.1:5088
```

The permanent host ID is `get-sender-id-tgbot`. The following information is reported every five seconds:

- Process name
- Environment
- Process CPU usage, normalized to a `0–100%` range across all logical processors
- Memory usage
- Uptime
- Assembly version
- Application health status: `Healthy`

Message counts, request rate, error rate, and all Telegram fields are deliberately excluded. The panel receives only operational process metrics needed for monitoring.

Commands are handled using a fixed allowlist:

| Command | Behavior |
|---|---|
| `start` | Safe no-op: a process receiving the command is already running |
| `stop` | Gracefully cancels polling and terminates the process |
| `restart` | Starts the same trusted `.exe`, then terminates the current process |

`restart` is rejected when the application is started with `dotnet run`, because the current process in that mode is the shared `dotnet.exe`, not the application executable.

ServerPanel leases commands for 30 seconds. The bot reports each command result to the ACK endpoint using the current `leaseToken`; a command whose acknowledgement is not accepted can be leased again. Unknown actions are rejected through a failed ACK. For `stop` and `restart`, the bot sends a successful acknowledgement before terminating the current process so the ACK is not lost during shutdown.

Command IDs are stable across redelivery, while lease tokens are not. Any future state-changing command handler must therefore remain idempotent by command ID. Do not expose the panel API beyond loopback without TLS, authentication, and a separate authorization model.
