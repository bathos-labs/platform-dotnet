using System.IO.Pipes;
using System.Text.Json;
using Bathos.Platform.Linux;

// Engineering-only owned test application: never opens an editor or a desktop window.
try
{
    var configuration = JsonSerializer.Deserialize<ProbeConfiguration>(File.ReadAllText(args.Single()))
        ?? throw new InvalidDataException("Missing probe configuration.");
    if (configuration.Mode == "desktop")
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var id = configuration.DesktopId ?? throw new InvalidDataException("Missing desktop ID.");
        var path = configuration.Document ?? throw new InvalidDataException("Missing document.");
        var target = LinuxApplicationDiscovery.FindByDesktopId(id).Single();
        await new LinuxFileInvoker().OpenAsync(path, target);
        return 0;
    }
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await using var pipe = new NamedPipeClientStream(".", configuration.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    await pipe.ConnectAsync(deadline.Token);
    using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
    using var reader = new StreamReader(pipe, leaveOpen: true);
    await writer.WriteLineAsync(JsonSerializer.Serialize(new ProbeResult(Environment.ProcessId, args)).AsMemory(), deadline.Token);
    if (await reader.ReadLineAsync(deadline.Token) != "finish") throw new InvalidDataException("Missing owner completion signal.");
    await writer.WriteLineAsync("finished".AsMemory(), deadline.Token);
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }

internal sealed record ProbeConfiguration(string Mode, string PipeName, string? Document = null, string? DesktopId = null);
internal sealed record ProbeResult(int ProcessId, string[] Arguments);
