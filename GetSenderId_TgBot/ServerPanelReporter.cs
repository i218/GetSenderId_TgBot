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
    private readonly Action<bool> _availabilityChanged;
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private bool? _isAvailable;

    internal ServerPanelReporter(
        HttpClient http,
        Action<PanelCommand> commandHandler,
        Action<bool> availabilityChanged)
    {
        _http = http;
        _commandHandler = commandHandler;
        _availabilityChanged = availabilityChanged;
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

                SetAvailability(true);

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
                SetAvailability(false);
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

    private void SetAvailability(bool available)
    {
        if (_isAvailable == available)
        {
            return;
        }

        _isAvailable = available;
        _availabilityChanged(available);
    }
}
