namespace Bathos.Platform;

/// <summary>Opaque eight-bit RGB for ordinary color selection, without a colorimetric accuracy guarantee.</summary>
/// <remarks>Values depend on the platform's sampling path and may differ from source content or perceived display appearance.</remarks>
/// <param name="R">Red component from 0 to 255.</param>
/// <param name="G">Green component from 0 to 255.</param>
/// <param name="B">Blue component from 0 to 255.</param>
public readonly record struct ScreenColor(byte R, byte G, byte B);

/// <summary>Interactive native selection. Null means user cancellation.</summary>
/// <remarks>
/// Invoke on the native UI thread when a dispatcher is required by the provider. Native interaction owns completion
/// and cleanup. Retiring a caller does not dismiss selection; callers must ignore late results. Implementations
/// report operation failures as exceptions, independently of capability availability.
/// </remarks>
public interface IScreenColorSampler
{
    /// <summary>Starts one native color selection and completes after its owned resources have been released.</summary>
    /// <returns>The selected opaque RGB color, or null only for user cancellation.</returns>
    /// <remarks>
    /// Windows and macOS require the configured native UI thread. Linux uses an owned portal worker.
    /// Built-in providers allow one active sampler operation per platform implementation; await completion before another call.
    /// There is no cancellation-token overload: retiring a caller must ignore the result and still await native completion.
    /// Windows samples a frozen GDI snapshot; macOS converts AppKit output to sRGB; Linux uses portal RGB values.
    /// No display-profile matching, original-image fidelity, or HDR accuracy is guaranteed. Native failures propagate as exceptions.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Another picker is active, UI-thread access is invalid, or native setup/color conversion fails.</exception>
    Task<ScreenColor?> SampleAsync();
}

/// <summary>Host integration with the native application's main event loop, without a toolkit dependency.</summary>
public interface INativeDispatcher
{
    /// <summary>Throws when called outside the native UI thread (the main thread on macOS).</summary>
    /// <exception cref="InvalidOperationException">The caller is outside the required native UI thread.</exception>
    void VerifyAccess();
    /// <summary>Enqueues work on that thread after the current native callback unwinds. Must never invoke inline.</summary>
    /// <param name="action">Work to enqueue on the native UI thread.</param>
    /// <remarks>
    /// The host must keep its event loop alive until outstanding native operations finish.
    /// Accepted work must run; the host must not shut down or reject cleanup posts while sampling is outstanding.
    /// </remarks>
    void Post(Action action);
}
