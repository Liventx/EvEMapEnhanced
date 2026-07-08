using System;
using System.Threading;
using System.Threading.Tasks;

namespace EvEMapEnhanced.Desktop;

/// <summary>
/// Ensures only one desktop process runs at a time. Secondary launches signal the primary
/// instance instead of starting another UI.
/// </summary>
internal static class SingleInstanceGate
{
    private const string MutexName = "Local\\EvEMapEnhanced.SingleInstance.v1";
    private const string ActivateEventName = "Local\\EvEMapEnhanced.Activate.v1";

    private static Mutex? _mutex;
    private static EventWaitHandle? _activateEvent;
    private static CancellationTokenSource? _listenerCts;

    public static bool TryBecomePrimary()
    {
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        return true;
    }

    public static void SignalPrimaryInstance()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            using var activateEvent = EventWaitHandle.OpenExisting(ActivateEventName);
            activateEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }

    public static void StartActivationListener(Action onActivate)
    {
        if (_activateEvent is null) return;

        _listenerCts = new CancellationTokenSource();
        var token = _listenerCts.Token;
        _ = Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_activateEvent.WaitOne(500))
                        onActivate();
                }
                catch (ObjectDisposedException) when (token.IsCancellationRequested)
                {
                    break;
                }
            }
        }, token);
    }

    public static void Shutdown()
    {
        _listenerCts?.Cancel();
        _listenerCts?.Dispose();
        _listenerCts = null;

        _activateEvent?.Dispose();
        _activateEvent = null;

        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _mutex.Dispose();
            _mutex = null;
        }
    }
}
