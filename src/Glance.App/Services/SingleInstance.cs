namespace Glance.App.Services;

public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Glance.App.SingleInstance";
    private const string EventName = @"Local\Glance.App.Show";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private CancellationTokenSource? _listen;
    private bool _ownsMutex;

    public bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out var created);
            _ownsMutex = created;
            return created;
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
            return true;
        }
    }

    public void SignalExisting()
    {
        try
        {
            using var existing = EventWaitHandle.OpenExisting(EventName);
            existing.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }

    public void StartListening(Action onShow)
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        _listen = new CancellationTokenSource();
        var token = _listen.Token;
        Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                var signaled = false;
                try
                {
                    signaled = _showEvent.WaitOne(TimeSpan.FromMilliseconds(250));
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (signaled && !token.IsCancellationRequested)
                {
                    onShow();
                }
            }
        }, token);
    }

    public void Dispose()
    {
        _listen?.Cancel();
        _showEvent?.Dispose();
        if (_ownsMutex)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _mutex?.Dispose();
    }
}
