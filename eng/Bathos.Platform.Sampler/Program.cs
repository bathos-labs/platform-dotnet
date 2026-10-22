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
var result = 1;
var finished = false;
var started = false;
host.Post(() =>
{
    started = true;
    _ = RunAsync();
});
if (started) throw new InvalidOperationException("Dispatch ran inline.");
host.Run();
if (!finished) throw new InvalidOperationException("The native loop exited before owned work finished.");
return result;

async Task RunAsync()
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
        if (args is ["--smoke"])
        {
            await host.InvokeAsync(() =>
            {
                host.VerifyAccess();
                return Task.FromResult(true);
            }).ConfigureAwait(false);
            Console.WriteLine("PASS: deferred main-thread dispatch, worker rejection, and owned completion. Picker not exercised.");
        }
        else
        {
            var sampler = new MacOSPlatformProvider(host).ScreenColorSampler
                ?? throw new PlatformNotSupportedException("The sampler is unavailable.");
            string[] steps = ["Select the red harness window (expect approximately #FF0000).",
                "Select a color outside the harness window (try another display if available).",
                "Press Escape (expect cancellation).", "Select or cancel after caller retirement (result will be ignored)."];
            for (var index = 0; index < steps.Length; index++)
            {
                Console.WriteLine($"Step {index + 1}/4: {steps[index]}");
                var retire = index == 3;
                var color = await host.InvokeAsync(() =>
                {
                    var pending = sampler.SampleAsync();
                    if (retire) Console.WriteLine("Caller retired; keeping the event loop alive until native completion.");
                    return pending;
                }).ConfigureAwait(false);
                Console.WriteLine(retire ? "Retired caller ignored the late result; native completion observed."
                    : color is { } value ? $"Selected sRGB #{value.R:X2}{value.G:X2}{value.B:X2}." : "Canceled by user.");
                if (index == 2 && color is not null) throw new InvalidOperationException("Escape step returned a color; repeat the manual check.");
            }
            Console.WriteLine("Sequence completed. Confirm the visual results and record host/OS observations in docs/validation.md.");
        }
        result = 0;
    }
    catch (Exception error) { Console.Error.WriteLine(error); }
    finally
    {
        host.Post(() =>
        {
            finished = true;
            host.Stop();
        });
    }
}
