using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace ExplorerBrokerProbe;

internal static unsafe partial class Native
{
    internal static void Check(int result) { if (result == 0) throw new Win32Exception(Marshal.GetLastPInvokeError()); }
    internal static bool InJob(nint process, nint job = 0) { Check(IsProcessInJob(process, job, out var result)); return result != 0; }
    internal static uint ImmediateJobFlags()
    {
        byte* data = stackalloc byte[144];
        Check(QueryInformationJobObject(0, 9, data, 144, 0));
        return *(uint*)(data + 16);
    }
    internal static nint Job(uint flags)
    {
        var job = CreateJobObjectW(0, null); if (job == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        byte* data = stackalloc byte[144]; new Span<byte>(data, 144).Clear(); *(uint*)(data + 16) = flags;
        try { Check(SetInformationJobObject(job, 9, data, 144)); return job; } catch { Close(job); throw; }
    }
    internal static void Assign(nint job, nint process) => Check(AssignProcessToJobObject(job, process));
    internal static void Close(nint handle) => Check(CloseHandle(handle));
    internal static int Parent(Process process)
    {
        nint* data = stackalloc nint[6];
        var status = NtQueryInformationProcess(process.Handle, 0, data, (uint)(6 * sizeof(nint)), out _);
        if (status != 0) throw new InvalidOperationException("Process identity query failed: " + status);
        return checked((int)data[5]);
    }
    internal static Process Breakaway(string executable, string arguments)
    {
        if (!Environment.Is64BitProcess) throw new PlatformNotSupportedException("Probe supports Windows x64 only");
        var command = ('"' + executable + "\" " + arguments + '\0').ToCharArray();
        byte* startup = stackalloc byte[104]; new Span<byte>(startup, 104).Clear(); *(int*)startup = 104;
        nint* info = stackalloc nint[3];
        fixed (char* text = command)
            Check(CreateProcessW(executable, text, 0, 0, 0, 0x01000000 | 0x08000000, 0, null, startup, info));
        try { var process = Process.GetProcessById(*(int*)(info + 2)); _ = process.Handle; return process; }
        finally { Close(info[0]); Close(info[1]); }
    }
    private static byte[] TokenData(nint token, int kind)
    {
        GetTokenInformation(token, kind, null, 0, out var length);
        var data = new byte[length];
        fixed (byte* buffer = data) Check(GetTokenInformation(token, kind, buffer, length, out _));
        return data;
    }
    private static uint TokenInt(nint token, int kind) => BitConverter.ToUInt32(TokenData(token, kind));
    internal static string Identity(Process process)
    {
        Check(OpenProcessToken(process.Handle, 8, out var token));
        try
        {
            using var identity = new WindowsIdentity(token);
            var statistics = TokenData(token, 10);
            return $"{identity.User?.Value}|session={TokenInt(token, 12)}|authentication={BitConverter.ToUInt64(statistics, 8)}|elevationType={TokenInt(token, 18)}";
        }
        finally { Close(token); }
    }
    internal static void RequireSupportedToken(Process process)
    {
        Check(OpenProcessToken(process.Handle, 8, out var token));
        try
        {
            if (TokenInt(token, 20) != 0 || TokenInt(token, 29) != 0 || IsTokenRestricted(token) != 0)
                throw new InvalidOperationException("Unsupported elevated, AppContainer or restricted token");
            // TOKEN_MANDATORY_LABEL embeds a SID pointer; query into pinned native storage.
            byte* label = stackalloc byte[256]; Check(GetTokenInformation(token, 25, label, 256, out _));
            var sid = *(byte**)label; var count = sid[1]; var integrity = *(uint*)(sid + 8 + (count - 1) * 4);
            if (integrity != 0x2000) throw new InvalidOperationException("Only medium integrity admitted by this probe");
        }
        finally { Close(token); }
    }
    internal static void RequireNoImpersonation()
    {
        if (OpenThreadToken(GetCurrentThread(), 8, 1, out var token) != 0) { Close(token); throw new InvalidOperationException("Impersonation not supported"); }
        if (Marshal.GetLastPInvokeError() != 1008) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int IsProcessInJob(nint process, nint job, out int result);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int QueryInformationJobObject(nint job, int kind, void* data, uint size, nint returned);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int SetInformationJobObject(nint job, int kind, void* data, uint size);
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)] private static partial nint CreateJobObjectW(nint attributes, string? name);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int AssignProcessToJobObject(nint job, nint process);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int CloseHandle(nint handle);
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static partial int GetNamedPipeClientProcessId(nint pipe, out uint pid);
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)] private static partial int CreateProcessW(string application, char* command, nint processAttributes, nint threadAttributes, int inherit, uint flags, nint environment, string? directory, void* startup, void* info);
    [LibraryImport("ntdll.dll")] private static partial int NtQueryInformationProcess(nint process, int kind, void* data, uint size, out uint returned);
    [LibraryImport("advapi32.dll", SetLastError = true)] private static partial int OpenProcessToken(nint process, uint access, out nint token);
    [LibraryImport("advapi32.dll", SetLastError = true)] private static partial int GetTokenInformation(nint token, int kind, void* data, uint size, out uint returned);
    [LibraryImport("advapi32.dll")] private static partial int IsTokenRestricted(nint token);
    [LibraryImport("advapi32.dll", SetLastError = true)] private static partial int OpenThreadToken(nint thread, uint access, int openAsSelf, out nint token);
    [LibraryImport("kernel32.dll")] private static partial nint GetCurrentThread();
    [LibraryImport("user32.dll")] internal static partial nint GetShellWindow();
    [LibraryImport("user32.dll")] internal static partial uint GetWindowThreadProcessId(nint window, out uint pid);
    [LibraryImport("ole32.dll")] internal static partial int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint result);
}
