using Bathos.Platform.Linux;

namespace Bathos.Platform.Sampler;

internal static class LinuxSamplerHarness
{
    internal static async Task<int> RunAsync(string[] args)
    {
        if (args is ["--smoke"])
        {
            Console.Error.WriteLine("Linux has no harness-owned native dispatcher. Run task check for headless protocol tests.");
            return 2;
        }
        var sampler = new LinuxPlatformProvider(enableScreenColorSampler: true).ScreenColorSampler
            ?? throw new PlatformNotSupportedException("The experimental Linux sampler is unavailable.");
        Console.WriteLine("Experimental Linux portal sampler. Human input is required; no desktop validation is assumed.");
        Console.WriteLine("s: select a color; c: test Escape cancellation; r: test caller retirement; q: finish.");
        Console.WriteLine("Compare the printed RGB value with your selected location. Repeat after selection and cancellation.");
        while (true)
        {
            var input = Console.ReadLine();
            if (input is null or "q") return 0;
            if (input is not ("s" or "c" or "r")) continue;
            Console.WriteLine(input == "c" ? "Cancel the portal picker with Escape." : "Select a color in the portal picker, or press Escape.");
            try
            {
                var color = await sampler.SampleAsync().ConfigureAwait(false);
                Console.WriteLine(input switch
                {
                    "r" => "PASS: caller retired; late result ignored after native completion.",
                    "c" when color is null => "PASS: user cancellation.",
                    "c" => "RETRY: a color was selected instead of canceling.",
                    _ => color is { } rgb ? $"RGB #{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}; visually compare with your selection." : "Canceled."
                });
            }
            catch (Exception error) { Console.Error.WriteLine(error); }
        }
    }
}
