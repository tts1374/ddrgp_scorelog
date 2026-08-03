namespace DDRGpScoreViewer.Tray;

public sealed class SingleInstanceCoordinator : IDisposable
{
    public const string DefaultName = "Local\\com.tts1374.ddrgp_scorelog";
    private readonly Semaphore semaphore;
    private readonly EventWaitHandle activationEvent;
    private readonly CancellationTokenSource cancellation = new();
    private Task? listener;
    private bool ownsMutex;

    private SingleInstanceCoordinator(string name, Semaphore semaphore, EventWaitHandle activationEvent, bool ownsMutex)
    {
        Name = name;
        this.semaphore = semaphore;
        this.activationEvent = activationEvent;
        this.ownsMutex = ownsMutex;
    }

    public string Name { get; }
    public bool IsPrimary => ownsMutex;

    public static SingleInstanceCoordinator Acquire(string name = DefaultName)
    {
        var semaphore = new Semaphore(1, 1, name + ".semaphore");
        var acquired = semaphore.WaitOne(0);
        var activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".activate");
        var instance = new SingleInstanceCoordinator(name, semaphore, activationEvent, acquired);
        if (!acquired)
        {
            activationEvent.Set();
        }
        return instance;
    }

    public void Listen(Action activate)
    {
        if (!IsPrimary || listener is not null)
        {
            return;
        }
        listener = Task.Run(() =>
        {
            var handles = new WaitHandle[] { activationEvent, cancellation.Token.WaitHandle };
            while (WaitHandle.WaitAny(handles) == 0)
            {
                activate();
            }
        });
    }

    public void Dispose()
    {
        cancellation.Cancel();
        listener?.Wait(TimeSpan.FromSeconds(1));
        if (ownsMutex)
        {
            semaphore.Release();
            ownsMutex = false;
        }
        activationEvent.Dispose();
        semaphore.Dispose();
        cancellation.Dispose();
    }
}
