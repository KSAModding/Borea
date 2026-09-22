using System.CommandLine;
using System.Globalization;
using Borea.Cli.Output;
using Borea.Core.Updates;

namespace Borea.Cli.Commands;

/// <summary>
/// <c>borea update-self</c>: replaces this build with the newest release of the update channel the
/// settings name, after the download was checked against the checksums of the same release. Borea
/// keeps the folder it runs in, so a shortcut and a PATH entry go on working.
/// </summary>
internal static class SelfUpdateCommand
{
    public static Command Build(Func<CancellationToken, Task<CliServices>> services)
    {
        var check = new Option<bool>("--check") { Description = "Only report which release would be installed." };
        var json = ArgumentRules.Json();
        var command = new Command("update-self", "Replace this Borea build with the newest release of your update channel.");
        command.Options.Add(check);
        command.Options.Add(json);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, error, ct) =>
        {
            var preferences = await cli.AppPreferences.GetAsync([], ct).ConfigureAwait(false);
            var channel = preferences.Preferences.UpdateChannel;
            var running = BoreaBuild.Version;

            var releases = await cli.ReleaseCheck.GetReleasesAsync(channel, ct).ConfigureAwait(false);
            var newest = releases.FirstOrDefault();
            if (newest is null || !newest.IsNewerThan(running))
            {
                Report(output, parseResult.GetValue(json), new SelfUpdateView(running, channel.ToString(), null, "current", null));
                return ExitCodes.Done;
            }

            var readiness = cli.SelfUpdater.GetReadiness();
            if (!readiness.CanUpdate)
            {
                error.WriteLine("error: " + BlockText(readiness));
                return ExitCodes.Failed;
            }

            if (parseResult.GetValue(check))
            {
                Report(output, parseResult.GetValue(json), new SelfUpdateView(running, channel.ToString(), newest.Version.ToString(), "available", null));
                return ExitCodes.Done;
            }

            var staged = await cli.SelfUpdater.StageAsync(newest, new SelfUpdateOutput(error), ct).ConfigureAwait(false);
            staged.Install();
            staged.HandOver();
            Report(output, parseResult.GetValue(json), new SelfUpdateView(running, channel.ToString(), staged.Version.ToString(), "installed", staged.Folder));
            return ExitCodes.Done;
        }));

        return command;
    }

    /// <summary>The blocks in the words of the command line. The App says the same in the player's language.</summary>
    private static string BlockText(SelfUpdateReadiness readiness) => readiness.Block switch
    {
        SelfUpdateBlock.PackageManaged when readiness.PackageManager is { } manager
            => $"{manager} installed this Borea build, so update it with {manager}.",
        SelfUpdateBlock.PackageManaged
            => "A package manager installed this Borea build, so update it with that package manager.",
        SelfUpdateBlock.ReadOnlyLocation
            => "Borea cannot write to its own folder, so it cannot replace itself. Unpack the new release by hand.",
        SelfUpdateBlock.UnsupportedPlatform
            => "Borea publishes no release for this operating system and processor.",
        _ => "This build did not come from a release archive, so it cannot replace itself.",
    };

    private static void Report(TextWriter output, bool json, SelfUpdateView view)
    {
        if (json)
        {
            JsonOutput.Write(output, view);
            return;
        }

        switch (view.State)
        {
            case "current":
                output.WriteLine($"Borea {view.Running} is the newest release in the {view.Channel} channel.");
                break;
            case "available":
                output.WriteLine($"Borea {view.Available} is available. Run borea update-self to install it.");
                break;
            default:
                output.WriteLine($"Borea {view.Available} is installed in {view.Folder}.");
                output.WriteLine("It starts now and removes the program file it took the place of.");
                break;
        }
    }

    /// <summary>Writes the download progress to stderr, so that a piped output holds the result alone.</summary>
    private sealed class SelfUpdateOutput(TextWriter error) : IProgress<SelfUpdateProgress>
    {
        private SelfUpdatePhase? _reported;

        private int _percent = -1;

        public void Report(SelfUpdateProgress value)
        {
            if (value.Phase == SelfUpdatePhase.Downloading)
            {
                // one line per tenth, because a line per percent would bury the result
                var percent = (int)value.PercentComplete / 10 * 10;
                if (value.TotalBytes <= 0 || percent == _percent)
                    return;

                _percent = percent;
                error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Downloading {percent}%"));
                return;
            }

            if (_reported == value.Phase)
                return;

            _reported = value.Phase;
            error.WriteLine(value.Phase == SelfUpdatePhase.Verifying ? "Checking the download" : "Unpacking");
        }
    }

    /// <param name="State">One of "current", "available" and "installed".</param>
    private sealed record SelfUpdateView(string Running, string Channel, string? Available, string State, string? Folder);
}
