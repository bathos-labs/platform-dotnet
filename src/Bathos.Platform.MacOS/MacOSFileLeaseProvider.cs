using System.Runtime.InteropServices;
using Bathos.Platform.Files;
using Microsoft.Win32.SafeHandles;

namespace Bathos.Platform.MacOS;

/// <summary>Cooperative macOS shared or exclusive leases using nonblocking flock on an owned file descriptor.</summary>
public sealed partial class MacOSFileLeaseProvider : IFileLeaseProvider
{
    /// <include file="../Bathos.Platform/Documentation/Contracts.xml" path="contracts/TryAcquireFileLease/*" />
    public IFileLease? TryAcquire(string path, FileLeaseMode mode, CancellationToken cancellationToken = default) =>
        TryAcquire(path, mode, beforeOwnershipTransfer: null, cancellationToken);

    // Call-scoped seam for retirement after the real native lock, before transferring ownership to the caller.
    internal static IFileLease? TryAcquire(string path, FileLeaseMode mode, Action? beforeOwnershipTransfer, CancellationToken cancellationToken)
    {
        if (mode is not (FileLeaseMode.Shared or FileLeaseMode.Exclusive)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (PlatformHost.Current != PlatformOperatingSystem.MacOS) throw new PlatformNotSupportedException();
        path = FileLeasePath.NormalizeUnix(path);
        cancellationToken.ThrowIfCancellationRequested();
        // O_RDWR | O_CREAT | O_NONBLOCK | O_NOFOLLOW | O_NOCTTY | O_CLOEXEC; mode 0600, subject to umask.
        // Raw open avoids the runtime's implicit flock and preserves exact contention/error classification.
        const int flags = 0x2 | 0x200 | 0x4 | 0x100 | 0x20000 | 0x1000000;
        // Darwin arm64 puts variadic arguments on the stack, after all eight integer register slots.
        var descriptor = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? OpenArm64(path, flags, 0, 0, 0, 0, 0, 0, 0x180)
            : Open(path, flags, 0x180);
        if (descriptor < 0) throw FileEntryErrors.Native();
        var handle = new SafeFileHandle((nint)descriptor, ownsHandle: true);
        var acquired = false;
        try
        {
            if (FStat(handle, out var info) != 0) throw FileEntryErrors.Native();
            FileEntryErrors.RequireRegular(info.Mode);
            if (FLock(handle, (mode == FileLeaseMode.Shared ? 1 : 2) | 4) != 0) // LOCK_SH/LOCK_EX | LOCK_NB
            {
                var error = Marshal.GetLastPInvokeError();
                if (error == 35) return null; // EWOULDBLOCK
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

    [StructLayout(LayoutKind.Explicit, Size = 144)]
    private struct Stat { [FieldOffset(4)] internal ushort Mode; }
    [LibraryImport("libSystem.B.dylib", EntryPoint = "fstat", SetLastError = true)]
    private static partial int FStat(SafeFileHandle handle, out Stat info);
    [LibraryImport("libSystem.B.dylib", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string path, int flags, uint mode);
    [LibraryImport("libSystem.B.dylib", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenArm64(string path, int flags, nint x2, nint x3, nint x4, nint x5, nint x6, nint x7, uint mode);
    [LibraryImport("libSystem.B.dylib", EntryPoint = "flock", SetLastError = true)]
    private static partial int FLock(SafeFileHandle handle, int operation);
}
