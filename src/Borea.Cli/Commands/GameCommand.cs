using System.CommandLine;
using Borea.Cli.Output;
using Borea.Core.Game;

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

    private static Command BuildVersion(Func<CancellationToken, Task<CliServices>> services)
    {
        var json = ArgumentRules.Json();
        var version = new Command("version", "Print the current public build the master server reports.");
        version.Options.Add(json);

        version.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var latest = await cli.LatestVersion.PingAsync(ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(latest.RawVersion))
                throw new InvalidOperationException("The master server gave no version.");

            var view = LatestVersionView.From(latest);

            if (parseResult.GetValue(json))
            {
                JsonOutput.Write(output, new GameVersionView(view));
                return ExitCodes.Done;
            }

            output.WriteLine(latest.Version is null
                ? $"Latest public build: {latest.RawVersion} (this did not parse as a game version)"
                : $"Latest public build: {latest.Version}");

            if (view.DownloadUrl is not null)
                output.WriteLine($"Download: {view.DownloadUrl}");

            return ExitCodes.Done;
        }));

        return version;
    }

    /// <summary>The JSON shape of <c>game version</c>.</summary>
    private sealed record GameVersionView(LatestVersionView Latest);

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
