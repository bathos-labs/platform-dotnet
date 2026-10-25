using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;

internal static class PublishedConsumerEvidence
{
    internal static async Task VerifyAsync(string root, string feed, string version, string provider, bool nativeAot)
    {
        var scratch = Directory.CreateTempSubdirectory("bathos-published-consumer-");
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(scratch.FullName, "project")).FullName;
            foreach (var file in Directory.GetFiles(Path.Combine(root, "eng", "package-consumer")))
                File.Copy(file, Path.Combine(project, Path.GetFileName(file)));
            File.Copy(Path.Combine(root, "global.json"), Path.Combine(scratch.FullName, "global.json"));
            var candidateFeed = Directory.CreateDirectory(Path.Combine(scratch.FullName, "feed")).FullName;
            string[] packages = ["Bathos.Platform", $"Bathos.Platform.{provider}"];
            foreach (var package in packages)
                File.Copy(Path.Combine(feed, $"{package}.{version}.nupkg"), Path.Combine(candidateFeed, $"{package}.{version}.nupkg"));
            // SDK runtime/linker/compiler packs may come from NuGet; Bathos must come from this pack operation.
            var configuration = new XDocument(new XElement("configuration",
                new XElement("packageSources", new XElement("clear"),
                    new XElement("add", new XAttribute("key", "candidate"), new XAttribute("value", candidateFeed)),
                    new XElement("add", new XAttribute("key", "nuget.org"), new XAttribute("value", "https://api.nuget.org/v3/index.json"))),
                new XElement("packageSourceMapping", new XElement("clear"),
                    new XElement("packageSource", new XAttribute("key", "candidate"), packages.Select(package =>
                        new XElement("package", new XAttribute("pattern", package)))),
                    new XElement("packageSource", new XAttribute("key", "nuget.org"),
                        new XElement("package", new XAttribute("pattern", "*"))))));
            configuration.Save(Path.Combine(scratch.FullName, "NuGet.Config"));
            var os = provider switch { "MacOS" => "osx", "Windows" => "win", "Linux" => "linux", _ => throw new PlatformNotSupportedException() };
            var rid = $"{os}-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";
            string[] modes = nativeAot ? ["self-contained", "single-file", "trimmed", "native-aot"] : ["self-contained", "single-file", "trimmed"];
            foreach (var mode in modes)
            {
                var elapsed = Stopwatch.StartNew();
                foreach (var directory in new[] { "bin", "obj" })
                    if (Directory.Exists(Path.Combine(project, directory))) Directory.Delete(Path.Combine(project, directory), recursive: true);
                var output = Path.Combine(scratch.FullName, "published", mode);
                Console.WriteLine($"Publishing isolated {provider} consumer: {mode}, {rid}.");
                await Run("dotnet", project, ["publish", "Consumer.csproj", "--configuration", "Release", "--runtime", rid,
                    "--self-contained", "true", "--output", output,
                    $"-p:BathosVersion={version}", $"-p:Provider={provider}",
                    $"-p:RestorePackagesPath={Path.Combine(scratch.FullName, "cache")}",
                    $"-p:PublishSingleFile={mode == "single-file"}", "-p:IncludeNativeLibrariesForSelfExtract=true",
                    $"-p:PublishTrimmed={mode is "trimmed" or "native-aot"}", "-p:TrimMode=full",
                    $"-p:PublishAot={mode == "native-aot"}", "-p:EnableSingleFileAnalyzer=true",
                    "-p:ILLinkTreatWarningsAsErrors=true", "-p:IlcTreatWarningsAsErrors=true",
                    "-p:TrimmerSingleWarn=false"]);
                using (var assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, "obj", "project.assets.json"))))
                {
                    var libraries = assets.RootElement.GetProperty("libraries").EnumerateObject()
                        .Select(library => library.Name).Where(name => name.StartsWith("Bathos.Platform", StringComparison.Ordinal)).Order(StringComparer.Ordinal);
                    if (!libraries.SequenceEqual(packages.Select(package => $"{package}/{version}").Order(StringComparer.Ordinal)))
                        throw new InvalidOperationException($"{mode} resolved unexpected Bathos packages.");
                }
                var looseProviders = Directory.GetFiles(output, "Bathos.Platform.*.dll", SearchOption.AllDirectories);
                if (looseProviders.Any(path => Path.GetFileNameWithoutExtension(path) != $"Bathos.Platform.{provider}"))
                    throw new InvalidOperationException($"{mode} shipped an unselected provider.");
                if (mode == "single-file" && Directory.GetFiles(output, "*", SearchOption.AllDirectories)
                    .Any(path => Path.GetExtension(path) is ".dll" or ".so" or ".dylib"))
                    throw new InvalidOperationException("Single-file deployment left loose managed or runtime libraries.");
                if (mode is "self-contained" or "trimmed")
                {
                    using var runtime = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "Consumer.runtimeconfig.json")));
                    if (!runtime.RootElement.GetProperty("runtimeOptions").TryGetProperty("includedFrameworks", out _))
                        throw new InvalidOperationException($"{mode} requires an installed shared framework.");
                }
                var executable = Path.Combine(output, OperatingSystem.IsWindows() ? "Consumer.exe" : "Consumer");
                var workingDirectory = Directory.CreateDirectory(Path.Combine(scratch.FullName, "run", mode)).FullName;
                await Run(executable, workingDirectory, mode == "native-aot" ? ["--native-aot"] : [],
                    Path.Combine(scratch.FullName, "bundle-extraction"));
                Console.WriteLine($"Published consumer passed: {mode}, {rid} ({elapsed.Elapsed.TotalSeconds:F1}s).");
            }
        }
        finally { scratch.Delete(recursive: true); }
    }

    private static async Task Run(string executable, string directory, string[] arguments, string? extractionDirectory = null)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (extractionDirectory is not null) start.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = extractionDirectory;
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not launch {executable}.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException($"Published consumer command failed ({process.ExitCode}): {executable} {string.Join(' ', arguments)}");
    }
}
