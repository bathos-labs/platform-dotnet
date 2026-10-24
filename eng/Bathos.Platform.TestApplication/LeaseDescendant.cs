using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal static partial class LeaseDescendant
{
    internal static Process Start(string configuration)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing owned application path.");
        if (OperatingSystem.IsWindows())
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false };
            start.ArgumentList.Add(configuration);
            return Process.Start(start) ?? throw new InvalidOperationException("The descendant did not start.");
        }

        // Unlike Process.Start, plain posix_spawn does not close unrelated descriptors on our behalf.
        // This makes the test sensitive to a missing O_CLOEXEC on the production lock descriptor.
        using var arguments = new NativeStrings([executable, configuration]);
        using var environment = new NativeStrings(Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .Select(entry => $"{entry.Key}={entry.Value}").ToArray());
        var error = Spawn(out var processId, executable, 0, 0, arguments.Address, environment.Address);
        if (error != 0) throw new IOException("The owned descendant did not start.", new Win32Exception(error));
        return Process.GetProcessById(processId);
    }

    private sealed class NativeStrings : IDisposable
    {
        private readonly List<nint> _strings = [];
        internal nint Address { get; }

        internal NativeStrings(string[] values)
        {
            Address = Marshal.AllocHGlobal((values.Length + 1) * nint.Size);
            try
            {
                for (var i = 0; i < values.Length; i++)
                {
                    var text = Marshal.StringToCoTaskMemUTF8(values[i]);
                    _strings.Add(text);
                    Marshal.WriteIntPtr(Address, i * nint.Size, text);
                }
                Marshal.WriteIntPtr(Address, values.Length * nint.Size, 0);
            }
            catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            foreach (var text in _strings) Marshal.FreeCoTaskMem(text);
            Marshal.FreeHGlobal(Address);
        }
    }

    [LibraryImport("libc", EntryPoint = "posix_spawn", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Spawn(out int processId, string executable, nint actions, nint attributes, nint arguments, nint environment);
}
