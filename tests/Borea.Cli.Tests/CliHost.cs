using System.Text.Json;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.Launch;
using Borea.Core.Logging;
using Borea.Core.ModLoaders;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.Settings;
using Borea.Core.Instances;
using Borea.Network.Index;
using Borea.Storage.Instances;
using Borea.Storage.Launch;
using Borea.Storage.Logging;
using Borea.Storage.Mods;
using Borea.Storage.Paths;
using Borea.Storage.Settings;

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

    /// <summary>
    /// The space check the commands install through. It accepts every plan, so
    /// the free space of the machine the tests run on does not reach them.
    /// </summary>
    public IInstallSpaceCheck SpaceCheck { get; set; } = new FakeInstallSpaceCheck();

    public IModRepository? ModRepository { get; set; }

    /// <summary>
    /// The pack repository. The index-backed one over <see cref="IndexReader"/>
    /// when a test does not set it, so the snapshot a test builds is the one source.
    /// </summary>
    public IModPackRepository? ModPacks { get; set; }

    public FakeModPackInstaller ModPackInstaller { get; } = new();

    public Func<BoreaServices, IModPackInstaller>? ModPackInstallerFactory { get; set; }

    public Func<BoreaServices, IModPackUpdater>? ModPackUpdaterFactory { get; set; }

    public FakeProcessStarter ProcessStarter { get; } = new();

    public ILoaderInstaller? LoaderInstaller { get; set; }

    public ILoaderAdopter? LoaderAdopter { get; set; }

    public ILoaderUninstaller? LoaderUninstaller { get; set; }

    public Func<BoreaServices, IModInstaller>? InstallerFactory { get; set; }

    public Func<BoreaServices, IModReplacer>? ReplacerFactory { get; set; }

    public Func<BoreaServices, IModUninstaller>? UninstallerFactory { get; set; }

    public Func<BoreaServices, IForeignModAdopter>? ForeignModAdopterFactory { get; set; }

    public Func<BoreaServices, IForeignModHandover>? ForeignModHandoverFactory { get; set; }

    /// <summary>The shared profile the importer reads, so no test reads the real one.</summary>
    public string SharedProfile => Path.Combine(Root, "GameProfile");

    /// <summary>Downloads the releases the importer compares a copy with. The graph's downloader when a test does not set it.</summary>
    public IModDownloader? Downloader { get; set; }

    public Func<BoreaServices, IInstanceRepository>? InstancesFactory { get; set; }

    /// <summary>Whether the library folder change sees two folders on one volume. Null compares their mount points.</summary>
    public Func<string, string, bool>? LibraryOnSameVolume { get; set; }

    /// <summary>Changes the library folder. A changer over the graph when a test does not set it.</summary>
    public ILibraryFolderChanger? LibraryChanger { get; set; }

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
        var instances = InstancesFactory?.Invoke(graph);
        return CliServices.From(
            graph,
            instances: instances,
            latestVersion: LatestVersion,
            installedVersion: InstalledVersion,
            indexFetcher: IndexFetcher,
            indexReader: IndexReader,
            indexSnapshots: IndexSnapshots ?? new ReaderSnapshotProvider(IndexReader),
            mods: ModRepository ?? Mods,
            readOnlyMods: ModRepository ?? Mods,
            spaceCheck: SpaceCheck,
            installer: InstallerFactory?.Invoke(graph),
            replacer: ReplacerFactory?.Invoke(graph),
            uninstaller: UninstallerFactory?.Invoke(graph),
            foreignModAdopter: ForeignModAdopterFactory?.Invoke(graph),
            foreignModHandover: ForeignModHandoverFactory?.Invoke(graph),
            loaderInstaller: LoaderInstaller,
            loaderAdopter: LoaderAdopter,
            loaderUninstaller: LoaderUninstaller,
            // the fake processes answer at once, so the startup watch needs no real time
            launcher: new LastPlayedLauncher(new LoaderLauncher(graph.Paths, ProcessStarter, TimeSpan.Zero), instances ?? graph.Instances),
            modPacks: ModPacks ?? new ContentIndexModPackRepository(IndexSnapshots ?? new ReaderSnapshotProvider(IndexReader)),
            readOnlyModPacks: ModPacks ?? new ContentIndexModPackRepository(new ReaderSnapshotProvider(IndexReader)),
            modPackInstaller: ModPackInstallerFactory?.Invoke(graph) ?? ModPackInstaller,
            modPackUpdater: ModPackUpdaterFactory?.Invoke(graph),
            sharedProfileLauncher: new SharedProfileLauncher(graph.Paths, ProcessStarter, OsPlatform.Windows),
            indexRefresh: IndexRefresh,
            sharedProfileImporter: BuildSharedProfileImporter(graph),
            // a game or a Borea the developer runs next to the tests must not refuse the move
            libraryFolderChanger: LibraryChanger ?? new LibraryFolderChanger(graph.SettingsRepository, graph.Paths, Root, graph.Launcher, (FileInstanceRepository)((LoggingInstanceRepository)graph.Instances).Inner, isGameProcessRunning: () => false, isOtherBoreaRunning: () => false, isSameVolume: LibraryOnSameVolume));
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
