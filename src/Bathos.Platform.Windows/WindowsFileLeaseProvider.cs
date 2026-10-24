using System.Runtime.InteropServices;
using Bathos.Platform.Files;
using Bathos.Platform.Paths;
using Microsoft.Win32.SafeHandles;

namespace Bathos.Platform.Windows;

/// <summary>Cooperative Windows leases using an immediate exclusive byte-range lock on an owned disk-file handle.</summary>
public sealed partial class WindowsFileLeaseProvider : ISharedFileLeaseProvider
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
        if (PlatformHost.Current != PlatformOperatingSystem.Windows) throw new PlatformNotSupportedException();
        path = NativeFilePaths.Windows.NormalizeAbsolute(path);
        cancellationToken.ThrowIfCancellationRequested();
        // Shared opens do not establish ownership. OPEN_ALWAYS, OPEN_REPARSE_POINT, noninheritable synchronous handle.
        var nativePath = path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
        var handle = Open(nativePath, 0xc0000000, 7, 0, 4, 0x00200000, 0);
        var acquired = false;
        try
        {
            if (handle.IsInvalid) throw FileEntryErrors.Native();
            if (GetFileType(handle) != 1) throw new IOException("A regular disk lock file is required.");
            if (!GetFileInformationByHandleEx(handle, 9, out var attributes, 8)) throw FileEntryErrors.Native();
            if ((attributes.Attributes & (0x10u | 0x400u | 0x40u)) != 0)
                throw new IOException("A regular lock file without a reparse point is required.");
            var overlapped = new Overlapped();
            if (!LockFileEx(handle, mode == FileLeaseMode.Shared ? 1u : 3u, 0, 1, 0, ref overlapped)) // FAIL_IMMEDIATELY, optionally EXCLUSIVE_LOCK; byte zero
            {
                var error = Marshal.GetLastPInvokeError();
                if (error == 33) return null; // ERROR_LOCK_VIOLATION
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
        var overlapped = new Overlapped();
        if (!UnlockFileEx(handle, 0, 1, 0, ref overlapped))
            throw new IOException("The native file lease could not be released.", new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Overlapped { internal nuint Internal; internal nuint InternalHigh; internal uint Offset; internal uint OffsetHigh; internal nint Event; }
    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTag { internal uint Attributes; internal uint ReparseTag; }
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle Open(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint GetFileType(SafeFileHandle handle);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out AttributeTag information, uint size);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LockFileEx(SafeFileHandle handle, uint flags, uint reserved, uint lengthLow, uint lengthHigh, ref Overlapped overlapped);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnlockFileEx(SafeFileHandle handle, uint reserved, uint lengthLow, uint lengthHigh, ref Overlapped overlapped);
}
