namespace Bathos.Platform.Linux;

// The portal owns its UI and event loop. A private blocking D-Bus connection stays on one dedicated worker;
// neither the host UI loop nor a thread-pool worker is occupied while the user chooses a color.
internal sealed class LinuxScreenColorSampler(Func<ScreenColor?> pickColor) : IScreenColorSampler
{
    private static int _active;

    public Task<ScreenColor?> SampleAsync()
    {
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
            throw new InvalidOperationException("A screen color picker is already open.");
        try
        {
            return Task.Factory.StartNew(() =>
            {
                try { return pickColor(); }
                finally { Interlocked.Exchange(ref _active, 0); }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        catch { Interlocked.Exchange(ref _active, 0); throw; }
    }
}
