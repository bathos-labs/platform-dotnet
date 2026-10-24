using System.Runtime.InteropServices;
using Bathos.Platform.Files;
using Microsoft.Win32.SafeHandles;

namespace Bathos.Platform.Linux;

/// <summary>Cooperative Linux leases using nonblocking exclusive flock on an owned file descriptor.</summary>
public sealed partial class LinuxFileLeaseProvider : ISharedFileLeaseProvider
{
    /// <include file="../Bathos.Platform/Documentation/Contracts.xml" path="contracts/TryAcquireFileLease/*" />
    public IFileLease? TryAcquire(string path, CancellationToken cancellationToken = default) =>
        TryAcquire(path, beforeOwnershipTransfer: null, cancellationToken);

    /// <include file="../Bathos.Platform/Documentation/Contracts.xml" path="contracts/TryAcquireSharedFileLease/*" />
    public IFileLease? TryAcquire(string path, FileLeaseMode mode, CancellationToken cancellationToken = default) =>
        Acquire(path, mode, beforeOwnershipTransfer: null, cancellationToken);

    // Call-scoped seam for retirement after the real native lock, before transferring ownership to the caller.
    internal static IFileLease? TryAcquire(string path, Action? beforeOwnershipTransfer, CancellationToken cancellationToken) =>
        Acquire(path, FileLeaseMode.Exclusive, beforeOwnershipTransfer, cancellationToken);

    private static HeldFileLease? Acquire(string path, FileLeaseMode mode, Action? beforeOwnershipTransfer, CancellationToken cancellationToken)
    {
        if (mode is not (FileLeaseMode.Shared or FileLeaseMode.Exclusive)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (PlatformHost.Current != PlatformOperatingSystem.Linux) throw new PlatformNotSupportedException();
        path = FileLeasePath.NormalizeUnix(path);
        cancellationToken.ThrowIfCancellationRequested();
        // O_RDWR | O_CREAT | O_NONBLOCK | O_NOFOLLOW | O_NOCTTY | O_CLOEXEC; mode 0600, subject to umask.
        // Raw open avoids the runtime's implicit flock and preserves exact contention/error classification.
        var descriptor = Open(path, 0x2 | 0x40 | 0x800 | 0x100 | 0x20000 | 0x80000, 0x180);
        if (descriptor < 0) throw FileEntryErrors.Native();
        var handle = new SafeFileHandle((nint)descriptor, ownsHandle: true);
        var acquired = false;
        try
        {
            if (StatX(handle, "", 0x1000, 1, out var info) != 0) throw FileEntryErrors.Native();
            if ((info.Mask & 1) == 0) throw new IOException("The filesystem did not identify the lock-file kind.");
            FileEntryErrors.RequireRegular(info.Mode);
            if (FLock(handle, (mode == FileLeaseMode.Shared ? 1 : 2) | 4) != 0) // LOCK_SH/LOCK_EX | LOCK_NB
            {
                var error = Marshal.GetLastPInvokeError();
                if (error == 11) return null; // EWOULDBLOCK
                throw new IOException("The native file lease could not be acquired.", new System.ComponentModel.Win32Exception(error));
            }
            acquired = true;
            beforeOwnershipTransfer?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            var lease = new HeldFileLease(handle, Release);
            handle = null;
            return lease;
        }
        finally
        {
            if (handle is not null)
            {
                try { if (acquired) Release(handle); }
                finally { handle.Dispose(); }
            }
        }
    }

    private static void Release(SafeFileHandle handle)
    {
        if (FLock(handle, 8) != 0) // LOCK_UN, before close: a concurrent fork may temporarily retain the descriptor.
            throw new IOException("The native file lease could not be released.", new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
    }

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct Stat { [FieldOffset(0)] internal uint Mask; [FieldOffset(28)] internal ushort Mode; }
    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int StatX(SafeFileHandle handle, string path, int flags, uint mask, out Stat info);
    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string path, int flags, uint mode);
    [LibraryImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static partial int FLock(SafeFileHandle handle, int operation);
}
