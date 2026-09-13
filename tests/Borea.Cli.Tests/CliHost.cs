using System.Text.Json;
using Borea.Composition;
using Borea.Core.Index;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Storage.Launch;
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

    public FakeContentIndexFetcher IndexFetcher { get; } = new();

    public FakeContentIndexReader IndexReader { get; } = new();

    public IContentIndexSnapshotProvider? IndexSnapshots { get; set; }

    public FakeModRepository Mods { get; } = new();

    public IModRepository? ModRepository { get; set; }

    public FakeProcessStarter ProcessStarter { get; } = new();

    public ILoaderInstaller? LoaderInstaller { get; set; }

    public ILoaderAdopter? LoaderAdopter { get; set; }

    public ILoaderUninstaller? LoaderUninstaller { get; set; }

    /// <summary>How many times a command built its services.</summary>
    public int Builds { get; private set; }

    public GamePathProvider Paths => new(gameDirectory: null, boreaRoot: Root);

    public async Task<CliRun> RunAsync(params string[] args)
        => await RunAsync(CancellationToken.None, args);

    public async Task<CliRun> RunAsync(CancellationToken cancellationToken, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await BoreaCli.RunAsync(args, BuildAsync, output, error, cancellationToken);

        return new CliRun(exitCode, output.ToString(), error.ToString());
    }

    private async Task<CliServices> BuildAsync(CancellationToken cancellationToken)
    {
        Builds++;
        var graph = await BoreaServices.BuildAsync(Root, cancellationToken);
        return CliServices.From(
            graph,
            latestVersion: LatestVersion,
            installedVersion: InstalledVersion,
            indexFetcher: IndexFetcher,
            indexReader: IndexReader,
            indexSnapshots: IndexSnapshots ?? new ReaderSnapshotProvider(IndexReader),
            mods: ModRepository ?? Mods,
            loaderInstaller: LoaderInstaller,
            loaderAdopter: LoaderAdopter,
            loaderUninstaller: LoaderUninstaller,
            launcher: new LoaderLauncher(graph.Paths, ProcessStarter));
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }

    private sealed class ReaderSnapshotProvider(IContentIndexReader reader) : IContentIndexSnapshotProvider
    {
        public Task<ContentIndexSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            reader.ReadAsync(cancellationToken);
    }
}

internal sealed record CliRun(int ExitCode, string Output, string Error)
{
    /// <summary>The output parsed as JSON.</summary>
    public JsonElement Json => JsonDocument.Parse(Output).RootElement;
}
