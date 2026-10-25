using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;

var root = Directory.GetCurrentDirectory();
if (!File.Exists(Path.Combine(root, "Bathos.Platform.slnx"))) throw new InvalidOperationException("Run from the repository root.");
var version = XDocument.Load(Path.Combine(root, "Directory.Build.props")).Descendants("Version").Single().Value;
if (args is ["clean-test-results"])
{
    foreach (var path in new[] { "artifacts/test-results", "artifacts/coverage" })
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    return;
}
if (args.Length is 2 or 3 && args[0] == "verify-tag" && (args.Length == 2 || args[2] == "--remote"))
{
    try
    {
        var tag = args[1];
        if (tag != "v" + version) throw new InvalidOperationException($"Release tag must be v{version}.");
        var changelog = File.ReadAllText(Path.Combine(root, "CHANGELOG.md"));
        var entry = System.Text.RegularExpressions.Regex.Match(changelog,
            $@"^## \[{System.Text.RegularExpressions.Regex.Escape(version)}\] - (\d{{4}}-\d{{2}}-\d{{2}})$",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        if (!entry.Success || !DateOnly.TryParseExact(entry.Groups[1].Value, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
            throw new InvalidOperationException($"Prepare a dated CHANGELOG.md entry for {version} before tagging.");
        await ReleasePolicy.VerifyAsync(root, version, tag, remote: args.Length == 3);
    }
    catch (InvalidOperationException error)
    {
        Console.Error.WriteLine(error.Message);
        Environment.ExitCode = 1;
    }
    return;
}
if (args is ["verify-coverage", var results, var output])
{
    CoverageEvidence.Verify(results, output);
    return;
}
if (args is not ["verify-packages", ..] || args.Length is < 2 or > 3 ||
    (args.Length == 3 && args[2] != "--native-aot"))
    throw new ArgumentException("Use verify-packages <feed> [--native-aot] or verify-tag <tag> [--remote].");
var feedArgument = args[1];
var feed = Path.GetFullPath(feedArgument);
string[] names = ["Bathos.Platform", "Bathos.Platform.MacOS", "Bathos.Platform.Windows", "Bathos.Platform.Linux"];
var readme = File.ReadAllText(Path.Combine(root, "README.md"));
var repository = "https://github.com/bathos-labs/platform-dotnet";
var expectedReadme = readme.Replace($"{repository}/blob/master/", $"{repository}/blob/v{version}/", StringComparison.Ordinal);
var installationCommands = readme.Split('\n').Select(line => line.Trim())
    .Where(line => line.StartsWith("dotnet add package ", StringComparison.Ordinal))
    .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToArray();
if (installationCommands.Length == 0 || installationCommands.Any(command =>
    command.Length != 4 || !names.Contains(command[3], StringComparer.Ordinal)))
    throw new InvalidOperationException("README installation commands must use versionless stable package names without flags.");
await ApiCompatibilityEvidence.VerifyAsync(root, feed, version);
foreach (var name in names)
{
    using var archive = ZipFile.OpenRead(Path.Combine(feed, $"{name}.{version}.nupkg"));
    using var stream = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
    var spec = XDocument.Load(stream);
    var license = spec.Descendants().Single(element => element.Name.LocalName == "license");
    if ((string?)license.Attribute("type") != "expression" || license.Value != "MIT" || archive.GetEntry("LICENSE") is null)
        throw new InvalidOperationException($"{name} must carry MIT metadata and license text.");
    var assemblyPath = $"lib/net10.0/{name}.dll";
    if (archive.GetEntry(assemblyPath) is null || archive.GetEntry("README.md") is null)
        throw new InvalidOperationException($"{name} must contain its assembly and package README.");
    using var readmeStream = archive.GetEntry("README.md")?.Open()
        ?? throw new InvalidOperationException($"{name} must contain its package README.");
    using var readmeReader = new StreamReader(readmeStream);
    if (await readmeReader.ReadToEndAsync() != expectedReadme)
        throw new InvalidOperationException($"{name} must preserve the README and pin documentation links to v{version}.");
    var documentationEntry = archive.GetEntry($"lib/net10.0/{name}.xml")
        ?? throw new InvalidOperationException($"{name} must ship matching XML documentation beside its assembly.");
    using var documentationStream = documentationEntry.Open();
    var documentation = XDocument.Load(documentationStream);
    if (documentation.Root?.Element("assembly")?.Element("name")?.Value != name)
        throw new InvalidOperationException($"{name} XML documentation names the wrong assembly.");
    var members = documentation.Root.Element("members")?.Elements("member").ToArray()
        ?? throw new InvalidOperationException($"{name} XML documentation has no members.");
    if (members.Length == 0 || members.Any(member => string.IsNullOrWhiteSpace(member.Element("summary")?.Value)) ||
        documentation.Descendants().Any(element => element.Name.LocalName is "include" or "inheritdoc"))
        throw new InvalidOperationException($"{name} must ship expanded, nonempty API summaries, without unresolved documentation inheritance.");
    string[] expectedMembers = name switch
    {
        "Bathos.Platform" => ["T:Bathos.Platform.IPlatformProvider", "T:Bathos.Platform.Files.FilePublication",
            "M:Bathos.Platform.Desktop.IFileInvoker.OpenAsync(System.String,Bathos.Platform.Desktop.ApplicationTarget,System.Threading.CancellationToken)"],
        "Bathos.Platform.MacOS" => ["T:Bathos.Platform.MacOS.MacOSPlatformProvider",
            "M:Bathos.Platform.MacOS.MacOSFileInvoker.OpenAsync(System.String,Bathos.Platform.Desktop.ApplicationTarget,System.Threading.CancellationToken)"],
        "Bathos.Platform.Windows" => ["T:Bathos.Platform.Windows.WindowsPlatformProvider",
            "M:Bathos.Platform.Windows.WindowsFileInvoker.OpenAsync(System.String,Bathos.Platform.Desktop.ApplicationTarget,System.Threading.CancellationToken)"],
        _ => ["T:Bathos.Platform.Linux.LinuxPlatformProvider",
            "M:Bathos.Platform.Linux.LinuxFileInvoker.OpenAsync(System.String,Bathos.Platform.Desktop.ApplicationTarget,System.Threading.CancellationToken)"]
    };
    foreach (var expected in expectedMembers)
        if (!members.Any(member => (string?)member.Attribute("name") == expected))
            throw new InvalidOperationException($"{name} is missing documentation for {expected}.");
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
    // Only these candidate packages may satisfy the versionless README installation commands.
    var candidateFeed = Directory.CreateDirectory(Path.Combine(scratch.FullName, "feed")).FullName;
    foreach (var name in names)
        File.Copy(Path.Combine(feed, $"{name}.{version}.nupkg"), Path.Combine(candidateFeed, $"{name}.{version}.nupkg"));
    var configuration = new XDocument(new XElement("configuration",
        new XElement("packageSources", new XElement("clear"), new XElement("add", new XAttribute("key", "local"), new XAttribute("value", candidateFeed)))));
    configuration.Save(Path.Combine(scratch.FullName, "NuGet.Config"));
    var provider = OperatingSystem.IsMacOS() ? "MacOS" : OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux"
        : throw new PlatformNotSupportedException();
    await Run("restore", "--configfile", "NuGet.Config", "--packages", Path.Combine(scratch.FullName, "cache"),
        $"-p:BathosVersion={version}", $"-p:Provider={provider}");
    await Run("run", "--configuration", "Release", "--no-restore", $"-p:BathosVersion={version}", $"-p:Provider={provider}");
    await PublishedConsumerEvidence.VerifyAsync(root, candidateFeed, version, provider, nativeAot: args.Length == 3);

    // The quick start must compile with exactly its documented dependencies, before guide examples add providers.
    var consumerProject = XDocument.Load(Path.Combine(scratch.FullName, "Consumer.csproj"));
    consumerProject.Root?.Elements("ItemGroup").Remove();
    consumerProject.Save(Path.Combine(scratch.FullName, "Consumer.csproj"));
    Console.WriteLine("Validating README installation against the isolated candidate feed, not the published NuGet feed.");
    foreach (var command in installationCommands)
    {
        var arguments = command.Skip(1).Concat(["--source", candidateFeed, "--package-directory", Path.Combine(scratch.FullName, "cache")]);
        // A prerelease development candidate needs opt-in only within this isolated validation.
        if (version.Contains('-')) arguments = arguments.Append("--prerelease");
        await Run(arguments.ToArray());
    }
    consumerProject = XDocument.Load(Path.Combine(scratch.FullName, "Consumer.csproj"));
    if (consumerProject.Descendants("PackageReference").Any(reference => (string?)reference.Attribute("Version") != version))
        throw new InvalidOperationException("README installation must resolve exactly the candidate package versions.");
    var readmeExamples = CSharpExamples(readme).ToArray();
    if (readmeExamples.Length != 1) throw new InvalidOperationException("Expected one executable README quick start.");
    File.WriteAllText(Path.Combine(scratch.FullName, "Program.cs"), readmeExamples[0]);
    await Run("restore", "--configfile", "NuGet.Config", "--packages", Path.Combine(scratch.FullName, "cache"));
    await Run("build", "--configuration", "Release", "--no-restore");
    if (provider is "MacOS" or "Windows" or "Linux")
    {
        await Run("run", "--configuration", "Release", "--no-build", "--no-restore");
        await Run("run", "--configuration", "Release", "--no-build", "--no-restore");
        var directory = Path.Combine(scratch.FullName, "published-files");
        var first = File.ReadAllBytes(Path.Combine(directory, "Greeting.txt"));
        var second = File.ReadAllBytes(Path.Combine(directory, "Greeting (2).txt"));
        if (!first.AsSpan().SequenceEqual("Hello from .NET!\n"u8) || !first.SequenceEqual(second) ||
            Directory.GetFiles(directory).Length != 2)
            throw new InvalidOperationException("README publication example did not preserve both complete outputs.");
    }

    // Guide examples cover capabilities from every provider independently of the README's install instructions.
    consumerProject.Root?.Elements("ItemGroup").Remove();
    consumerProject.Root?.Add(new XElement("ItemGroup", names.Select(name =>
        new XElement("PackageReference", new XAttribute("Include", name), new XAttribute("Version", version)))));
    consumerProject.Save(Path.Combine(scratch.FullName, "Consumer.csproj"));
    var usageExamples = CSharpExamples(File.ReadAllText(Path.Combine(root, "docs", "usage.md"))).ToArray();
    if (usageExamples.Length == 0) throw new InvalidOperationException("Expected integration guide examples.");
    for (var index = 0; index < usageExamples.Length; index++)
        File.WriteAllText(Path.Combine(scratch.FullName, $"GuideExample{index}.cs"), usageExamples[index]);
    await Run("restore", "--configfile", "NuGet.Config", "--packages", Path.Combine(scratch.FullName, "cache"));
    await Run("build", "--configuration", "Release", "--no-restore");

    if (provider == "Windows")
    {
        // WPF is a host example, so compile it separately from the portable integration guide.
        foreach (var file in Directory.GetFiles(scratch.FullName, "*.cs")) File.Delete(file);
        var properties = consumerProject.Root?.Element("PropertyGroup")
            ?? throw new InvalidOperationException("Consumer project has no properties.");
        properties.SetElementValue("TargetFramework", "net10.0-windows");
        properties.SetElementValue("OutputType", "Library");
        properties.SetElementValue("UseWPF", "true");
        consumerProject.Root?.Elements("ItemGroup").Remove();
        consumerProject.Root?.Add(new XElement("ItemGroup", names.Where(name => name is "Bathos.Platform" or "Bathos.Platform.Windows").Select(name =>
            new XElement("PackageReference", new XAttribute("Include", name), new XAttribute("Version", version)))));
        consumerProject.Save(Path.Combine(scratch.FullName, "Consumer.csproj"));
        var wpfExamples = CSharpExamples(File.ReadAllText(Path.Combine(root, "docs", "examples", "wpf-dispatcher.md"))).ToArray();
        if (wpfExamples.Length == 0) throw new InvalidOperationException("Expected WPF dispatcher examples.");
        for (var index = 0; index < wpfExamples.Length; index++)
            File.WriteAllText(Path.Combine(scratch.FullName, $"WpfExample{index}.cs"), wpfExamples[index]);
        await Run("restore", "--configfile", "NuGet.Config", "--packages", Path.Combine(scratch.FullName, "cache"));
        await Run("build", "--configuration", "Release", "--no-restore");
        Console.WriteLine("WPF dispatcher and window examples compiled against the Windows packages.");
    }

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
Console.WriteLine("MIT metadata, expanded XML documentation, package isolation, native consumption, and Markdown examples passed.");

static IEnumerable<string> CSharpExamples(string markdown)
{
    foreach (var block in markdown.Split("```csharp", StringSplitOptions.None).Skip(1))
    {
        var end = block.IndexOf("```", StringComparison.Ordinal);
        if (end < 0) throw new InvalidOperationException("Unterminated C# documentation example.");
        yield return block[..end].Trim() + Environment.NewLine;
    }
}
