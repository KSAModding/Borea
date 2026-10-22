using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The Mod loader section of the instance page: the loader version that starts the instance, how the
/// instance chooses it, which versions the mods accept, and whether that version is installed.
/// </summary>
public partial class MainViewModel
{
    /// <summary>The section of the shown instance. Null until an instance is open.</summary>
    [ObservableProperty]
    private InstanceLoaderSection? _instanceLoader;

    /// <summary>What the launch would start for the shown instance, with the id of that instance. Null when no listings could be read.</summary>
    private (Guid InstanceId, InstanceLoaderResolution Resolution)? _instanceLoaderResolution;

    private int _instanceLoaderGeneration;

    /// <summary>
    /// Chooses the loader version of the shown instance the way Play does, then builds the section again.
    /// A later call wins over an earlier one that is still reading.
    /// </summary>
    private async Task LoadInstanceLoaderAsync()
    {
        var generation = ++_instanceLoaderGeneration;
        (Guid, InstanceLoaderResolution)? found = null;
        if (_services is { } services && _selectedInstanceEntity is { } instance)
        {
            try
            {
                found = (instance.InstanceId, await ResolveLaunchLoaderAsync(services, instance));
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or HttpRequestException or TaskCanceledException)
            {
                // without listings the section shows only what the mods ask for, and Play says why it cannot start
            }
        }

        if (generation != _instanceLoaderGeneration)
            return;

        _instanceLoaderResolution = found;
        RefreshInstanceLoader();
    }

    private void RefreshInstanceLoader()
    {
        if (_services is not { } services || _selectedInstanceEntity is not { } instance)
        {
            InstanceLoader = null;
            return;
        }

        var need = LoaderNeed.For(instance);
        var names = _content.ToDictionary(item => item.ModId, item => item.Name, ModIds.Comparer);
        var resolution = _instanceLoaderResolution is { } found && found.InstanceId == instance.InstanceId ? found.Resolution : null;
        var rows = new List<InstanceLoaderRow>();

        // the loader that starts the instance comes first, also when no mod needs it or it is not installed yet
        if (resolution?.Version is { } version)
            rows.Add(Row(version.LoaderId, version));

        // the other loaders the mods name, which this launch does not start
        foreach (var loader in need.Loaders.Where(loader => !ModIds.Equals(loader.LoaderId, resolution?.Version?.LoaderId)))
            rows.Add(Row(loader.LoaderId, version: null));

        InstanceLoader = new InstanceLoaderSection(this, need, rows);

        InstanceLoaderRow Row(string loaderId, LoaderVersionResolution? version) => new(
            this,
            loaderId,
            need.Loaders.FirstOrDefault(loader => ModIds.Equals(loader.LoaderId, loaderId)),
            _listings.FirstOrDefault(listing => listing.Type == ContentType.ModLoader && ModIds.Equals(listing.ModId, loaderId)),
            services.Settings.LoaderInstallations.FirstOrDefault(entry => ModIds.Equals(entry.Key, loaderId)).Value,
            LoaderUpdateFor(loaderId),
            id => names.TryGetValue(id, out var name) ? name : id,
            version,
            instance.Loader);
    }

    /// <summary>"0.4.5 to 0.8.0" or "0.4.5 or newer", the versions one mod accepts.</summary>
    internal string LoaderBoundsText(ModVersion min, ModVersion? max) => max is { } upper
        ? Localization.FormatInstanceLoaderBoundsRange(min.ToString(), upper.ToString())
        : Localization.FormatInstanceLoaderBoundsMinimum(min.ToString());

    /// <summary>
    /// Installs one release of a loader beside the installed versions, as the usual loader install task, and tells the
    /// player when it fails. False when it did not install.
    /// </summary>
    internal async Task<bool> InstallLoaderVersionAsync(string loaderId, ModVersionMetadata release)
    {
        if (await UpdateLoaderToAsync(loaderId, release))
            return true;

        // the modal shows the error itself, and a row has no place for it
        if (SetupError is { } error && !IsLoaderSettingModalOpen && !IsLoaderPromptOpen)
            ShowErrorToast(() => Localization.FormatToastInstallFailed($"{loaderId} {release.Version}"), error);
        return false;
    }
}

/// <summary>The Mod loader section of one instance.</summary>
public sealed partial class InstanceLoaderSection
{
    private readonly MainViewModel _owner;
    private readonly LoaderNeed _need;

    public IReadOnlyList<InstanceLoaderRow> Rows { get; }

    public InstanceLoaderSection(MainViewModel owner, LoaderNeed need, IReadOnlyList<InstanceLoaderRow> rows)
    {
        _owner = owner;
        _need = need;
        Rows = rows;
    }

    public string Title => _owner.Localization.InstanceLoaderTitle;

    /// <summary>
    /// The line for an instance whose mods need no loader, when Borea could not read which loader starts it.
    /// Otherwise the row of that loader says it.
    /// </summary>
    public string? NoneText => _need.IsNone && Rows.Count == 0 ? _owner.Localization.InstanceLoaderNone : null;

    /// <summary>The line when the mods need more than one loader, which one launch cannot start.</summary>
    public string? DifferentText => _need.NeedsDifferentLoaders
        ? _owner.Localization.FormatInstanceLoaderDifferent(string.Join(", ", _need.Loaders.Select(loader => Rows.FirstOrDefault(row => row.IsFor(loader.LoaderId))?.Name ?? loader.LoaderId)))
        : null;

    /// <summary>Opens the modal that changes which loader version starts the instance.</summary>
    [RelayCommand]
    private Task ChangeAsync() => _owner.SelectedInstance is { } instance ? _owner.BeginEditLoaderSettingAsync(instance) : Task.CompletedTask;
}

public enum InstanceLoaderState
{
    /// <summary>Installed, in a version every mod accepts.</summary>
    Installed,

    /// <summary>Installed, and Borea does not know which version it is.</summary>
    VersionUnknown,

    /// <summary>Installed, in a release that the index says does not support the game.</summary>
    GameIncompatible,

    /// <summary>Installed, in a version outside what the mods accept.</summary>
    WrongVersion,

    /// <summary>Not installed.</summary>
    NotInstalled,

    /// <summary>The mods ask for versions that do not overlap.</summary>
    Conflict,
}

/// <summary>
/// One loader of the instance. The first row is the loader version that starts it, chosen as Play chooses it.
/// Further rows are other loaders the mods name, which that launch does not start.
/// </summary>
public sealed partial class InstanceLoaderRow : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly string _loaderId;
    private readonly NeededLoader? _loader;
    private readonly DiscoverItem? _listing;
    private readonly Func<string, string> _nameOf;
    private readonly ModVersion? _version;
    private readonly ModVersionMetadata? _offer;
    private readonly ModVersionMetadata? _missing;
    private readonly LoaderUpdate? _update;
    private readonly LoaderVersionResolution? _resolution;
    private readonly LoaderSetting? _setting;

    /// <summary>A row for a loader the mods need, rated from the recorded installation as the update check rates it.</summary>
    /// <param name="update">The last loader update check's answer for this loader, or null before it answered.</param>
    /// <param name="nameOf">The display name of a mod id, as its row on the page shows it.</param>
    public InstanceLoaderRow(MainViewModel owner, NeededLoader loader, DiscoverItem? listing, LoaderInstallation? installation, LoaderUpdate? update, Func<string, string> nameOf)
        : this(owner, loader.LoaderId, loader, listing, installation, update, nameOf, resolution: null, setting: null)
    {
    }

    /// <param name="loader">What the mods accept of this loader, or null when no mod needs it.</param>
    /// <param name="update">The last loader update check's answer for this loader, or null before it answered.</param>
    /// <param name="nameOf">The display name of a mod id, as its row on the page shows it.</param>
    /// <param name="resolution">The version that starts the instance, or null for a loader that this launch does not start.</param>
    /// <param name="setting">The loader setting of the instance, which names a pinned version also when it is not installed.</param>
    public InstanceLoaderRow(MainViewModel owner, string loaderId, NeededLoader? loader, DiscoverItem? listing, LoaderInstallation? installation, LoaderUpdate? update, Func<string, string> nameOf, LoaderVersionResolution? resolution, LoaderSetting? setting)
    {
        _owner = owner;
        _loaderId = loaderId;
        _loader = loader;
        _listing = listing;
        _nameOf = nameOf;
        _resolution = resolution;
        _setting = setting;
        Name = listing?.Name ?? loaderId;
        Icon = listing?.Icon;

        if (resolution is not null)
        {
            var start = resolution.Start;

            // the update check reads the version of a recorded folder from its files when the settings do not know it
            _version = start is null ? null : start.Version ?? (start.IsRecorded ? update?.InstalledVersion : null);
            State = StateOf(loader, start is not null, _version, resolution.StartCompatibility);

            // the update check rated the recorded version, so its answer explains only that one
            _update = start is { IsRecorded: true } && update is not null
                ? update
                : new LoaderUpdate(loaderId, _version, resolution.StartCompatibility, resolution.Offer);
            if (!IsConflict)
            {
                _offer = start is null ? null : resolution.Offer;
                _missing = start is null ? resolution.Missing : null;
            }

            return;
        }

        _update = update;
        _version = installation is null ? null : installation.Version ?? update?.InstalledVersion;
        State = StateOf(loader, installation is not null, _version, update?.InstalledCompatibility ?? GameCompatibility.Unknown);

        // like a mod row, an update shows in place when the mods accept the new release
        if (State is InstanceLoaderState.Installed or InstanceLoaderState.GameIncompatible && update?.Offer is { } offer && (loader is null || loader.Accepts(offer.Version)))
            _offer = offer;
    }

    public string Name { get; }

    public ListingImage? Icon { get; }

    /// <summary>True when this row is about <paramref name="loaderId"/>.</summary>
    internal bool IsFor(string loaderId) => ModIds.Equals(_loaderId, loaderId);

    /// <summary>Whether the row links to the loader's page, like a mod row does. A loader the index does not list has none.</summary>
    public bool CanOpen => _listing is not null;

    /// <summary>Why the row has no link, for the tooltip of its icon and name, the way a mod row says it. Null when it links.</summary>
    public string? NoPageText => CanOpen ? null : _owner.Localization.InstanceLoaderNotInIndex;

    /// <summary>Opens the loader's page with the way back to this instance, the way a mod row opens its page.</summary>
    [RelayCommand]
    private Task OpenAsync() => _listing is null ? Task.CompletedTask : _owner.OpenContentFromInstanceAsync(_listing);

    public InstanceLoaderState State { get; }

    /// <summary>True when the setting of the instance keeps an exact version of this loader.</summary>
    public bool IsPinned => _resolution?.IsPinned == true;

    /// <summary>
    /// "Automatic: 0.4.8", the version that the instance starts or would start once it is installed, or "Pinned at 0.4.7".
    /// Null on a row that does not start the instance, and when Automatic has no version to name.
    /// </summary>
    public string? ModeText
    {
        get
        {
            if (_resolution is null)
                return null;

            if (_resolution.IsPinned)
            {
                var pinned = _setting?.Version ?? _version ?? _resolution.Missing?.Version;
                return pinned is { } version ? _owner.Localization.FormatInstanceLoaderPinned(version.ToString()) : null;
            }

            return (_version ?? _missing?.Version) is { } automatic ? _owner.Localization.FormatInstanceLoaderAutomatic(automatic.ToString()) : null;
        }
    }

    /// <summary>
    /// "Needs 0.4.5 or newer", or "Needs 0.4.5 to 0.8.0". On a conflict the combined bounds hold no version,
    /// so the line names what each mod asks for instead. Null when no mod needs the loader.
    /// </summary>
    public string? RangeText => _loader is not { } loader
        ? null
        : IsConflict
            ? string.Join("; ", loader.Requirements.Select(requirement =>
                _owner.Localization.FormatInstanceLoaderModNeeds(_nameOf(requirement.ModId), _owner.LoaderBoundsText(requirement.MinVersion, requirement.MaxVersion))))
            : loader.MaxVersion is { } max
                ? _owner.Localization.FormatInstanceLoaderRange(loader.MinVersion.ToString(), max.ToString())
                : _owner.Localization.FormatInstanceLoaderMinimum(loader.MinVersion.ToString());

    /// <summary>The mods that need the loader. Null on a conflict, where the range line already names them, and when no mod needs it.</summary>
    public string? NeededByText => _loader is not { } loader || IsConflict
        ? null
        : _owner.Localization.FormatInstanceLoaderNeededBy(string.Join(", ", loader.NeededBy.Select(_nameOf)));

    /// <summary>
    /// The line for a loader that no mod needs. The instance still starts through it,
    /// because only a loader points the game at an instance.
    /// </summary>
    public string? NoneText => _loader is null ? _owner.Localization.InstanceLoaderNone : null;

    /// <summary>A short label, so the chip keeps the size of the other chips on the page.</summary>
    public string StatusText => State switch
    {
        InstanceLoaderState.Installed => _owner.Localization.FormatInstanceLoaderInstalled(_version!.Value.ToString()),
        InstanceLoaderState.VersionUnknown => _owner.Localization.InstanceLoaderVersionUnknown,
        InstanceLoaderState.GameIncompatible => _owner.Localization.FormatInstanceLoaderIncompatible(_version!.Value.ToString()),
        InstanceLoaderState.WrongVersion => _owner.Localization.FormatInstanceLoaderWrongVersion(_version!.Value.ToString()),
        InstanceLoaderState.NotInstalled => _owner.Localization.InstanceLoaderNotInstalled,
        _ => _owner.Localization.InstanceLoaderConflictShort,
    };

    /// <summary>The sentence behind a short chip. Null where the chip says it all.</summary>
    public string? StatusToolTip => State switch
    {
        InstanceLoaderState.Conflict => _owner.Localization.InstanceLoaderConflict,
        InstanceLoaderState.GameIncompatible when _update is not null => _owner.LoaderIncompatibleText(Name, _update),
        _ => null,
    };

    public bool IsInstalled => State == InstanceLoaderState.Installed;

    public bool IsWarning => State is InstanceLoaderState.WrongVersion or InstanceLoaderState.NotInstalled;

    public bool IsConflict => State == InstanceLoaderState.Conflict;

    /// <summary>A conflict or a release that does not support the game shows as a danger.</summary>
    public bool IsDanger => State is InstanceLoaderState.Conflict or InstanceLoaderState.GameIncompatible;

    /// <summary>"Update to 0.4.8" when a newer release that fits the game and the mods is offered, else null.</summary>
    public string? UpdateText => _offer is { } offer ? _owner.Localization.FormatSetupUpdateLoader(offer.Version.ToString()) : null;

    public bool CanUpdate => _offer is not null;

    /// <summary>"Install 0.4.8" when the version that would start the instance is not installed and the index offers it, else null.</summary>
    public string? InstallText => _missing is { } missing ? _owner.Localization.FormatInstanceLoaderInstall(missing.Version.ToString()) : null;

    public bool CanInstall => _missing is not null;

    /// <summary>True while this row installs a release, so the row shows the progress of the install.</summary>
    [ObservableProperty]
    private bool _isInstalling;

    [RelayCommand]
    private async Task UpdateAsync()
    {
        if (_offer is { } offer)
            await InstallAsync(offer);
    }

    [RelayCommand]
    private async Task InstallMissingAsync()
    {
        if (_missing is { } missing)
            await InstallAsync(missing);
    }

    private async Task InstallAsync(ModVersionMetadata release)
    {
        IsInstalling = true;
        try
        {
            await _owner.InstallLoaderVersionAsync(_loaderId, release);
        }
        finally
        {
            IsInstalling = false;
        }
    }

    private static InstanceLoaderState StateOf(NeededLoader? loader, bool isInstalled, ModVersion? version, GameCompatibility compatibility)
    {
        if (loader?.HasConflict == true)
            return InstanceLoaderState.Conflict;
        if (!isInstalled)
            return InstanceLoaderState.NotInstalled;
        if (version is not { } known)
            return InstanceLoaderState.VersionUnknown;
        if (compatibility == GameCompatibility.Incompatible)
            return InstanceLoaderState.GameIncompatible;
        return loader is null || loader.Accepts(known) ? InstanceLoaderState.Installed : InstanceLoaderState.WrongVersion;
    }
}
