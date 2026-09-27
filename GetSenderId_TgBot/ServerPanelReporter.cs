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
    private TimeSpan _previousProcessorTime;
    private long _previousCpuTimestamp;
    private bool? _isAvailable;

    internal ServerPanelReporter(
        HttpClient http,
        Action<PanelCommand> commandHandler,
        Action<bool> availabilityChanged)
    {
        _http = http;
        _commandHandler = commandHandler;
        _availabilityChanged = availabilityChanged;

        _process.Refresh();
        _previousProcessorTime = _process.TotalProcessorTime;
        _previousCpuTimestamp = Stopwatch.GetTimestamp();
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
                        cpuPercent = GetCpuPercent(),
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

    private double GetCpuPercent()
    {
        var processorTime = _process.TotalProcessorTime;
        var timestamp = Stopwatch.GetTimestamp();
        var elapsedSeconds = (timestamp - _previousCpuTimestamp) / (double)Stopwatch.Frequency;
        var processorSeconds = (processorTime - _previousProcessorTime).TotalSeconds;

        _previousProcessorTime = processorTime;
        _previousCpuTimestamp = timestamp;

        if (elapsedSeconds <= 0)
        {
            return 0;
        }

        var usage = processorSeconds / elapsedSeconds / Environment.ProcessorCount * 100;
        return Math.Clamp(usage, 0, 100);
    }
}
