using System.Text.Json;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.Logging;
using Borea.Core.ModLoaders;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Instances;
using Borea.Network.Index;
using Borea.Storage.Instances;
using Borea.Storage.Launch;
using Borea.Storage.Mods;
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

    public IContentIndexRefresh? IndexRefresh { get; set; }

    public FakeModRepository Mods { get; } = new();

    public IModRepository? ModRepository { get; set; }

    /// <summary>
    /// The pack repository. The index-backed one over <see cref="IndexReader"/>
    /// when a test does not set it, so the snapshot a test builds is the one source.
    /// </summary>
    public IModPackRepository? ModPacks { get; set; }

    public FakeModPackInstaller ModPackInstaller { get; } = new();

    public Func<BoreaServices, IModPackInstaller>? ModPackInstallerFactory { get; set; }

    public FakeProcessStarter ProcessStarter { get; } = new();

    public ILoaderInstaller? LoaderInstaller { get; set; }

    public ILoaderAdopter? LoaderAdopter { get; set; }

    public ILoaderUninstaller? LoaderUninstaller { get; set; }

    public Func<BoreaServices, IModInstaller>? InstallerFactory { get; set; }

    public Func<BoreaServices, IModReplacer>? ReplacerFactory { get; set; }

    public Func<BoreaServices, IModUninstaller>? UninstallerFactory { get; set; }

    public Func<BoreaServices, IForeignModAdopter>? ForeignModAdopterFactory { get; set; }

    /// <summary>The shared profile the importer reads, so no test reads the real one.</summary>
    public string SharedProfile => Path.Combine(Root, "GameProfile");

    /// <summary>Downloads the releases the importer compares a copy with. The graph's downloader when a test does not set it.</summary>
    public IModDownloader? Downloader { get; set; }

    public Func<BoreaServices, IInstanceRepository>? InstancesFactory { get; set; }

    /// <summary>How many times a command built its services.</summary>
    public int Builds { get; private set; }

    public GamePathProvider Paths => new(gameDirectory: null, boreaRoot: Root, sharedProfileRoot: SharedProfile);

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
        var graph = await BoreaServices.BuildAsync(Root, BoreaLogSource.Cli, cancellationToken);
        return CliServices.From(
            graph,
            instances: InstancesFactory?.Invoke(graph),
            latestVersion: LatestVersion,
            installedVersion: InstalledVersion,
            indexFetcher: IndexFetcher,
            indexReader: IndexReader,
            indexSnapshots: IndexSnapshots ?? new ReaderSnapshotProvider(IndexReader),
            mods: ModRepository ?? Mods,
            readOnlyMods: ModRepository ?? Mods,
            installer: InstallerFactory?.Invoke(graph),
            replacer: ReplacerFactory?.Invoke(graph),
            uninstaller: UninstallerFactory?.Invoke(graph),
            foreignModAdopter: ForeignModAdopterFactory?.Invoke(graph),
            loaderInstaller: LoaderInstaller,
            loaderAdopter: LoaderAdopter,
            loaderUninstaller: LoaderUninstaller,
            // the fake processes answer at once, so the startup watch needs no real time
            launcher: new LoaderLauncher(graph.Paths, ProcessStarter, TimeSpan.Zero),
            modPacks: ModPacks ?? new ContentIndexModPackRepository(IndexSnapshots ?? new ReaderSnapshotProvider(IndexReader)),
            readOnlyModPacks: ModPacks ?? new ContentIndexModPackRepository(new ReaderSnapshotProvider(IndexReader)),
            modPackInstaller: ModPackInstallerFactory?.Invoke(graph) ?? ModPackInstaller,
            sharedProfileLauncher: new SharedProfileLauncher(graph.Paths, ProcessStarter, OsPlatform.Windows),
            indexRefresh: IndexRefresh,
            sharedProfileImporter: BuildSharedProfileImporter(graph));
    }

    private FileSharedProfileImporter BuildSharedProfileImporter(BoreaServices graph)
    {
        var snapshots = IndexSnapshots ?? new ReaderSnapshotProvider(IndexReader);
        var adopter = new FileForeignModAdopter(graph.Paths, graph.Instances, new ContentIndexModRepository(snapshots));
        var matcher = new FileForeignModReleaseMatcher(graph.Paths, Downloader ?? graph.Downloader, adopter, snapshots);
        return new FileSharedProfileImporter(Paths, graph.Instances, graph.ModState, adopter, matcher);
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
