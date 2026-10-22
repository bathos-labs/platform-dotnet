using Bathos.Platform;
using Bathos.Platform.MacOS;
using Bathos.Platform.Sampler;

if (args is not ([] or ["--smoke"]))
{
    Console.Error.WriteLine("Usage: dotnet run --project eng/Bathos.Platform.Sampler -- [--smoke]");
    return 2;
}
if (!OperatingSystem.IsMacOSVersionAtLeast(10, 15))
{
    Console.Error.WriteLine("The interactive screen sampler requires macOS 10.15 or later. Use task check for file capabilities on other hosts.");
    return 2;
}

using var host = new NativeHost();
var sampler = new MacOSPlatformProvider(host).ScreenColorSampler
    ?? throw new PlatformNotSupportedException("The sampler is unavailable.");
var result = 0;
var finished = false;
var busy = false;
string[] outcomes = ["1. Red target: not run", "2. Outside window: not run", "3. Escape: not run", "4. Retirement: not run"];
host.ConfigureTests(index => { _ = SampleAsync(index); }, Finish);
ShowResults("Ready. Move the window, then choose a test.");
if (args is ["--smoke"])
{
    var started = false;
    host.Post(() => { started = true; _ = SmokeAsync(); });
    if (started) throw new InvalidOperationException("Dispatch ran inline.");
}
host.Run();
if (!finished) throw new InvalidOperationException("The native loop exited before owned work finished.");
return result;

void ShowResults(string message) => host.SetStatus(message + "\n\n" + string.Join("\n", outcomes));

void Finish()
{
    host.VerifyAccess();
    if (busy) return;
    finished = true;
    host.Stop();
}

async Task SampleAsync(int index)
{
    host.VerifyAccess();
    if (busy) return;
    busy = true;
    host.SetBusy(true);
    string[] instructions = ["Click the center of the red target.", "Click a color outside this window.",
        "Press Escape to cancel the picker.", "Caller retired. Select or cancel; the result will be ignored."];
    ShowResults(instructions[index]);
    string outcome;
    try
    {
        // Every invocation starts on the main thread in response to a distinct button click.
        var color = await sampler.SampleAsync().ConfigureAwait(false);
        outcome = index switch
        {
            0 when color is { R: >= 250, G: <= 5, B: <= 5 } => $"1. PASS: red target {Format(color)}",
            0 => $"1. CHECK: expected #FF0000; got {Format(color)}. Retry the red target.",
            1 => $"2. {Format(color)} — visually confirm against the selected location.",
            2 when color is null => "3. PASS: Escape canceled the picker.",
            2 => $"3. RETRY: expected cancellation; got {Format(color)}.",
            _ => "4. PASS: retired caller ignored the result; native completion observed."
        };
    }
    catch (Exception error)
    {
        outcome = $"{index + 1}. ERROR: {error.Message}";
        Console.Error.WriteLine(error);
    }
    host.Post(() =>
    {
        outcomes[index] = outcome;
        busy = false;
        host.SetBusy(false);
        ShowResults("Ready. Choose another test, repeat a test, or Finish.");
        Console.WriteLine(outcome);
    });
}

static string Format(ScreenColor? color) => color is { } value ? $"sRGB #{value.R:X2}{value.G:X2}{value.B:X2}" : "canceled";

async Task SmokeAsync()
{
    try
    {
        host.VerifyAccess();
        await Task.Run(() =>
        {
            try { host.VerifyAccess(); }
            catch (InvalidOperationException) { return; }
            throw new InvalidOperationException("The dispatcher accepted a worker thread.");
        }).ConfigureAwait(false);
        await host.InvokeAsync(() =>
        {
            host.VerifyAccess();
            host.SetBusy(true);
            host.SetStatus("Dispatcher smoke check passed.");
            host.SetBusy(false);
            return Task.FromResult(true);
        }).ConfigureAwait(false);
        Console.WriteLine("PASS: deferred main-thread dispatch, worker rejection, and owned completion. Picker not exercised.");
    }
    catch (Exception error) { result = 1; Console.Error.WriteLine(error); }
    finally { host.Post(Finish); }
}
