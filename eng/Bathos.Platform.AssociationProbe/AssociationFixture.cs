using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Win32;

internal static class AssociationFixture
{
    internal static void Run(string receiver)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!File.Exists(receiver)) throw new FileNotFoundException("Owned receiver required.", receiver);
        var id = "BathosProbe" + Guid.NewGuid().ToString("N");
        var extension = "." + id;
        var root = @"Software\Classes\";
        var folder = Directory.CreateTempSubdirectory("bathos-association-");
        var document = Path.Combine(folder.FullName, "owned space \u03a9" + extension);
        try
        {
            File.WriteAllText(document, "owned fixture");
            using (var key = Registry.CurrentUser.CreateSubKey(root + extension + @"\OpenWithProgids"))
                key.SetValue(id, Array.Empty<byte>(), RegistryValueKind.None);
            using (var key = Registry.CurrentUser.CreateSubKey(root + id)) key.SetValue(null, "Bathos owned fixture");
            List<Handler> Read() => Sta(() => Native.Read(extension));
            void Register(string pipe)
            {
                using var command = Registry.CurrentUser.CreateSubKey(root + id + @"\shell\open\command");
                command.SetValue(null, $"\"{receiver}\" --association-marker {pipe} \"%1\"");
            }
            string? previousName = null;
            for (var iteration = 0; iteration < 2; iteration++)
            {
                var pipeName = id + iteration;
                using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                Register(pipeName);
                var candidates = Read();
                var owned = candidates.Where(h => string.Equals(h.Name, receiver, StringComparison.OrdinalIgnoreCase)).ToArray();
                Console.WriteLine(JsonSerializer.Serialize(new { Stage = iteration, OwnedCount = owned.Length, Total = candidates.Count }));
                Console.WriteLine(JsonSerializer.Serialize(owned.Select(h => new { h.ProgId, h.AppId })));
                if (owned.Length != 1 || owned[0].Name is not { } name) throw new IOException("Owned registration did not resolve uniquely.");
                Console.WriteLine(JsonSerializer.Serialize(new { SameNameAfterCommandChange = previousName == name }));
                previousName = name;
                var connected = pipe.WaitForConnectionAsync(timeout.Token);
                Sta(() => Native.Read(extension, name, document, id));
                connected.GetAwaiter().GetResult();
                using var reader = new StreamReader(pipe);
                var response = reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult() ?? throw new IOException("Missing acknowledgment.");
                using var json = JsonDocument.Parse(response);
                var received = json.RootElement.GetProperty("Arguments").EnumerateArray().Select(v => v.GetString()).ToArray();
                if (!received.SequenceEqual(new[] { "--association-marker", pipeName, document })) throw new IOException("Arguments changed.");
                try
                {
                    using var child = Process.GetProcessById(json.RootElement.GetProperty("ProcessId").GetInt32());
                    child.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
                }
                catch (ArgumentException) { /* Receiver already exited after acknowledgment. */ }
                Console.WriteLine("Native activation preserved marker, pipe, and Unicode document arguments.");
            }
            using (var key = Registry.CurrentUser.CreateSubKey(root + extension + @"\OpenWithProgids"))
                key.SetValue(id + "Duplicate", Array.Empty<byte>(), RegistryValueKind.None);
            using (var key = Registry.CurrentUser.CreateSubKey(root + id + "Duplicate" + @"\shell\open\command"))
                key.SetValue(null, $"\"{receiver}\" --different-marker \"%1\"");
            Console.WriteLine(JsonSerializer.Serialize(new { SameExecutableDifferentCommands = Read().Count(h => h.Name == previousName) }));
            Console.WriteLine(JsonSerializer.Serialize(Read().Where(h => h.Name == previousName).Select(h => new { h.ProgId, h.AppId })));
            using (var key = Registry.CurrentUser.OpenSubKey(root + extension + @"\OpenWithProgids", writable: true))
                (key ?? throw new IOException("Missing owned registration.")).DeleteValue(id);
            Registry.CurrentUser.DeleteSubKeyTree(root + id);
            var replacement = Read().Where(h => h.Name == previousName).ToArray();
            if (replacement.Length != 1 || replacement[0].ProgId.Value != id + "Duplicate")
                throw new IOException("Replacement registration identity was not distinguished.");
            Console.WriteLine("Same-name replacement has a different ProgID; the original descriptor can reject it.");
            try
            {
                Sta(() => Native.Read(extension, previousName, document, id));
                throw new InvalidOperationException("Stale ProgID selected a replacement registration.");
            }
            catch (IOException error) when (error.Message == "Selected handler no longer exists.")
            {
                Console.WriteLine("ProgID matching rejected the same-name replacement without activation.");
            }
            Registry.CurrentUser.DeleteSubKeyTree(root + extension);
            Registry.CurrentUser.DeleteSubKeyTree(root + id + "Duplicate");
            Console.WriteLine(JsonSerializer.Serialize(new { RemovedHandlerStillEnumerated = Read().Any(h => h.Name == previousName) }));
            try
            {
                Sta(() => Native.Read(extension, previousName, document));
                throw new InvalidOperationException("Removed handler was invoked.");
            }
            catch (IOException error) when (error.Message == "Selected handler no longer exists.")
            {
                Console.WriteLine("Removed selection failed explicitly without default fallback.");
            }
            var direct = new ProcessStartInfo(receiver) { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
            direct.ArgumentList.Add(document);
            using var process = Process.Start(direct) ?? throw new IOException("Receiver did not start.");
            var errors = process.StandardError.ReadToEndAsync();
            using var directTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { process.WaitForExitAsync(directTimeout.Token).GetAwaiter().GetResult(); }
            finally
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            }
            errors.GetAwaiter().GetResult();
            if (process.ExitCode == 0) throw new IOException("Executable-only invocation unexpectedly satisfied marker contract.");
            Console.WriteLine("Executable-plus-file invocation failed the marker contract as expected.");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(root + extension, false);
            Registry.CurrentUser.DeleteSubKeyTree(root + id, false);
            Registry.CurrentUser.DeleteSubKeyTree(root + id + "Duplicate", false);
            folder.Delete(true);
        }
    }

    private static T Sta<T>(Func<T> action)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        T? result = default;
        Exception? failure = null;
        var thread = new Thread(() => { try { result = action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return result ?? throw new IOException("Worker returned no result.");
    }
}
