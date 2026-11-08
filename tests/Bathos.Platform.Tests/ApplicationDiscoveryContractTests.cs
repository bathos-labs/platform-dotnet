using Bathos.Platform.Desktop;
using Xunit;

namespace Bathos.Platform.Tests;

[Trait("Validation", "Portable")]
public sealed class ApplicationDiscoveryContractTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData("label\0suffix")]
    public void CandidateRejectsUnusableLabels(string label)
    {
        Assert.Throws<ArgumentException>(() => new ApplicationCandidate(ApplicationTarget.LinuxDesktopEntry("owned.desktop"), label));
    }

    [Fact]
    public void SameLabelDoesNotCollapseDifferentTargets()
    {
        var first = new ApplicationCandidate(ApplicationTarget.LinuxDesktopEntry("first.desktop"), "Editor");
        var second = new ApplicationCandidate(ApplicationTarget.LinuxDesktopEntry("second.desktop"), "Editor");
        Assert.NotEqual(first, second);
        Assert.Equal(first, new ApplicationCandidate(first.Target, first.DisplayName));
    }
}
