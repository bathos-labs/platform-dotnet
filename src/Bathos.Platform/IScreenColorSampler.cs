namespace Bathos.Platform;

/// <summary>Opaque eight-bit sRGB screen appearance, not original image samples or source-profile evidence.</summary>
public readonly record struct ScreenColor(byte R, byte G, byte B);

/// <summary>Interactive native selection. Invoke on the native main thread; null means user cancellation.</summary>
/// <remarks>
/// Native UI owns completion and cleanup. Retiring a caller does not close the system chooser; callers must ignore
/// late results. Implementations report operation failures as exceptions, independently of capability availability.
/// </remarks>
public interface IScreenColorSampler
{
    Task<ScreenColor?> SampleAsync();
}

/// <summary>Host integration with the native application's main event loop, without a toolkit dependency.</summary>
public interface INativeDispatcher
{
    /// <summary>Throws when called outside the native main thread.</summary>
    void VerifyAccess();
    /// <summary>Enqueues work on that thread after the current native callback unwinds. Must never invoke inline.</summary>
    /// <remarks>The host must keep its event loop alive until outstanding native operations finish.</remarks>
    void Post(Action action);
}
