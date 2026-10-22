namespace Bathos.Platform;

/// <summary>Opaque eight-bit RGB for ordinary color selection, without a colorimetric accuracy guarantee.</summary>
/// <remarks>Values depend on the platform's sampling path and may differ from source content or perceived display appearance.</remarks>
public readonly record struct ScreenColor(byte R, byte G, byte B);

/// <summary>Interactive native selection. Null means user cancellation.</summary>
/// <remarks>
/// Invoke on the native UI thread when a dispatcher is required by the provider. Native interaction owns completion
/// and cleanup. Retiring a caller does not dismiss selection; callers must ignore late results. Implementations
/// report operation failures as exceptions, independently of capability availability.
/// </remarks>
public interface IScreenColorSampler
{
    Task<ScreenColor?> SampleAsync();
}

/// <summary>Host integration with the native application's main event loop, without a toolkit dependency.</summary>
public interface INativeDispatcher
{
    /// <summary>Throws when called outside the native UI thread (the main thread on macOS).</summary>
    void VerifyAccess();
    /// <summary>Enqueues work on that thread after the current native callback unwinds. Must never invoke inline.</summary>
    /// <remarks>The host must keep its event loop alive until outstanding native operations finish.</remarks>
    void Post(Action action);
}
