namespace GetSenderId_TgBot;

internal sealed class SingleInstanceLease : IDisposable
{
    private const string MutexName = @"Local\GetSenderId_TgBot.SingleInstance";
    private const string ActivationEventName = @"Local\GetSenderId_TgBot.Activate";

    private readonly Mutex _mutex;
    private bool _ownsMutex = true;

    private SingleInstanceLease(Mutex mutex, EventWaitHandle activationEvent)
    {
        _mutex = mutex;
        ActivationEvent = activationEvent;
    }

    internal EventWaitHandle ActivationEvent { get; }

    internal static bool TryAcquire(out SingleInstanceLease? lease)
    {
        var activationEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            ActivationEventName);
        Mutex? mutex = null;

        try
        {
            mutex = new Mutex(true, MutexName, out var createdNew);
            if (!createdNew)
            {
                activationEvent.Set();
                mutex.Dispose();
                activationEvent.Dispose();
                lease = null;
                return false;
            }

            lease = new SingleInstanceLease(mutex, activationEvent);
            return true;
        }
        catch
        {
            mutex?.Dispose();
            activationEvent.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        ActivationEvent.Dispose();

        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
            _ownsMutex = false;
        }

        _mutex.Dispose();
    }
}
