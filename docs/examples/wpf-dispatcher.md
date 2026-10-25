# WPF dispatcher integration

A WPF application can adapt its window's existing `Dispatcher` to `INativeDispatcher`. This example
belongs in the host application; the Bathos packages have no WPF dependency. Reference
`Bathos.Platform.Windows` from a WPF project with `UseWPF` enabled. The example is compiled against
the packed libraries by Windows package validation.

[`VerifyAccess`](https://learn.microsoft.com/en-us/dotnet/api/system.windows.threading.dispatcher.verifyaccess)
checks access to the WPF UI thread.
[`BeginInvoke`](https://learn.microsoft.com/en-us/dotnet/api/system.windows.threading.dispatcher.begininvoke)
queues work asynchronously, including when called on that thread. Use the dispatcher from an
existing window; do not create one on a worker thread or replace asynchronous posting with an inline
call.

```csharp
using System.Windows.Threading;
using Bathos.Platform;

public sealed class WpfNativeDispatcher : INativeDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfNativeDispatcher(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
    }

    public void VerifyAccess() => _dispatcher.VerifyAccess();

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
    }
}
```

The following window wires a button to native sampling and previews the result. WPF event handlers
resume on the UI thread after `await`. Disabling the button prevents overlap; the closing handler
keeps the dispatcher alive until the operation completes. Exceptions are handled at the event
boundary, and canceled selection leaves the preview unchanged. The host can adapt the same pattern
to its own controls and error presentation.

```csharp
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Bathos.Platform;
using Bathos.Platform.Windows;

public sealed class SamplingWindow : Window
{
    private readonly IScreenColorSampler _sampler;
    private readonly Button _sample = new() { Content = "Pick a screen color" };
    private readonly Border _preview = new() { Height = 80, Background = Brushes.White };
    private bool _sampling;

    public SamplingWindow()
    {
        _sampler = new WindowsPlatformProvider(new WpfNativeDispatcher(Dispatcher)).ScreenColorSampler
            ?? throw new PlatformNotSupportedException("Screen sampling is unavailable.");
        var panel = new StackPanel();
        panel.Children.Add(_sample);
        panel.Children.Add(_preview);
        Content = panel;
        _sample.Click += SampleClicked;
        Closing += KeepAliveWhileSampling;
    }

    private async void SampleClicked(object sender, RoutedEventArgs args)
    {
        _sampling = true;
        _sample.IsEnabled = false;
        try
        {
            if (await _sampler.SampleAsync() is { } color)
                _preview.Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Screen sampling failed");
        }
        finally
        {
            _sampling = false;
            _sample.IsEnabled = true;
        }
    }

    private void KeepAliveWhileSampling(object? sender, CancelEventArgs args)
    {
        if (_sampling) args.Cancel = true;
    }
}
```

This prevents ordinary window closing while sampling; it does not take ownership of application-wide
shutdown. Coordinate an explicit `Application.Shutdown` or other host shutdown paths with
outstanding work as well. Do not block the UI thread with `.Wait()`/`.Result`, start a nested
dispatcher loop, or shut down the dispatcher while sampling. See the
[general sampling recipe](../usage.md#applying-a-result-to-a-live-caller) when callers can retire
independently of the host window.
