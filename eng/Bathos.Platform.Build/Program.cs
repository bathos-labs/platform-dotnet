using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;

var root = Directory.GetCurrentDirectory();
if (!File.Exists(Path.Combine(root, "Bathos.Platform.slnx"))) throw new InvalidOperationException("Run from the repository root.");
var version = XDocument.Load(Path.Combine(root, "Directory.Build.props")).Descendants("Version").Single().Value;
if (args is ["verify-tag", var tag])
{
    if (tag != "v" + version) throw new InvalidOperationException($"Release tag must be v{version}.");
    return;
}
if (args is not ["verify-packages", var feedArgument]) throw new ArgumentException("Use verify-packages <feed> or verify-tag <tag>.");
var feed = Path.GetFullPath(feedArgument);
string[] names = ["Bathos.Platform", "Bathos.Platform.MacOS", "Bathos.Platform.Windows", "Bathos.Platform.Linux"];
foreach (var name in names)
{
    using var archive = ZipFile.OpenRead(Path.Combine(feed, $"{name}.{version}.nupkg"));
    using var stream = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
    var spec = XDocument.Load(stream);
    var license = spec.Descendants().Single(element => element.Name.LocalName == "license");
    if ((string?)license.Attribute("type") != "expression" || license.Value != "MIT" || archive.GetEntry("LICENSE") is null)
        throw new InvalidOperationException($"{name} must carry MIT metadata and license text.");
    var dependencies = spec.Descendants().Where(element => element.Name.LocalName == "dependency")
        .Select(element => (string?)element.Attribute("id")).ToArray();
    if (name == "Bathos.Platform" ? dependencies.Length != 0 : dependencies.Length != 1 || dependencies[0] != "Bathos.Platform")
        throw new InvalidOperationException($"{name} has unexpected package dependencies.");
}
var scratch = Directory.CreateTempSubdirectory("bathos-package-consumer-");
try
{
    foreach (var file in Directory.GetFiles(Path.Combine(root, "eng", "package-consumer")))
        File.Copy(file, Path.Combine(scratch.FullName, Path.GetFileName(file)));
    File.Copy(Path.Combine(root, "global.json"), Path.Combine(scratch.FullName, "global.json"));
    var configuration = new XDocument(new XElement("configuration",
        new XElement("packageSources", new XElement("clear"), new XElement("add", new XAttribute("key", "local"), new XAttribute("value", feed)))));
    configuration.Save(Path.Combine(scratch.FullName, "NuGet.Config"));
    var provider = OperatingSystem.IsMacOS() ? "MacOS" : OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux"
        : throw new PlatformNotSupportedException();
    await Run("restore", "--configfile", "NuGet.Config", "--packages", Path.Combine(scratch.FullName, "cache"),
        $"-p:BathosVersion={version}", $"-p:Provider={provider}");
    await Run("run", "--configuration", "Release", "--no-restore", $"-p:BathosVersion={version}", $"-p:Provider={provider}");

    async Task Run(params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = scratch.FullName, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not launch dotnet.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException($"Package consumer exited with {process.ExitCode}.");
    }
}
finally { scratch.Delete(true); }
Console.WriteLine("MIT metadata, package isolation, and single-provider native consumption passed.");
