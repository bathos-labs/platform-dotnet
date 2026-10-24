using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Bathos.Platform.Tests;

internal static partial class WindowsLeaseRelease
{
    // Process signaling does not promise that Windows has completed its implicit byte-range unlock.
    // Await the kernel's lock completion, with cancellation, instead of retrying TryAcquire or sleeping.
    internal static async Task WaitAsync(string path, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete, FileOptions.Asynchronous);
        using var signal = new EventWaitHandle(false, EventResetMode.ManualReset);
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<Overlapped>());
        var pending = false;
        var acquired = false;
        try
        {
            Exception? failure = null;
            try
            {
                // The event and unmanaged OVERLAPPED remain owned until native completion, including cancellation.
                Marshal.StructureToPtr(new Overlapped { Event = signal.SafeWaitHandle.DangerousGetHandle() }, memory, false);
                acquired = LockFileEx(handle, 2, 0, 1, 0, memory); // EXCLUSIVE_LOCK; an asynchronous native wait.
                if (!acquired)
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error != 997) throw NativeError(error); // ERROR_IO_PENDING
                    pending = true;
                    await SignaledAsync(signal, deadline.Token);
                    acquired = GetOverlappedResult(handle, memory, out _, false);
                    pending = false;
                    if (!acquired) throw NativeError(Marshal.GetLastPInvokeError());
                }
            }
            catch (Exception error) { failure = error; }
            try
            {
                if (pending)
                {
                    CancelIoEx(handle, memory); // Completion may have won the race; observe either outcome before freeing.
                    await SignaledAsync(signal, CancellationToken.None);
                    acquired = GetOverlappedResult(handle, memory, out _, false);
                    if (!acquired && Marshal.GetLastPInvokeError() != 995) // ERROR_OPERATION_ABORTED
                        throw NativeError(Marshal.GetLastPInvokeError());
                }
                if (acquired)
                {
                    signal.Reset();
                    Marshal.StructureToPtr(new Overlapped { Event = signal.SafeWaitHandle.DangerousGetHandle() }, memory, false);
                    if (!UnlockFileEx(handle, 0, 1, 0, memory))
                    {
                        var error = Marshal.GetLastPInvokeError();
                        if (error != 997) throw NativeError(error);
                        await SignaledAsync(signal, CancellationToken.None);
                        if (!GetOverlappedResult(handle, memory, out _, false)) throw NativeError(Marshal.GetLastPInvokeError());
                    }
                }
            }
            catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static async Task SignaledAsync(WaitHandle signal, CancellationToken cancellationToken)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => completed.TrySetResult(),
            null, Timeout.Infinite, executeOnlyOnce: true);
        try { await completed.Task.WaitAsync(cancellationToken); }
        finally { registration.Unregister(null); }
    }

    private static IOException NativeError(int error) => new("The Windows lease-release observation failed.", new Win32Exception(error));

    [StructLayout(LayoutKind.Sequential)]
    private struct Overlapped { internal nuint Internal; internal nuint InternalHigh; internal uint Offset; internal uint OffsetHigh; internal nint Event; }
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LockFileEx(SafeFileHandle handle, uint flags, uint reserved, uint lengthLow, uint lengthHigh, nint overlapped);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnlockFileEx(SafeFileHandle handle, uint reserved, uint lengthLow, uint lengthHigh, nint overlapped);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetOverlappedResult(SafeFileHandle handle, nint overlapped, out uint transferred, [MarshalAs(UnmanagedType.Bool)] bool wait);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CancelIoEx(SafeFileHandle handle, nint overlapped);
}
