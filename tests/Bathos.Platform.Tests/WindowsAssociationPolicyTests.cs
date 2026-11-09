using System.Runtime.InteropServices;
using Bathos.Platform.Windows;
using Xunit;

namespace Bathos.Platform.Tests;

public sealed class WindowsAssociationPolicyTests
{
    [Theory]
    [InlineData(false, true, true, false, true)]
    [InlineData(true, false, false, true, false)]
    [InlineData(true, false, true, true, true)]
    [InlineData(true, true, true, true, false)]
    public void CoalescingKeepsCompleteWinner(bool firstRecommended, bool firstId, bool nextRecommended, bool nextId, bool replace)
    {
        var first = new WindowsAssociationHandlers.Entry("APP", "First", firstRecommended, firstId, 1);
        var next = new WindowsAssociationHandlers.Entry("app", "Second", nextRecommended, nextId, 2);
        var winners = new Dictionary<string, WindowsAssociationHandlers.Entry>(StringComparer.OrdinalIgnoreCase);
        WindowsAssociationHandlers.AddCandidate(winners, first.Name, first);
        WindowsAssociationHandlers.AddCandidate(winners, next.Name, next);
        Assert.Same(replace ? next : first, Assert.Single(winners).Value);
        var distinct = new WindowsAssociationHandlers.Entry("another", "First", true, true, 3);
        WindowsAssociationHandlers.AddCandidate(winners, distinct.Name, distinct);
        Assert.Equal(2, winners.Count);
    }

    [Fact]
    public async Task WorkerNormalizesComFailureAndPreservesNativeDetails()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows worker required."); return; }
        var native = Assert.IsType<COMException>(Marshal.GetExceptionForHR(unchecked((int)0x80004005)));
        var error = await Assert.ThrowsAsync<IOException>(() => WindowsAssociationHandlers.Run<int>(() => throw native));
        Assert.Same(native, error.InnerException);
        Assert.Contains("80004005", error.Message);
    }

    [Fact]
    public async Task EnumerationUnwindsAfterConsumerAllocationFailure()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows Shell required."); return; }
        var failure = new InsufficientMemoryException("Injected consumer allocation failure.");
        await WindowsAssociationHandlers.Run(() =>
        {
            var error = Assert.Throws<InsufficientMemoryException>(() =>
                WindowsAssociationHandlers.WithHandlers<int>(".txt", _ => throw failure, CancellationToken.None));
            Assert.Same(failure, error);
            // Exercise another enumeration on the same apartment after exception cleanup.
            return WindowsAssociationHandlers.WithHandlers(".txt", entries => entries.Count, CancellationToken.None);
        });
    }

    [Fact]
    public async Task PreCanceledDiscoveryDoesNotInspectMissingFileSynchronously()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows worker required."); return; }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.txt");
        var pending = new WindowsFileApplicationDiscovery().FindForFileAsync(missing, cancellation.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }
}
