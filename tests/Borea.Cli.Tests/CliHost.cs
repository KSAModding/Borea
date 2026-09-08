using System.Text.Json;
using Borea.Composition;
using Borea.Storage.Paths;

namespace Borea.Cli.Tests;

/// <summary>
/// Runs command lines against a temporary Borea root the test owns. The
/// services come from the real composition root under that root, with the
/// master server replaced by <see cref="LatestVersion"/> and, when a test
/// sets it, the installed build by <see cref="InstalledVersion"/>.
/// </summary>
internal sealed class CliHost : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());

    public FakeLatestVersionPing LatestVersion { get; } = new();

    public FakeInstalledGameVersionProvider? InstalledVersion { get; set; }

    /// <summary>How many times a command built its services.</summary>
    public int Builds { get; private set; }

    public GamePathProvider Paths => new(gameDirectory: null, boreaRoot: Root);

    public async Task<CliRun> RunAsync(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await BoreaCli.RunAsync(args, BuildAsync, output, error);

        return new CliRun(exitCode, output.ToString(), error.ToString());
    }

    private async Task<CliServices> BuildAsync(CancellationToken cancellationToken)
    {
        Builds++;
        return CliServices.From(await BoreaServices.BuildAsync(Root, cancellationToken), LatestVersion, InstalledVersion);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}

internal sealed record CliRun(int ExitCode, string Output, string Error)
{
    /// <summary>The output parsed as JSON.</summary>
    public JsonElement Json => JsonDocument.Parse(Output).RootElement;
}
