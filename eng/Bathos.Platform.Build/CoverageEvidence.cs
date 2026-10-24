using System.Globalization;
using System.Xml.Linq;

internal static class CoverageEvidence
{
    internal static void Verify(string results, string output)
    {
        var reports = Directory.GetFiles(results, "coverage.cobertura.*.xml", SearchOption.AllDirectories);
        if (reports.Length != 1) throw new InvalidDataException($"Expected one fresh coverage report in {results}; found {reports.Length}.");
        var report = XDocument.Load(reports[0]);
        var coverage = report.Root ?? throw new InvalidDataException("Coverage report has no root.");
        if (coverage.Name != "coverage" || Count(coverage, "lines-covered") == 0 || Count(coverage, "branches-valid") == 0)
            throw new InvalidDataException("Coverage report has no executed lines or instrumented branches.");
        string[] expected = ["Bathos.Platform", "Bathos.Platform.Linux", "Bathos.Platform.MacOS", "Bathos.Platform.Windows"];
        var packages = coverage.Element("packages")?.Elements("package").ToArray() ?? [];
        if (!packages.Select(package => (string?)package.Attribute("name")).Order().SequenceEqual(expected))
            throw new InvalidDataException("Coverage must include exactly the four shipping assemblies, including unexecuted native code.");
        foreach (var package in packages)
        {
            var classes = package.Descendants("class").ToArray();
            if (classes.Length == 0 || !classes.Any(type => type.Descendants("line").Any()))
                throw new InvalidDataException($"No instrumented source in {package.Attribute("name")}.");
            foreach (var type in classes)
            {
                var filename = ((string?)type.Attribute("filename") ?? "").Replace('\\', '/');
                // Coverlet makes paths relative to its common source root (src). Normalize only for upload;
                // retain the untouched collector report alongside TRX for independent inspection.
                var relative = "src/" + filename;
                if (!expected.Any(name => filename.StartsWith(name + "/", StringComparison.Ordinal)) || !File.Exists(relative))
                    throw new InvalidDataException($"Unexpected coverage source: {filename}");
                type.SetAttributeValue("filename", relative);
            }
        }
        coverage.Element("sources")?.ReplaceWith(new XElement("sources", new XElement("source", ".")));
        var directory = Path.GetDirectoryName(Path.GetFullPath(output))
            ?? throw new ArgumentException("Coverage output requires a parent directory.", nameof(output));
        Directory.CreateDirectory(directory);
        report.Save(output);
        Console.WriteLine($"Coverage evidence: {Count(coverage, "lines-covered")}/{Count(coverage, "lines-valid")} lines; " +
            $"{Count(coverage, "branches-covered")}/{Count(coverage, "branches-valid")} branches. This host only; no acceptance threshold.");
    }

    private static int Count(XElement coverage, string attribute) =>
        int.Parse((string?)coverage.Attribute(attribute) ?? throw new InvalidDataException($"Missing {attribute}."), CultureInfo.InvariantCulture);
}
