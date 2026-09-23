using System.Diagnostics;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;

namespace GetSenderId_TgBot;

internal sealed record PanelCommand(string? Id, string? Action, DateTimeOffset CreatedAtUtc);

internal sealed class ServerPanelReporter : IDisposable
{
    private const string HostId = "get-sender-id-tgbot";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly Action<PanelCommand> _commandHandler;
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private bool _unavailableReported;

    internal ServerPanelReporter(HttpClient http, Action<PanelCommand> commandHandler)
    {
        _http = http;
        _commandHandler = commandHandler;
    }

    internal async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _process.Refresh();

                using var heartbeatResponse = await _http.PostAsJsonAsync(
                    $"/api/hosts/{HostId}/heartbeat",
                    new
                    {
                        name = "GetSenderId Telegram Bot",
                        environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production",
                        memoryMb = _process.WorkingSet64 / 1024d / 1024d,
                        uptimeSeconds = (long)_uptime.Elapsed.TotalSeconds,
                        version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(),
                        health = "Healthy"
                    },
                    stoppingToken);
                heartbeatResponse.EnsureSuccessStatusCode();

                var commands = await _http.GetFromJsonAsync<PanelCommand[]>(
                    $"/api/hosts/{HostId}/commands",
                    stoppingToken) ?? [];

                ReportAvailable();

                foreach (var command in commands)
                {
                    _commandHandler(command);
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
            {
                ReportUnavailable(exception);
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        _process.Dispose();
    }

    private void ReportUnavailable(Exception exception)
    {
        if (_unavailableReported)
        {
            return;
        }

        _unavailableReported = true;
        Console.Error.WriteLine($"ServerPanel is unavailable: {exception.GetType().Name}");
    }

    private void ReportAvailable()
    {
        if (!_unavailableReported)
        {
            return;
        }

        _unavailableReported = false;
        Console.WriteLine("ServerPanel connection restored.");
    }
}
