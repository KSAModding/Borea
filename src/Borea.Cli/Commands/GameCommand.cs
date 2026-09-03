using System.CommandLine;
using Borea.Cli.Output;
using Borea.Core.Game;
using Borea.Core.Settings;

namespace Borea.Cli.Commands;

/// <summary>
/// <c>borea game</c>: facts about the game installation.
/// </summary>
internal static class GameCommand
{
    public static Command Build(Func<CancellationToken, Task<CliServices>> services)
    {
        var game = new Command("game", "Read facts about the game installation.");
        game.Subcommands.Add(BuildVersion(services));
        return game;
    }

    /// <summary>
    /// the installed build comes from disk, the latest from the master server.
    /// </summary>
    private static Command BuildVersion(Func<CancellationToken, Task<CliServices>> services)
    {
        var json = ArgumentRules.Json();
        var version = new Command("version", "Print the installed build and the current public build the master server reports.");
        version.Options.Add(json);

        version.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, error, ct) =>
        {
            var installed = cli.InstalledVersion.GetInstalledVersion();
            var (latest, latestProblem) = await AskMasterServerAsync(cli.LatestVersion, ct).ConfigureAwait(false);

            if (installed is null && latest is null)
                throw new InvalidOperationException($"The installed build is unknown: {InstalledUnknownReason(cli.Settings)}. {latestProblem}");

            if (latestProblem is not null)
                error.WriteLine($"warning: {latestProblem}");

            if (parseResult.GetValue(json))
            {
                JsonOutput.Write(output, new GameVersionView(
                    installed is null ? null : InstalledVersionView.From(installed),
                    latest is null ? null : LatestVersionView.From(latest)));
                return ExitCodes.Done;
            }

            output.WriteLine(installed switch
            {
                null => $"Installed build: unknown ({InstalledUnknownReason(cli.Settings)})",
                { Version: null } => $"Installed build: {installed.RawVersion} (this did not parse as a game version)",
                _ => $"Installed build: {installed.Version}",
            });

            if (latest is not null)
            {
                output.WriteLine(latest.Version is null
                    ? $"Latest public build: {latest.RawVersion} (this did not parse as a game version)"
                    : $"Latest public build: {latest.Version}");

                if (!string.IsNullOrWhiteSpace(latest.DownloadUrl))
                    output.WriteLine($"Download: {latest.DownloadUrl}");
            }

            if (installed?.Version is { } have && latest?.Version is { } current)
                output.WriteLine(have < current ? "A newer build is available." : "The installed build is current.");

            return ExitCodes.Done;
        }));

        return version;
    }

    /// <summary>
    /// The master server's answer.
    /// </summary>
    private static async Task<(LatestVersionInfo? Latest, string? Problem)> AskMasterServerAsync(ILatestVersionPing ping, CancellationToken cancellationToken)
    {
        LatestVersionInfo answer;
        try
        {
            answer = await ping.PingAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            return (null, $"The master server could not be reached. {exception.Message}");
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, $"The master server did not answer in time. {exception.Message}");
        }

        return string.IsNullOrWhiteSpace(answer.RawVersion)
            ? (null, "The master server gave no version.")
            : (answer, null);
    }

    private static string InstalledUnknownReason(BoreaSettings settings)
        => settings.GameDirectoryPath is null
            ? "no game directory is set, run 'borea settings set game'"
            : $"no KSA.dll with a version was found in {settings.GameDirectoryPath}";

    /// <summary>The JSON shape of <c>game version</c>. A half that is missing is null.</summary>
    private sealed record GameVersionView(InstalledVersionView? Installed, LatestVersionView? Latest);

    private sealed record InstalledVersionView(string? Version, int? Revision, string Raw)
    {
        public static InstalledVersionView From(InstalledGameVersion installed)
            => new(installed.Version?.ToString(), installed.Version?.Revision, installed.RawVersion);
    }

    /// <summary>
    /// The master server's answer. An answer without a download page carries
    /// null, not the empty string the ping keeps.
    /// </summary>
    private sealed record LatestVersionView(string? Version, int? Revision, string Raw, string? DownloadUrl)
    {
        public static LatestVersionView From(LatestVersionInfo latest)
            => new(
                latest.Version?.ToString(),
                latest.Version?.Revision,
                latest.RawVersion,
                string.IsNullOrWhiteSpace(latest.DownloadUrl) ? null : latest.DownloadUrl);
    }
}
