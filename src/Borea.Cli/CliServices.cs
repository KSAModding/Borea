using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.Instances;
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

    public required IInstanceRepository Instances { get; init; }

    public required IModStateRepository ModState { get; init; }

    public required ILatestVersionPing LatestVersion { get; init; }

    public required IInstalledGameVersionProvider InstalledVersion { get; init; }

    /// <summary>
    /// The graph the services came from, disposed with this instance. Null when
    /// nothing needs disposing.
    /// </summary>
    public IDisposable? Graph { get; init; }

    /// <summary>
    /// The graph's services. <paramref name="latestVersion"/> replaces the
    /// graph's master-server ping and <paramref name="installedVersion"/> its
    /// reader of the installed build, which is what a test needs.
    /// </summary>
    public static CliServices From(BoreaServices services, ILatestVersionPing? latestVersion = null, IInstalledGameVersionProvider? installedVersion = null)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        return new CliServices
        {
            Settings = services.Settings,
            SettingsRepository = services.SettingsRepository,
            Instances = services.Instances,
            ModState = services.ModState,
            LatestVersion = latestVersion ?? services.LatestVersion,
            InstalledVersion = installedVersion ?? services.InstalledVersion,
            Graph = services,
        };
    }

    public void Dispose() => Graph?.Dispose();
}
