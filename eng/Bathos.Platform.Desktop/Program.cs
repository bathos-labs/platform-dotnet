using Bathos.Platform;
using Bathos.Platform.Desktop;
using Bathos.Platform.Linux;
using Bathos.Platform.MacOS;
using Bathos.Platform.Windows;

// Manual desktop validation only. No command chooses an application on the user's behalf.
try
{
    var platform = PlatformProviders.SelectCurrent(new MacOSPlatformProvider(), new WindowsPlatformProvider(), new LinuxPlatformProvider());
    if (args is ["reveal", var path])
    {
        await (platform.FileRevealer ?? throw new PlatformNotSupportedException()).RevealAsync(path);
        Console.WriteLine("Reveal request submitted. Visually confirm the selected item in its containing folder.");
    }
    else if (args is ["open", var file, var kind, var value])
    {
        var target = kind switch
        {
            "executable" => ApplicationTarget.Executable(value),
            "bundle" => ApplicationTarget.MacOSBundle(value),
            "desktop-entry" => ApplicationTarget.LinuxDesktopEntry(value),
            _ => throw new ArgumentException("Use executable, bundle, or desktop-entry as the target kind.")
        };
        await (platform.FileInvoker ?? throw new PlatformNotSupportedException()).OpenAsync(file, target);
        Console.WriteLine("File handed off. Confirm the document opens in the selected application.");
    }
    else if (args is ["find", var identifier])
    {
        IReadOnlyList<ApplicationTarget> targets;
        if (OperatingSystem.IsWindows()) targets = WindowsApplicationDiscovery.FindByExecutableName(identifier);
        else if (OperatingSystem.IsMacOS()) targets = MacOSApplicationDiscovery.FindByBundleIdentifier(identifier);
        else if (OperatingSystem.IsLinux()) targets = LinuxApplicationDiscovery.FindByDesktopId(identifier);
        else throw new PlatformNotSupportedException();
        foreach (var target in targets) Console.WriteLine($"{target.Kind}: {target.Value}");
        if (targets.Count == 0) Console.WriteLine("No usable registration found.");
    }
    else
    {
        Console.Error.WriteLine("Use reveal <absolute-path>, open <absolute-file> <executable|bundle|desktop-entry> <target>, or find <native-identifier>.");
        return 2;
    }
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
