using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Bathos.Platform.Windows;
using Xunit;

namespace Bathos.Platform.Tests;

[Trait("Validation", "Native")]
public sealed partial class WindowsShellWorkerTests
{
    private const int Failure = unchecked((int)0x80004005);

    [Fact]
    public async Task StaWorkerDoesNotCompleteItsTaskUntilCleanupReturns()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("STA workers require Windows."); return; }
        var enteredCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCleanup = new ManualResetEventSlim();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var shell = new Probe(new Stub(), _ => 0)
        {
            BeforeUninitialize = () => { enteredCleanup.SetResult(); releaseCleanup.Wait(); }
        };
        var callerThread = Environment.CurrentManagedThreadId;
        var operation = WindowsFileRevealer.StartWorker("owned fixture", shell, CancellationToken.None);
        try
        {
            await enteredCleanup.Task.WaitAsync(deadline.Token);
            Assert.False(operation.IsCompleted);
            releaseCleanup.Set();
            await operation.WaitAsync(deadline.Token);
            Assert.True(operation.IsCompletedSuccessfully);
            AssertWorker(shell, callerThread);
            Assert.Equal(["initialize", "parse", "select", "free", "uninitialize"], shell.Events);
        }
        finally
        {
            releaseCleanup.Set();
            await JoinAsync(operation, shell);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkerFaultsPreserveNativeHresultOrManagedExceptionIdentity(bool nativeFailure)
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("STA workers require Windows."); return; }
        var failure = new IOException("Controlled submission failure.");
        var shell = new Probe(new Stub(), _ => nativeFailure ? Failure : throw failure);
        var operation = WindowsFileRevealer.StartWorker("owned fixture", shell, CancellationToken.None);
        try
        {
            if (nativeFailure)
            {
                var error = await Assert.ThrowsAsync<COMException>(() => operation);
                Assert.Equal(Failure, error.HResult);
            }
            else Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => operation));
            Assert.True(operation.IsFaulted);
            AssertWorker(shell);
            Assert.Equal(["initialize", "parse", "select", "free", "uninitialize"], shell.Events);
        }
        finally { await JoinAsync(operation, shell); }
    }

    [Theory]
    [InlineData("before-submission")]
    [InlineData("after-success")]
    [InlineData("after-failure")]
    public async Task WorkerTaskStateRespectsTheSubmissionBoundary(string scenario)
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("STA workers require Windows."); return; }
        using var cancellation = new CancellationTokenSource();
        var shell = new Probe(new Stub(), _ =>
        {
            cancellation.Cancel();
            return scenario == "after-failure" ? Failure : 0;
        });
        if (scenario == "before-submission") shell.AfterParse = _ => cancellation.Cancel();
        var operation = WindowsFileRevealer.StartWorker("owned fixture", shell, cancellation.Token);
        try
        {
            if (scenario == "before-submission")
            {
                var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
                Assert.Equal(cancellation.Token, error.CancellationToken);
                Assert.True(operation.IsCanceled);
                Assert.Equal(["initialize", "parse", "free", "uninitialize"], shell.Events);
            }
            else
            {
                if (scenario == "after-failure")
                {
                    var error = await Assert.ThrowsAsync<COMException>(() => operation);
                    Assert.Equal(Failure, error.HResult);
                    Assert.True(operation.IsFaulted);
                }
                else { await operation; Assert.True(operation.IsCompletedSuccessfully); }
                Assert.Equal(["initialize", "parse", "select", "free", "uninitialize"], shell.Events);
            }
            Assert.True(cancellation.IsCancellationRequested);
            AssertWorker(shell);
        }
        finally { await JoinAsync(operation, shell); }
    }

    [Fact]
    public async Task IndependentOperationsOwnDifferentStaWorkersAndCanOverlap()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("STA workers require Windows."); return; }
        using var release = new ManualResetEventSlim();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new Probe(new Stub(), _ => 0) { AfterParse = _ => { firstEntered.SetResult(); release.Wait(); } };
        var second = new Probe(new Stub(), _ => 0) { AfterParse = _ => { secondEntered.SetResult(); release.Wait(); } };
        var firstOperation = WindowsFileRevealer.StartWorker("first fixture", first, CancellationToken.None);
        var secondOperation = WindowsFileRevealer.StartWorker("second fixture", second, CancellationToken.None);
        try
        {
            await Task.WhenAll(firstEntered.Task, secondEntered.Task).WaitAsync(deadline.Token);
            Assert.False(firstOperation.IsCompleted);
            Assert.False(secondOperation.IsCompleted);
            Assert.NotSame(first.Worker, second.Worker);
            release.Set();
            await Task.WhenAll(firstOperation, secondOperation).WaitAsync(deadline.Token);
            AssertWorker(first);
            AssertWorker(second);
        }
        finally
        {
            release.Set();
            await JoinAsync(firstOperation, first);
            await JoinAsync(secondOperation, second);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativePreparationResolvesTheRequestedEntryAndReleasesItOnTheStaWorker(bool directory)
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Real COM/PIDL preparation requires Windows."); return; }
        var folder = Directory.CreateTempSubdirectory("bathos-shell-");
        try
        {
            var path = Path.Combine(folder.FullName, "owned #100% café");
            if (directory) Directory.CreateDirectory(path);
            else File.WriteAllText(path, "file owner");
            string? resolvedPath = null;
            var shell = NativeProbe(item =>
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                resolvedPath = PathFromItem(item);
                return 0;
            });
            var operation = WindowsFileRevealer.StartWorker(path, shell, CancellationToken.None);
            try
            {
                await operation;
                Assert.Equal(path, resolvedPath, ignoreCase: true);
                Assert.Equal(0, shell.ParseResult);
                Assert.NotEqual(0, shell.ParsedItem);
                Assert.Equal([shell.ParsedItem], shell.FreedItems);
                AssertNativeCleanup(shell);
                if (directory) Assert.True(Directory.Exists(path));
                else Assert.Equal("file owner", File.ReadAllText(path));
            }
            finally { await JoinAsync(operation, shell); }
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData("parse-failure")]
    [InlineData("cancel-after-parse")]
    [InlineData("selection-failure")]
    public async Task RealNativePreparationCleansUpBeforeReportingCancellationOrFailure(string scenario)
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Real COM/PIDL preparation requires Windows."); return; }
        var folder = Directory.CreateTempSubdirectory("bathos-shell-failure-");
        using var cancellation = new CancellationTokenSource();
        try
        {
            var path = Path.Combine(folder.FullName, "owned.txt");
            File.WriteAllText(path, "file owner");
            var shell = NativeProbe(_ => Failure);
            if (scenario == "parse-failure") shell.BeforeParse = () => File.Delete(path);
            if (scenario == "cancel-after-parse") shell.AfterParse = _ => cancellation.Cancel();
            var operation = WindowsFileRevealer.StartWorker(path, shell, cancellation.Token);
            try
            {
                if (scenario == "cancel-after-parse")
                {
                    var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
                    Assert.Equal(cancellation.Token, error.CancellationToken);
                    Assert.True(operation.IsCanceled);
                }
                else
                {
                    var error = await Record.ExceptionAsync(() => operation);
                    Assert.NotNull(error);
                    Assert.Equal(scenario == "parse-failure" ? shell.ParseResult : Failure, error.HResult);
                    Assert.True(operation.IsFaulted);
                }
                if (scenario == "parse-failure")
                {
                    Assert.True(shell.ParseResult < 0);
                    Assert.Equal(0, shell.ParsedItem);
                    Assert.Empty(shell.FreedItems);
                }
                else
                {
                    Assert.Equal(0, shell.ParseResult);
                    Assert.NotEqual(0, shell.ParsedItem);
                    Assert.Equal([shell.ParsedItem], shell.FreedItems);
                    Assert.Equal("file owner", File.ReadAllText(path));
                }
                Assert.Equal(scenario == "selection-failure", shell.Events.Contains("select"));
                AssertNativeCleanup(shell);
            }
            finally { await JoinAsync(operation, shell); }
        }
        finally { folder.Delete(true); }
    }

    private static void AssertWorker(Probe shell, int? callerThread = null)
    {
        var worker = Assert.IsType<Thread>(shell.Worker);
        Assert.Equal(ApartmentState.STA, shell.Apartment);
        Assert.True(shell.Background);
        if (callerThread is { } caller) Assert.NotEqual(caller, worker.ManagedThreadId);
        Assert.All(shell.Threads, id => Assert.Equal(worker.ManagedThreadId, id));
        Assert.Equal("uninitialize", shell.Events[^1]);
    }

    private static async Task JoinAsync(Task operation, Probe shell)
    {
        // An assertion failure must still release gates and await all owned work, including the worker's exit.
        await operation.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        shell.Worker?.Join();
    }

    [SupportedOSPlatform("windows")]
    private static Probe NativeProbe(Func<nint, int> selection) => new(new WindowsFileRevealer.NativeShell(), selection)
    {
        InspectCom = true
    };

    private static void AssertNativeCleanup(Probe shell)
    {
        AssertWorker(shell);
        Assert.True(shell.InitializeResult is 0 or 1); // S_OK or S_FALSE both own an initialization.
        Assert.Equal(0, shell.DuringCom.Result);
        Assert.True(shell.DuringCom.Kind is 0 or 3); // STA or main STA, as reported by the native COM API.
        Assert.Equal(shell.BeforeCom, shell.AfterCom);
        Assert.Equal(1, shell.Events.Count(item => item == "uninitialize"));
    }

    [SupportedOSPlatform("windows")]
    private static unsafe string PathFromItem(nint item)
    {
        Span<char> buffer = stackalloc char[260]; // SHGetPathFromIDListW requires a MAX_PATH buffer.
        fixed (char* output = buffer) Assert.NotEqual(0, GetPathFromItem(item, output));
        var end = buffer.IndexOf('\0');
        if (end < 0) throw new InvalidDataException("The Shell path was not terminated.");
        return new string(buffer[..end]);
    }

    [SupportedOSPlatform("windows")]
    private static ApartmentSnapshot ReadApartment()
    {
        var result = GetApartmentType(out var kind, out var qualifier);
        return result < 0 ? new(result, 0, 0) : new(result, kind, qualifier);
    }

    private readonly record struct ApartmentSnapshot(int Result, int Kind, int Qualifier);

    // All preparation/cleanup calls delegate to the supplied implementation. Selection is always controlled:
    // this fixture cannot call SHOpenFolderAndSelectItems or open Explorer, even with the real native adapter.
    private sealed class Probe(WindowsFileRevealer.IShell preparation, Func<nint, int> selection) : WindowsFileRevealer.IShell
    {
        internal List<string> Events { get; } = [];
        internal List<int> Threads { get; } = [];
        internal List<nint> FreedItems { get; } = [];
        internal Thread? Worker { get; private set; }
        internal ApartmentState Apartment { get; private set; }
        internal bool Background { get; private set; }
        internal bool InspectCom { get; init; }
        internal int InitializeResult { get; private set; }
        internal int ParseResult { get; private set; }
        internal nint ParsedItem { get; private set; }
        internal ApartmentSnapshot BeforeCom { get; private set; }
        internal ApartmentSnapshot DuringCom { get; private set; }
        internal ApartmentSnapshot AfterCom { get; private set; }
        internal Action? BeforeParse { get; set; }
        internal Action<nint>? AfterParse { get; set; }
        internal Action? BeforeUninitialize { get; init; }

        public int Initialize()
        {
            Worker = Thread.CurrentThread;
            Apartment = Worker.GetApartmentState();
            Background = Worker.IsBackground;
            Record("initialize");
            if (InspectCom && OperatingSystem.IsWindows()) BeforeCom = ReadApartment();
            InitializeResult = preparation.Initialize();
            if (InspectCom && OperatingSystem.IsWindows()) DuringCom = ReadApartment();
            return InitializeResult;
        }
        public int Parse(string path, out nint item)
        {
            Record("parse");
            BeforeParse?.Invoke();
            ParseResult = preparation.Parse(path, out item);
            ParsedItem = item;
            AfterParse?.Invoke(item);
            return ParseResult;
        }
        public int Select(nint item) { Record("select"); return selection(item); }
        public void Free(nint item) { Record("free"); preparation.Free(item); FreedItems.Add(item); }
        public void Uninitialize()
        {
            BeforeUninitialize?.Invoke();
            preparation.Uninitialize();
            if (InspectCom && OperatingSystem.IsWindows()) AfterCom = ReadApartment();
            Record("uninitialize");
        }
        private void Record(string name) { Events.Add(name); Threads.Add(Environment.CurrentManagedThreadId); }
    }

    private sealed class Stub : WindowsFileRevealer.IShell
    {
        public int Initialize() => 0;
        public int Parse(string path, out nint item) { item = 42; return 0; }
        public int Select(nint item) => throw new InvalidOperationException("Selection must be controlled by the probe.");
        public void Free(nint item) { }
        public void Uninitialize() { }
    }

    [LibraryImport("ole32.dll", EntryPoint = "CoGetApartmentType")]
    private static partial int GetApartmentType(out int kind, out int qualifier);
    [LibraryImport("shell32.dll", EntryPoint = "SHGetPathFromIDListW")]
    private static unsafe partial int GetPathFromItem(nint item, char* path);
}
