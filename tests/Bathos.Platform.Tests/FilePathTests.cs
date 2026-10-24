using Bathos.Platform.Paths;
using Xunit;

namespace Bathos.Platform.Tests;

[Trait("Validation", "Portable")]
public sealed class FilePathTests
{
    private static NativeFilePaths Native => OperatingSystem.IsWindows() ? NativeFilePaths.Windows : NativeFilePaths.Posix;

    [Theory]
    [InlineData("image #日本%20.png")]
    [InlineData(" trailing space.png ")]
    [InlineData("a+b;@name.png")]
    [InlineData("\U0001F3A8 %FF %2F.png")]
    public void NativeRoundtripPreservesCharactersAndDoesNotRequireExistence(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "nested", "..", name);
        var normalized = Native.NormalizeAbsolute(path);
        Assert.Equal(Path.Combine(root, name), normalized);
        Assert.True(Native.TryFromFileUri(Native.ToFileUri(path), out var restored));
        Assert.Equal(normalized, restored);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void RootsAndDirectoryReferencesRetainTheirMeaning()
    {
        var root = Path.GetPathRoot(Path.GetTempPath()) ?? throw new InvalidOperationException();
        Assert.True(Native.TryFromFileUri(Native.ToFileUri(root, directory: true), out var restored));
        Assert.Equal(root, restored);
        Assert.EndsWith("/", Native.ToFileUri(Path.Combine(root, "folder"), directory: true), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => Native.NormalizeAbsolute("relative"));
        Assert.Throws<ArgumentException>(() => Native.NormalizeAbsolute(root + "bad\0name"));
    }

    [Theory]
    [InlineData("https://example.com/file")]
    [InlineData("file:///a%00b")]
    [InlineData("file:///a%2Fb")]
    [InlineData("file:///a?query")]
    [InlineData("file:///a#fragment")]
    [InlineData("relative")]
    [InlineData("file:///bad%zz")]
    [InlineData(" file:///leading")]
    public void UnsupportedUriEvidenceRemainsUnresolved(string uri) => Assert.False(Native.TryFromFileUri(uri, out _));

    [Fact]
    public void FileUriEncodingRejectsMalformedUnicodeInsteadOfReplacingIt()
    {
        var directory = Path.GetTempPath();
        foreach (var name in new[] { "\ud800", "\udc00", "a\ud800b" })
        {
            var exception = Assert.Throws<ArgumentException>(() => Native.ToFileUri(Path.Combine(directory, name)));
            Assert.Equal("path", exception.ParamName);
        }
    }

    [Fact]
    public void FileUriDecodingRejectsMalformedUnicodeInsteadOfReplacingIt()
    {
        var uriPrefix = Native.ToFileUri(Path.GetTempPath(), directory: true);
        foreach (var name in new[] { "\ud800", "\udc00", "a\ud800b" })
        {
            Assert.False(Native.TryFromFileUri(uriPrefix + name, out var restored));
            Assert.Null(restored);
        }
    }

    [Theory]
    [InlineData("%FF")]
    [InlineData("%C0%AF")]
    [InlineData("%E2%82")]
    [InlineData("%ED%A0%80")]
    [InlineData("%F4%90%80%80")]
    [InlineData("%C3x%A9")]
    public void FileUriConversionRejectsInvalidUtf8InsteadOfTreatingItAsALiteralName(string escapedName)
    {
        var uri = Native.ToFileUri(Path.GetTempPath(), directory: true) + escapedName;
        Assert.False(Native.TryFromFileUri(uri, out var restored));
        Assert.Null(restored);
    }

    [Fact]
    public void ForeignSyntaxRequiresItsHostAndDoesNotUseTheWorkingDirectory()
    {
        var foreign = OperatingSystem.IsWindows() ? NativeFilePaths.Posix : NativeFilePaths.Windows;
        Assert.Throws<PlatformNotSupportedException>(() => foreign.NormalizeAbsolute("/file"));
        Assert.Equal(OperatingSystem.IsWindows(), Native.TryFromFileUri("file:///D:/folder/file.png", out _));
        Assert.Equal(OperatingSystem.IsWindows(), Native.TryFromFileUri("file://server/share/file.png", out _));
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(@"C:\folder\file", Native.NormalizeAbsolute("C:/folder/file"));
            Assert.Throws<ArgumentException>(() => Native.NormalizeAbsolute(@"\\?\C:\file"));
            Assert.False(Native.TryFromFileUri("file:///C:/a%5Cb", out _));
        }
        else
        {
            Assert.Throws<ArgumentException>(() => Native.NormalizeAbsolute("//server/share"));
            const string path = "/folder/literal\\backslash";
            Assert.True(Native.TryFromFileUri(Native.ToFileUri(path), out var restored));
            Assert.Equal(path, restored);
        }
    }

    [Theory]
    [InlineData("file.png", true)]
    [InlineData("日本 #%.png", true)]
    [InlineData("CON.txt", false)]
    [InlineData("COM¹.txt", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("a:", false)]
    [InlineData("name ", false)]
    [InlineData("name.", false)]
    [InlineData("..", false)]
    public void PortabilityIsAnExplicitValidationPolicy(string name, bool expected) => Assert.Equal(expected, PortableFileNames.IsValid(name));

    [Fact]
    public void PortabilityBoundsUnicodeWithoutChangingTheInput()
    {
        Assert.False(PortableFileNames.IsValid(new string('a', 256)));
        Assert.False(PortableFileNames.IsValid(new string('日', 86)));
        Assert.False(PortableFileNames.IsValid("\ud800"));
        Assert.True(PortableFileNames.IsValid(new string('a', 255)));
    }
}
