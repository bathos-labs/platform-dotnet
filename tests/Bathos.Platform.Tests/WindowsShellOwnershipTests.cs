using System.Runtime.InteropServices;
using Bathos.Platform.Windows;
using Xunit;

namespace Bathos.Platform.Tests;

[Trait("Validation", "Portable")]
public sealed class WindowsShellOwnershipTests
{
    private const int Failure = unchecked((int)0x80004005);

    [Theory]
    [InlineData(0)]
    [InlineData(1)] // S_FALSE owns a COM initialization just as S_OK does.
    public void SuccessfulSubmissionReleasesPidlBeforeComEvenIfCallerCancelsAfterSubmission(int initialized)
    {
        using var cancellation = new CancellationTokenSource();
        var shell = new Shell { Initialized = initialized, OnSelect = cancellation.Cancel };
        WindowsFileRevealer.Reveal("document", shell, cancellation.Token);
        Assert.Equal(["initialize", "parse", "select", "free", "uninitialize"], shell.Events);
        Assert.True(cancellation.IsCancellationRequested);
    }

    [Fact]
    public void FailedInitializationOwnsNoComOrPidl()
    {
        var shell = new Shell { Initialized = Failure };
        Assert.Throws<COMException>(() => WindowsFileRevealer.Reveal("document", shell, CancellationToken.None));
        Assert.Equal(["initialize"], shell.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParseFailureAndMalformedNullItemAlwaysBalanceCom(bool failedParse)
    {
        var shell = new Shell { Parsed = failedParse ? Failure : 0, Item = 0 };
        if (failedParse) Assert.Throws<COMException>(() => WindowsFileRevealer.Reveal("document", shell, CancellationToken.None));
        else Assert.Throws<IOException>(() => WindowsFileRevealer.Reveal("document", shell, CancellationToken.None));
        Assert.Equal(["initialize", "parse", "uninitialize"], shell.Events);
    }

    [Fact]
    public void ParseFailureWithAnOwnedItemReleasesItBeforeCom()
    {
        var shell = new Shell { Parsed = Failure };
        Assert.Throws<COMException>(() => WindowsFileRevealer.Reveal("document", shell, CancellationToken.None));
        Assert.Equal(["initialize", "parse", "free", "uninitialize"], shell.Events);
    }

    [Fact]
    public void FailedSelectionPreservesNativeErrorAndReleasesResources()
    {
        var shell = new Shell { Selected = Failure };
        var error = Assert.Throws<COMException>(() => WindowsFileRevealer.Reveal("document", shell, CancellationToken.None));
        Assert.Equal(Failure, error.HResult);
        Assert.Equal(["initialize", "parse", "select", "free", "uninitialize"], shell.Events);
    }

    [Fact]
    public void CancellationDuringPreparationFreesPidlWithoutSubmitting()
    {
        using var cancellation = new CancellationTokenSource();
        var shell = new Shell { OnParse = cancellation.Cancel };
        var error = Assert.Throws<OperationCanceledException>(() => WindowsFileRevealer.Reveal("document", shell, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(["initialize", "parse", "free", "uninitialize"], shell.Events);
        var next = new Shell();
        WindowsFileRevealer.Reveal("document", next, CancellationToken.None);
        Assert.Equal(["initialize", "parse", "select", "free", "uninitialize"], next.Events);
    }

    [Fact]
    public void PreCanceledRequestDoesNotInitializeCom()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var shell = new Shell();
        Assert.Throws<OperationCanceledException>(() => WindowsFileRevealer.Reveal("document", shell, cancellation.Token));
        Assert.Empty(shell.Events);
    }

    private sealed class Shell : WindowsFileRevealer.IShell
    {
        internal List<string> Events { get; } = [];
        internal int Initialized { get; init; }
        internal int Parsed { get; init; }
        internal int Selected { get; init; }
        internal nint Item { get; init; } = 42;
        internal Action? OnParse { get; init; }
        internal Action? OnSelect { get; init; }
        public int Initialize() { Events.Add("initialize"); return Initialized; }
        public int Parse(string path, out nint item) { Events.Add("parse"); item = Item; OnParse?.Invoke(); return Parsed; }
        public int Select(nint item) { Assert.Equal(Item, item); Events.Add("select"); OnSelect?.Invoke(); return Selected; }
        public void Free(nint item) { Assert.Equal(Item, item); Events.Add("free"); }
        public void Uninitialize() => Events.Add("uninitialize");
    }
}
