using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;

internal static class ApiCompatibilityEvidence
{
    internal static async Task VerifyAsync(string root, string feed, string version)
    {
        var baseline = XDocument.Load(Path.Combine(root, "eng", "Bathos.Package.props"))
            .Descendants("PackageValidationBaselineVersion").Single().Value;
        var scratch = Directory.CreateTempSubdirectory("bathos-api-compatibility-");
        try
        {
            File.Copy(Path.Combine(root, "global.json"), Path.Combine(scratch.FullName, "global.json"));
            var baselineDirectory = CopyFixture("consumer", "baseline");
            var currentDirectory = CopyFixture("consumer", "current");
            var runnerDirectory = CopyFixture("runner", "runner");
            var baselineConfig = Path.Combine(scratch.FullName, "baseline.config");
            File.Copy(Path.Combine(root, "NuGet.Config"), baselineConfig);
            var candidateConfig = Path.Combine(scratch.FullName, "candidate.config");
            new XDocument(new XElement("configuration", new XElement("packageSources", new XElement("clear"),
                new XElement("add", new XAttribute("key", "candidate"), new XAttribute("value", feed))))).Save(candidateConfig);
            var baselineCache = Path.Combine(scratch.FullName, "baseline-cache");
            var candidateCache = Path.Combine(scratch.FullName, "candidate-cache");

            await RunAsync(baselineDirectory, "restore", "--locked-mode", "--configfile", baselineConfig, "--packages", baselineCache, $"-p:BathosVersion={baseline}");
            await RunAsync(baselineDirectory, "build", "-c", "Release", "--no-restore", $"-p:BathosVersion={baseline}");
            var releasedAssembly = Path.Combine(baselineDirectory, "bin", "Release", "net10.0", "ReleasedConsumer.dll");
            var releasedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(releasedAssembly)));

            // Rebuild the same source against candidate packages to catch source compatibility, including named arguments.
            await RunAsync(currentDirectory, "restore", "--force-evaluate", "--configfile", candidateConfig, "--packages", candidateCache, $"-p:BathosVersion={version}");
            await RunAsync(currentDirectory, "build", "-c", "Release", "--no-restore", $"-p:BathosVersion={version}");
            await RunAsync(runnerDirectory, "restore", "--configfile", candidateConfig, "--packages", candidateCache, $"-p:BathosVersion={version}");
            await RunAsync(runnerDirectory, "build", "-c", "Release", "--no-restore", $"-p:BathosVersion={version}");

            string[] names = ["Bathos.Platform", "Bathos.Platform.Windows", "Bathos.Platform.MacOS", "Bathos.Platform.Linux"];
            var hashes = names.Select(name =>
            {
                using var package = ZipFile.OpenRead(Path.Combine(feed, $"{name}.{version}.nupkg"));
                using var assembly = (package.GetEntry($"lib/net10.0/{name}.dll")
                    ?? throw new InvalidOperationException($"Missing candidate assembly for {name}.")).Open();
                return Convert.ToHexString(SHA256.HashData(assembly));
            }).ToArray();
            await RunAsync(runnerDirectory, [Path.Combine(runnerDirectory, "bin", "Release", "net10.0", "Runner.dll"), .. hashes, releasedHash]);
            Console.WriteLine($"Source and binary compatibility fixtures passed against stable {baseline}.");

            string CopyFixture(string source, string destination)
            {
                var directory = Directory.CreateDirectory(Path.Combine(scratch.FullName, destination)).FullName;
                foreach (var file in Directory.GetFiles(Path.Combine(root, "eng", "api-compatibility", source)))
                    File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
                return directory;
            }
        }
        finally { scratch.Delete(recursive: true); }
    }

    private static async Task RunAsync(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not launch API compatibility check.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException($"API compatibility check exited with {process.ExitCode}.");
    }
}
