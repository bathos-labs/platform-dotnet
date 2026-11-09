using Xunit;

namespace Bathos.Platform.Tests;

// Unique registry keys do not isolate Shell's shared application/icon metadata cache.
// Keep each fixture's registered files alive until its native work and cleanup complete.
[CollectionDefinition("Windows Shell registrations", DisableParallelization = true)]
public sealed class WindowsShellRegistrationTests;
