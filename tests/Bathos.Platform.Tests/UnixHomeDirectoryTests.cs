using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Bathos.Platform.Directories;
using Xunit;

namespace Bathos.Platform.Tests;

[Trait("Validation", "Portable")]
public sealed class UnixHomeDirectoryTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/home/explicit # value")]
    public void ConfiguredHomeDoesNotConsultTheAccountDatabase(string home) =>
        Assert.Equal(home, UnixHomeDirectory.Resolve(home, (_, _) => throw new InvalidOperationException("Unexpected account lookup.")));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void UnavailableAccountDoesNotBecomeTheFilesystemRoot(string? configuredHome)
    {
        var called = false;
        Assert.Null(UnixHomeDirectory.Resolve(configuredHome, (_, _) => { called = true; return new(0, false, 0); }));
        Assert.True(called);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/home/日本 \U0001F3A8 ")]
    public void AccountHomeIsCopiedBeforeItsBufferIsReleased(string home) =>
        Assert.Equal(home, UnixHomeDirectory.Resolve(null, (buffer, _) => WriteHome(buffer, home)));

    [Fact]
    public void AnUndersizedBufferIsRetriedAndAccountFailuresDoNotPreventLaterCalls()
    {
        var firstSize = 0;
        var result = UnixHomeDirectory.Resolve(null, (buffer, size) =>
        {
            if (firstSize == 0) { firstSize = size; return new(34, false, 0); }
            Assert.True(size > firstSize);
            return WriteHome(buffer, "/home/retried");
        });
        Assert.Equal("/home/retried", result);
        var error = Assert.Throws<IOException>(() => UnixHomeDirectory.Resolve(null, (_, _) => new(5, false, 0)));
        Assert.Equal(5, Assert.IsType<Win32Exception>(error.InnerException).NativeErrorCode);
        Assert.Equal("/home/recovered", UnixHomeDirectory.Resolve(null, (buffer, _) => WriteHome(buffer, "/home/recovered")));
    }

    [Fact]
    public void UnboundedAccountEntriesFailWithinTheDocumentedMemoryLimit()
    {
        Assert.Throws<IOException>(() => UnixHomeDirectory.Resolve(null, (_, size) =>
        {
            Assert.InRange(size, 1, 1024 * 1024);
            return new(34, false, 0);
        }));
    }

    [Theory]
    [InlineData("before-buffer")]
    [InlineData("past-buffer")]
    [InlineData("unterminated")]
    [InlineData("invalid-utf8")]
    public void MalformedNativeHomeResponsesFailWithoutReadingOutsideTheOwnedBuffer(string scenario)
    {
        Assert.Throws<InvalidDataException>(() => UnixHomeDirectory.Resolve(null, (buffer, size) =>
        {
            if (scenario == "before-buffer") return new(0, true, buffer - 1);
            if (scenario == "past-buffer") return new(0, true, buffer + size);
            var bytes = scenario == "unterminated" ? Enumerable.Repeat((byte)'x', size).ToArray() : new byte[] { 0xff, 0 };
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            return new(0, true, buffer);
        }));
    }

    [Fact]
    public void NullAndEmptyAccountHomeFieldsRemainUnavailable()
    {
        Assert.Null(UnixHomeDirectory.Resolve(null, (_, _) => new(0, true, 0)));
        Assert.Null(UnixHomeDirectory.Resolve(null, (buffer, _) => WriteHome(buffer, "")));
    }

    private static UnixHomeDirectory.Result WriteHome(nint buffer, string home)
    {
        // Native records contain several strings; the home field need not start at the beginning of the buffer.
        var address = buffer + 16;
        var bytes = Encoding.UTF8.GetBytes(home + '\0');
        Marshal.Copy(bytes, 0, address, bytes.Length);
        return new(0, true, address);
    }
}
