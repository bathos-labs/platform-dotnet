using System.IO.Pipes;
using System.Text.Json;
using Bathos.Platform;
using Bathos.Platform.Files;
using Bathos.Platform.Linux;
using Bathos.Platform.MacOS;
using Bathos.Platform.Windows;

// Engineering-only owned test application: never opens an editor or a desktop window.
try
{
    var configuration = JsonSerializer.Deserialize<ProbeConfiguration>(File.ReadAllText(args.Single()))
        ?? throw new InvalidDataException("Missing probe configuration.");
    if (configuration.Mode == "lease-terminal-fixture")
    {
        using var master = UnixLeaseProbe.CreateTerminal(out var path, out var slave);
        using var ownedSlave = slave;
        Console.WriteLine(path);
        if (await Console.In.ReadLineAsync() != "finish") throw new InvalidDataException("Missing terminal completion signal.");
        return 0;
    }
    if (configuration.Mode == "reject-terminal-lease")
    {
        var provider = PlatformProviders.SelectCurrent(new MacOSPlatformProvider(), new WindowsPlatformProvider(), new LinuxPlatformProvider());
        UnixLeaseProbe.RejectTerminal(provider.FileLeases ?? throw new InvalidOperationException("Missing lease capability."),
            configuration.Document ?? throw new InvalidDataException("Missing owned terminal path."));
        Console.WriteLine("rejected-without-terminal");
        return 0;
    }
    if (configuration.Mode == "reject-file-lease")
    {
        var provider = PlatformProviders.SelectCurrent(new MacOSPlatformProvider(), new WindowsPlatformProvider(), new LinuxPlatformProvider());
        try
        {
            using var lease = (provider.FileLeases ?? throw new InvalidOperationException("Missing lease capability."))
                .TryAcquire(configuration.Document ?? throw new InvalidDataException("Missing lock path."), FileLeaseMode.Exclusive);
            throw new InvalidOperationException("A special entry was admitted for leasing.");
        }
        catch (IOException) { Console.WriteLine("rejected"); }
        if (await Console.In.ReadLineAsync() != "finish") throw new InvalidDataException("Missing lease completion signal.");
        return 0;
    }
    if (configuration.Mode == "file-lease")
    {
        var provider = PlatformProviders.SelectCurrent(new MacOSPlatformProvider(), new WindowsPlatformProvider(), new LinuxPlatformProvider());
        var path = configuration.Document ?? throw new InvalidDataException("Missing lock path.");
        using var lease = (provider.FileLeases ?? throw new InvalidOperationException("Missing lease capability."))
            .TryAcquire(path, configuration.LeaseMode);
        Console.WriteLine(lease is null ? "contended" : "acquired");
        if (await Console.In.ReadLineAsync() != "finish") throw new InvalidDataException("Missing lease completion signal.");
        return 0;
    }
    if (configuration.Mode == "file-lease-owner")
    {
        var provider = PlatformProviders.SelectCurrent(new MacOSPlatformProvider(), new WindowsPlatformProvider(), new LinuxPlatformProvider());
        using var lease = (provider.FileLeases ?? throw new InvalidOperationException("Missing lease capability."))
            .TryAcquire(configuration.Document ?? throw new InvalidDataException("Missing lock path."), configuration.LeaseMode)
            ?? throw new InvalidOperationException("The owned parent did not acquire the lease.");
        var descendantConfiguration = Path.Combine(Path.GetDirectoryName(args.Single()) ?? throw new InvalidDataException("Missing fixture directory."), "descendant.json");
        await File.WriteAllTextAsync(descendantConfiguration, JsonSerializer.Serialize(configuration with { Mode = "lease-descendant" }));
        using var descendant = LeaseDescendant.Start(descendantConfiguration);
        try
        {
            Console.WriteLine(descendant.Id);
            if (await Console.In.ReadLineAsync() != "finish") throw new InvalidDataException("Missing parent completion signal.");
        }
        finally
        {
            if (!descendant.HasExited) descendant.Kill(entireProcessTree: true);
            await descendant.WaitForExitAsync(CancellationToken.None);
        }
        return 0;
    }
    if (configuration.Mode == "standard-directories")
    {
        var provider = PlatformProviders.SelectCurrent(new MacOSPlatformProvider(), new WindowsPlatformProvider(), new LinuxPlatformProvider());
        var roots = provider.Directories?.Discover() ?? throw new InvalidOperationException("Missing directory capability.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Roots = roots,
            RuntimeHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify),
            NativeCache = OperatingSystem.IsMacOS()
                ? Environment.GetFolderPath(Environment.SpecialFolder.InternetCache, Environment.SpecialFolderOption.DoNotVerify) : null,
        }));
        return 0;
    }
    if (configuration.Mode == "reject-special")
    {
        var provider = PlatformProviders.SelectCurrent(new MacOSPlatformProvider(), new WindowsPlatformProvider(), new LinuxPlatformProvider());
        var probe = provider.FileEntries ?? throw new InvalidOperationException("Missing inspection capability.");
        try { probe.RequireRegularFile(configuration.Document ?? throw new InvalidDataException("Missing entry.")); }
        catch (IOException) { return 0; }
        throw new InvalidOperationException("Inspection accepted a link or special entry.");
    }
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

internal sealed record ProbeConfiguration(string Mode, string PipeName, string? Document = null, string? DesktopId = null,
    FileLeaseMode LeaseMode = FileLeaseMode.Exclusive);
internal sealed record ProbeResult(int ProcessId, string[] Arguments);
