using Bathos.Platform.Desktop;
using Bathos.Platform.Linux;
using Xunit;

namespace Bathos.Platform.Tests;

public sealed class LinuxIconThemeTests
{
    [Fact]
    [Trait("Validation", "Portable")]
    public void ThemeLookupSelectsSizeThenInheritanceAndReturnsUndecodedOwnedResources()
    {
        var folder = Directory.CreateTempSubdirectory("bathos-icons-");
        var token = TestContext.Current.CancellationToken;
        try
        {
            var root = folder.FullName;
            Write(root, "Custom/index.theme", "[Icon Theme]\nDirectories=16x16/apps,32x32/apps\nInherits=Parent\n[16x16/apps]\nSize=16\nType=Fixed\n[32x32/apps]\nSize=32\nType=Fixed\n");
            Write(root, "Custom/16x16/apps/editor.svg", "<svg>small</svg>");
            Write(root, "Custom/32x32/apps/editor.svg", "<svg>large</svg>");
            Write(root, "Parent/index.theme", "[Icon Theme]\nDirectories=scalable/apps\nInherits=Custom\n[scalable/apps]\nSize=48\nType=Scalable\nMinSize=1\nMaxSize=256\n");
            Write(root, "Parent/scalable/apps/other.svg", "<svg>parent</svg>");
            Write(root, "hicolor/index.theme", "[Icon Theme]\nDirectories=32x32/apps\n[32x32/apps]\nSize=32\nScale=2\nType=Fixed\n");
            Write(root, "hicolor/32x32/apps/standard.xpm", "/* XPM */");
            var selected = LinuxIconTheme.Find(["editor"], "Custom", 30, [root], token);
            Assert.Equal(Path.GetFullPath(Path.Combine(root, "Custom/32x32/apps/editor.svg")), Path.GetFullPath(Assert.IsType<string>(selected)));
            var icon = LinuxIconTheme.ReadIcon(Assert.IsType<string>(selected));
            Assert.Equal(ApplicationIconFormat.Svg, icon.Format);
            Assert.Equal("<svg>large</svg>"u8.ToArray(), icon.Data.ToArray());
            Assert.Equal(Path.GetFullPath(Path.Combine(root, "Parent/scalable/apps/other.svg")), Path.GetFullPath(Assert.IsType<string>(LinuxIconTheme.Find(["other"], "Custom", 64, [root], token))));
            Assert.Equal(Path.GetFullPath(Path.Combine(root, "hicolor/32x32/apps/standard.xpm")), Path.GetFullPath(Assert.IsType<string>(LinuxIconTheme.Find(["standard"], "Custom", 64, [root], token))));
            Assert.Null(LinuxIconTheme.Find(["missing"], "Custom", 32, [root], token));
            folder.Delete(true);
            Assert.Equal("<svg>large</svg>"u8.ToArray(), icon.Data.ToArray());
        }
        finally { if (folder.Exists) folder.Delete(true); }
    }

    [Fact]
    [Trait("Validation", "Portable")]
    public void ThemeBoundsRejectTraversalOversizedResourcesAndCancellation()
    {
        var folder = Directory.CreateTempSubdirectory("bathos-icon-bounds-");
        try
        {
            var root = folder.FullName; var token = TestContext.Current.CancellationToken;
            Write(root, "Unsafe/index.theme", "[Icon Theme]\nDirectories=../outside\n");
            Assert.Throws<IOException>(() => LinuxIconTheme.Find(["editor"], "Unsafe", 32, [root], token));
            Assert.Throws<IOException>(() => LinuxIconTheme.Find(["../editor"], "hicolor", 32, [root], token));
            var path = Path.Combine(root, "large.png");
            using (var stream = File.Create(path)) stream.SetLength(ApplicationIcon.MaximumResourceBytes + 1L);
            Assert.Throws<IOException>(() => LinuxIconTheme.ReadIcon(path));
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => LinuxIconTheme.Find(["editor"], "hicolor", 32, [root], canceled.Token));
            Assert.Throws<ArgumentException>(() => new LinuxApplicationIconProvider("../theme"));
        }
        finally { folder.Delete(true); }
    }

    private static void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException());
        File.WriteAllText(path, text);
    }
}
