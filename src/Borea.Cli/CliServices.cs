using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.Settings;
using Borea.Core.State;

namespace Borea.Cli;

/// <summary>
/// The services the commands run against, seen as Borea.Core interfaces.
/// The program fills it from <see cref="BoreaServices"/>.
/// </summary>
internal sealed class CliServices : IDisposable
{
    /// <summary>
    /// The settings the graph was built from. Empty settings when no file was
    /// saved yet.
    /// </summary>
    public required BoreaSettings Settings { get; init; }

    public required IBoreaSettingsRepository SettingsRepository { get; init; }

    public required IGameDirectoryChanger GameDirectoryChanger { get; init; }

    public required IInstanceRepository Instances { get; init; }

    public required IModStateRepository ModState { get; init; }

    public required ILatestVersionPing LatestVersion { get; init; }

    public required IInstalledGameVersionProvider InstalledVersion { get; init; }

    public required IContentIndexFetcher IndexFetcher { get; init; }

    public required IContentIndexReader IndexReader { get; init; }

    public required IGamePathProvider Paths { get; init; }

    public required IModRepository Mods { get; init; }

    public required ILoaderInstaller LoaderInstaller { get; init; }

    public required ILoaderAdopter LoaderAdopter { get; init; }

    public required ILoaderUninstaller LoaderUninstaller { get; init; }

    public required ILauncher Launcher { get; init; }

    /// <summary>
    /// The graph the services came from, disposed with this instance. Null when
    /// nothing needs disposing.
    /// </summary>
    public IDisposable? Graph { get; init; }

    public IDisposable? AdditionalDisposable { get; init; }

    /// <summary>
    /// The graph's services. <paramref name="latestVersion"/> replaces the
    /// graph's master-server ping and <paramref name="installedVersion"/> its
    /// reader of the installed build, which is what a test needs.
    /// </summary>
    public static CliServices From(
        BoreaServices services,
        ILatestVersionPing? latestVersion = null,
        IInstalledGameVersionProvider? installedVersion = null,
        IContentIndexFetcher? indexFetcher = null,
        IContentIndexReader? indexReader = null,
        IModRepository? mods = null,
        ILoaderInstaller? loaderInstaller = null,
        ILoaderAdopter? loaderAdopter = null,
        ILoaderUninstaller? loaderUninstaller = null,
        ILauncher? launcher = null)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        return new CliServices
        {
            Settings = services.Settings,
            SettingsRepository = services.SettingsRepository,
            GameDirectoryChanger = services.GameDirectoryChanger,
            Instances = services.Instances,
            ModState = services.ModState,
            LatestVersion = latestVersion ?? services.LatestVersion,
            InstalledVersion = installedVersion ?? services.InstalledVersion,
            IndexFetcher = indexFetcher ?? services.IndexFetcher,
            IndexReader = indexReader ?? services.IndexReader,
            Paths = services.Paths,
            Mods = mods ?? services.Mods,
            LoaderInstaller = loaderInstaller ?? services.LoaderInstaller,
            LoaderAdopter = loaderAdopter ?? services.LoaderAdopter,
            LoaderUninstaller = loaderUninstaller ?? services.LoaderUninstaller,
            Launcher = launcher ?? services.Launcher,
            Graph = services,
            AdditionalDisposable = launcher is IDisposable disposable && !ReferenceEquals(launcher, services.Launcher)
                ? disposable
                : null,
        };
    }

    public void Dispose()
    {
        AdditionalDisposable?.Dispose();
        Graph?.Dispose();
    }
}
