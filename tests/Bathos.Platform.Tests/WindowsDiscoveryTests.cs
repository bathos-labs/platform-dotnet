using Bathos.Platform.Windows;
using Microsoft.Win32;
using Xunit;

namespace Bathos.Platform.Tests;

[Trait("Validation", "Native")]
public sealed class WindowsDiscoveryTests
{
    [Theory]
    [InlineData("relative")]
    [InlineData("command")]
    [InlineData("directory")]
    [InlineData("missing")]
    [InlineData("non-string")]
    public void InvalidAppPathsRegistrationsAreOmittedWithoutGuessingATarget(string scenario)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("App Paths registration requires the Windows registry.");
            return;
        }
        var folder = Directory.CreateTempSubdirectory("bathos-app-paths-");
        var executableName = "bathos-" + Guid.NewGuid().ToString("N") + ".exe";
        var key = @"Software\Microsoft\Windows\CurrentVersion\App Paths\" + executableName;
        try
        {
            var executable = Path.Combine(folder.FullName, "registered.exe");
            File.WriteAllText(executable, "owned registration fixture");
            using (var registration = Registry.CurrentUser.CreateSubKey(key))
            {
                object value = scenario switch
                {
                    "relative" => "registered.exe",
                    "command" => "\"" + executable + "\" --open",
                    "directory" => folder.FullName,
                    "missing" => Path.Combine(folder.FullName, "missing.exe"),
                    _ => 42
                };
                registration.SetValue(null, value, scenario == "non-string" ? RegistryValueKind.DWord : RegistryValueKind.String);
            }
            Assert.Empty(WindowsApplicationDiscovery.FindByExecutableName(executableName));
            Assert.Equal("owned registration fixture", File.ReadAllText(executable));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKey(key, throwOnMissingSubKey: false);
            folder.Delete(true);
        }
    }
}
