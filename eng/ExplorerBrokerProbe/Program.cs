using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace ExplorerBrokerProbe;

internal static class Program
{
    private static readonly string[] Payload = ["", "two words", "a\"b", "\\", "end\\", "space end\\", "\\\"quoted", "\u65E5\u672C\u8A9E caf\u00E9 \U0001F989"];
    internal static string Executable => Environment.ProcessPath ?? throw new InvalidOperationException("No executable");

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0) throw new ArgumentException("Use bootstrap <absolute evidence file>, coordinator <file>, caller ..., or target ...");
            switch (args[0])
            {
                case "bootstrap":
                    // Native breakaway is permission-checked by Windows; never broker around a restrictive Job.
                    using (var coordinator = Native.Breakaway(Executable, "coordinator " + Quote(args[1])))
                    {
                        if (!coordinator.WaitForExit(180000)) { coordinator.Kill(true); coordinator.WaitForExit(); throw new TimeoutException("Coordinator watchdog"); }
                        Console.WriteLine(File.ReadAllText(args[1]));
                        return coordinator.ExitCode;
                    }
                case "coordinator":
                    using (var log = new StreamWriter(args[1], false, new UTF8Encoding(false)) { AutoFlush = true })
                    {
                        try { RunAsync(log).GetAwaiter().GetResult(); }
                        catch (Exception error) { log.WriteLine(error); return 1; }
                    }
                    return 0;
                case "caller": Caller(args); return 0;
                case "target": TargetAsync(args).GetAwaiter().GetResult(); return 0;
                default: throw new ArgumentException("Unknown mode");
            }
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static async Task RunAsync(TextWriter log)
    {
        if (Native.InJob(Process.GetCurrentProcess().Handle)) throw new InvalidOperationException("Coordinator still in an ambient Job: no broker submission allowed.");
        await log.WriteLineAsync($"Coordinator {Environment.ProcessId}; no ambient Job; OS {Environment.OSVersion}; runtime {Environment.Version}");
        foreach (var scenario in new[] { "orderly", "tree", "job", "silent-job", "restrictive", "nested-restrictive", "nested-permitted", "no-explorer", "before-submit", "lost-reply" })
            await ScenarioAsync(scenario, log);
        await log.WriteLineAsync("PASS: all captured targets and callers joined; no automatic retry or fallback.");
    }

    private static async Task ScenarioAsync(string scenario, TextWriter log)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var pipeName = "bathos-explorer-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var connected = server.WaitForConnectionAsync(token);
        var start = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("caller"); start.ArgumentList.Add(scenario); start.ArgumentList.Add(pipeName);
        using var caller = Process.Start(start) ?? throw new IOException("Caller creation failed");
        var exited = caller.WaitForExitAsync(CancellationToken.None);
        var stderr = caller.StandardError.ReadToEndAsync(CancellationToken.None);
        var jobs = new List<nint>();
        Process? target = null;
        StreamWriter? writer = null;
        StreamReader? reader = null;
        try
        {
            if (scenario is "job" or "silent-job" or "restrictive" or "nested-restrictive" or "nested-permitted")
            {
                var outer = Native.Job(scenario is "restrictive" or "nested-restrictive" ? 0x2000u : scenario == "silent-job" ? 0x3000u : 0x2800u);
                jobs.Add(outer); Native.Assign(outer, caller.Handle);
                if (scenario.StartsWith("nested-", StringComparison.Ordinal))
                {
                    var inner = Native.Job(0x2800); jobs.Add(inner); Native.Assign(inner, caller.Handle);
                }
            }
            // The coordinator knows the complete chain because it started outside Jobs and assigned every Job.
            // This is test admission evidence, NOT a general client policy heuristic.
            caller.StandardInput.WriteLine(scenario == "nested-restrictive" ? "deny-known-ancestor" : "go");
            var reportLine = await caller.StandardOutput.ReadLineAsync(token) ?? throw new IOException("Caller ended: " + await stderr);
            using var report = JsonDocument.Parse(reportLine);
            await log.WriteLineAsync(scenario + " caller=" + caller.Id + " " + reportLine);
            if (report.RootElement.GetProperty("outcome").GetString() == "not-submitted")
            {
                caller.StandardInput.Close(); await exited.WaitAsync(token);
                await caller.StandardOutput.ReadToEndAsync(token); await stderr.WaitAsync(token);
                if (server.IsConnected) throw new InvalidOperationException("Rejected request launched a target");
                await log.WriteLineAsync($"PASS {scenario}: pre-submission rejection; caller joined; no broker invocation.");
                return;
            }
            await connected;
            Native.Check(Native.GetNamedPipeClientProcessId(server.SafePipeHandle.DangerousGetHandle(), out var targetPid));
            target = Process.GetProcessById(checked((int)targetPid));
            if (!StringComparer.OrdinalIgnoreCase.Equals(target.MainModule?.FileName, Executable)) throw new InvalidOperationException("Target executable mismatch");
            reader = new StreamReader(server, Encoding.UTF8, false, 1024, true);
            writer = new StreamWriter(server, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
            using var hello = JsonDocument.Parse(await reader.ReadLineAsync(token) ?? throw new IOException("No readiness"));
            if (!hello.RootElement.GetProperty("arguments").EnumerateArray().Select(x => x.GetString()).SequenceEqual(Payload)) throw new InvalidOperationException("Argument mismatch");
            var parent = hello.RootElement.GetProperty("parent").GetInt32();
            var broker = report.RootElement.GetProperty("broker").GetInt32();
            if (parent != broker || parent == caller.Id) throw new InvalidOperationException("Not parented by the selected desktop broker");
            if (hello.RootElement.GetProperty("session").GetInt32() != Process.GetCurrentProcess().SessionId) throw new InvalidOperationException("Session changed");
            if (hello.RootElement.GetProperty("identity").GetString() != Native.Identity(Process.GetCurrentProcess())) throw new InvalidOperationException("Token context changed");
            foreach (var job in jobs) if (Native.InJob(target.Handle, job)) throw new InvalidOperationException("Target inherited caller Job");
            await log.WriteLineAsync($"READY target={target.Id}; parent=desktop-broker:{broker}; exact executable/argv/token/session=true; caller Jobs=false; targetAnyJob={Native.InJob(target.Handle)}");
            if (scenario == "tree") caller.Kill(entireProcessTree: true);
            else if (jobs.Count > 0) { foreach (var job in jobs) Native.Close(job); jobs.Clear(); }
            else caller.StandardInput.Close();
            await exited.WaitAsync(token);
            await caller.StandardOutput.ReadToEndAsync(token); await stderr.WaitAsync(token);
            await writer.WriteLineAsync("ping");
            if (await reader.ReadLineAsync(token) != "pong") throw new IOException("Target unresponsive after caller retirement");
            await log.WriteLineAsync($"PASS {scenario}: caller exit and stdout/stderr EOF joined; target responsive.");
            await writer.WriteLineAsync("quit");
            await target.WaitForExitAsync(token);
            await log.WriteLineAsync($"JOINED target={target.Id}; exit={target.ExitCode}; caller={caller.Id}");
        }
        finally
        {
            foreach (var job in jobs) Native.Close(job);
            if (target is not null) { if (!target.HasExited) target.Kill(); await target.WaitForExitAsync(); target.Dispose(); }
            if (!caller.HasExited) caller.Kill(true);
            await exited; await stderr;
            writer?.Dispose(); reader?.Dispose();
            await timeout.CancelAsync();
            try { await connected; } catch (OperationCanceledException) { }
        }
    }

    private static void Caller(string[] args)
    {
        var gate = Console.ReadLine();
        var scenario = args[1];
        var inJob = Native.InJob(Process.GetCurrentProcess().Handle);
        var flags = inJob ? Native.ImmediateJobFlags() : 0;
        if (gate == "deny-known-ancestor" || inJob && (flags & 0x1800) == 0)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { outcome = "not-submitted", reason = "restrictive Job", immediateFlags = flags })); return;
        }
        if (scenario == "no-explorer")
        {
            Console.WriteLine(JsonSerializer.Serialize(new { outcome = "not-submitted", reason = "simulated missing desktop broker" })); return;
        }
        using var broker = new DesktopBroker();
        if (scenario == "before-submit")
        {
            Console.WriteLine(JsonSerializer.Serialize(new { outcome = "not-submitted", reason = "injected failure before invocation" })); return;
        }
        var command = string.Join(' ', new[] { "target", args[2] }.Concat(Payload).Select(Quote));
        // Exceptions during/after COM invocation are ambiguous. Never retry.
        broker.Execute(Executable, command);
        Console.WriteLine(JsonSerializer.Serialize(new { outcome = scenario == "lost-reply" ? "ambiguous-injected-after-invocation" : "submitted-not-ready", broker = broker.ProcessId, immediateFlags = flags }));
        Console.ReadLine();
    }

    private static async Task TargetAsync(string[] args)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(deadline.Token);
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
        using var self = Process.GetCurrentProcess();
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { pid = self.Id, parent = Native.Parent(self), session = self.SessionId, identity = Native.Identity(self), arguments = args[2..] }));
        while (await reader.ReadLineAsync(deadline.Token) is { } line)
        {
            if (line == "quit") return;
            if (line == "ping") await writer.WriteLineAsync("pong");
        }
    }

    // Windows CRT/.NET argv encoding; ShellExecute receives a single argument string.
    private static string Quote(string value)
    {
        var result = new StringBuilder("\""); var slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? 2 * slashes + 1 : slashes); result.Append(c); slashes = 0;
        }
        return result.Append('\\', 2 * slashes).Append('"').ToString();
    }
}
