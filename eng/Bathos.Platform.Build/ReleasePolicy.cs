using System.Diagnostics;
using System.Text.RegularExpressions;

internal static partial class ReleasePolicy
{
    internal static async Task VerifyAsync(string root, string version, string tag, bool remote)
    {
        var match = SemanticVersion().Match(version);
        var suffix = match.Groups["prerelease"];
        if (!match.Success || suffix.Value.Split('.').Any(identifier =>
            identifier.Length > 1 && identifier[0] == '0' && identifier.All(char.IsAsciiDigit)))
            throw new InvalidOperationException("Release versions must follow Semantic Versioning: major.minor.patch[-prerelease][+metadata].");
        var prerelease = suffix.Success;
        if (!remote)
        {
            var branch = await Git(root, "symbolic-ref", "--quiet", "--short", "HEAD");
            if (branch.ExitCode == 1) throw new InvalidOperationException("Release preparation requires a checked-out master or release/* branch.");
            RequireSuccess(branch);
            if (branch.Output == "master" || prerelease && IsReleaseBranch(branch.Output))
            {
                Console.WriteLine($"Release preparation accepted: {tag} on {branch.Output}.");
                return;
            }
            throw new InvalidOperationException("Only master may prepare stable releases; master and release/* may prepare prereleases.");
        }

        var changes = await Run(root, "status", "--porcelain");
        if (changes.Length != 0) throw new InvalidOperationException("Release publication requires a clean checkout of the tagged commit.");
        var head = await Run(root, "rev-parse", "--verify", "HEAD");
        var tagged = await Run(root, "rev-parse", "--verify", $"refs/tags/{tag}^{{commit}}");
        if (head != tagged) throw new InvalidOperationException("Check out the tagged commit before verifying or pushing its release tag.");

        // Refresh only eligible heads and prune deleted release branches; stale local refs are not evidence.
        await Run(root, "fetch", "--no-tags", "--prune", "origin",
            "+refs/heads/master:refs/remotes/origin/master",
            "+refs/heads/release/*:refs/remotes/origin/release/*");
        var references = await Run(root, "for-each-ref", "--format=%(refname)", "refs/remotes/origin/master", "refs/remotes/origin/release/");
        foreach (var reference in references.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var branch = reference["refs/remotes/origin/".Length..];
            if (branch != "master" && !(prerelease && IsReleaseBranch(branch))) continue;
            var ancestor = await Git(root, "merge-base", "--is-ancestor", head, reference);
            if (ancestor.ExitCode == 1) continue;
            RequireSuccess(ancestor);
            Console.WriteLine($"Release source accepted: {tag} is contained in origin/{branch}.");
            return;
        }
        throw new InvalidOperationException(prerelease
            ? "The tagged prerelease commit must be pushed to origin/master or origin/release/*."
            : "The tagged stable release commit must be contained in origin/master.");
    }

    private static bool IsReleaseBranch(string branch) =>
        branch.StartsWith("release/", StringComparison.Ordinal) && branch.Length > "release/".Length;

    private static async Task<string> Run(string root, params string[] arguments)
    {
        var result = await Git(root, arguments);
        RequireSuccess(result);
        return result.Output;
    }

    private static void RequireSuccess((int ExitCode, string Output, string Error) result)
    {
        if (result.ExitCode != 0) throw new InvalidOperationException($"Release Git verification failed ({result.ExitCode}): {result.Error}");
    }

    private static async Task<(int ExitCode, string Output, string Error)> Git(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not launch Git for release verification.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, (await output).Trim(), (await error).Trim());
    }

    [GeneratedRegex(@"\A(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-(?<prerelease>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersion();
}
