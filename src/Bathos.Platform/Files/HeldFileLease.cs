using Microsoft.Win32.SafeHandles;

namespace Bathos.Platform.Files;

internal sealed class HeldFileLease(SafeFileHandle handle, Action<SafeFileHandle> release) : IFileLease
{
    private SafeFileHandle? _handle = handle;
    public void Dispose()
    {
        var owned = Interlocked.Exchange(ref _handle, null);
        if (owned is null) return;
        try { release(owned); }
        finally { owned.Dispose(); }
    }
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}
