using System.ComponentModel;
using System.Runtime.InteropServices;
using Bathos.Platform.Files;
using Microsoft.Win32.SafeHandles;

// Runs only in an owned child. It never opens a user's terminal or changes the test runner's session.
internal static partial class UnixLeaseProbe
{
    internal static SafeFileHandle CreateTerminal(out string path, out SafeFileHandle slave)
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var noTerminal = OperatingSystem.IsMacOS() ? 0x20000 : 0x100;
        var descriptor = OpenTerminal(2 | noTerminal);
        if (descriptor < 0) throw NativeError();
        var master = new SafeFileHandle((nint)descriptor, ownsHandle: true);
        try
        {
            if (GrantTerminal(master) != 0 || UnlockTerminal(master) != 0) throw NativeError();
            // ptsname's static result is copied immediately; this child has no concurrent terminal lookup.
            path = Marshal.PtrToStringUTF8(TerminalName(master)) ?? throw NativeError();
            // Keep the slave open too, matching openpty and avoiding a hangup between fixture setup and the probe.
            var slaveDescriptor = Open(path, 2 | noTerminal);
            if (slaveDescriptor < 0) throw NativeError();
            slave = new SafeFileHandle((nint)slaveDescriptor, ownsHandle: true);
            return master;
        }
        catch { master.Dispose(); throw; }
    }

    internal static void RejectTerminal(IFileLeaseProvider leases, string path)
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (SetSid() < 0) throw NativeError();
        if (HasTerminal()) throw new InvalidOperationException("The isolated session already has a terminal.");
        try
        {
            using var lease = leases.TryAcquire(path);
            throw new InvalidOperationException("A terminal was admitted as a lock file.");
        }
        catch (IOException) { }
        if (HasTerminal()) throw new InvalidOperationException("Rejected acquisition assigned a controlling terminal.");
    }

    private static bool HasTerminal()
    {
        var flags = OperatingSystem.IsMacOS() ? 2 | 4 | 0x20000 : 2 | 0x800 | 0x100;
        var descriptor = Open("/dev/tty", flags);
        if (descriptor < 0)
        {
            if (Marshal.GetLastPInvokeError() == 6) return false; // ENXIO: no controlling terminal.
            throw NativeError();
        }
        using var terminal = new SafeFileHandle((nint)descriptor, ownsHandle: true);
        return true;
    }

    private static IOException NativeError() => new("The owned terminal fixture failed.", new Win32Exception(Marshal.GetLastPInvokeError()));

    // Both supported Unix hosts expose their C runtime under libc; no application-native library is shipped.
    [LibraryImport("libc", EntryPoint = "setsid", SetLastError = true)]
    private static partial int SetSid();
    [LibraryImport("libc", EntryPoint = "posix_openpt", SetLastError = true)]
    private static partial int OpenTerminal(int flags);
    [LibraryImport("libc", EntryPoint = "grantpt", SetLastError = true)]
    private static partial int GrantTerminal(SafeFileHandle master);
    [LibraryImport("libc", EntryPoint = "unlockpt", SetLastError = true)]
    private static partial int UnlockTerminal(SafeFileHandle master);
    [LibraryImport("libc", EntryPoint = "ptsname", SetLastError = true)]
    private static partial nint TerminalName(SafeFileHandle master);
    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string path, int flags);
}
