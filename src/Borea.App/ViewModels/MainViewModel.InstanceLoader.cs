using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The Mod loader section of the instance page: which loader the mods need,
/// which versions of it they accept, and whether that is what is installed.
/// </summary>
public partial class MainViewModel
{
    /// <summary>The section of the shown instance. Null until an instance is open.</summary>
    [ObservableProperty]
    private InstanceLoaderSection? _instanceLoader;

    private void RefreshInstanceLoader()
    {
        if (_services is null || _selectedInstanceEntity is not { } instance)
        {
            InstanceLoader = null;
            return;
        }

        var need = LoaderNeed.For(instance);
        var names = _content.ToDictionary(item => item.ModId, item => item.Name, ModIds.Comparer);
        var rows = need.Loaders.Select(loader => new InstanceLoaderRow(
            this,
            loader,
            _listings.FirstOrDefault(listing => listing.Type == ContentType.ModLoader && ModIds.Equals(listing.ModId, loader.LoaderId)),
            _services.Settings.LoaderInstallations.FirstOrDefault(entry => ModIds.Equals(entry.Key, loader.LoaderId)).Value,
            loader.NeededBy.Select(id => names.TryGetValue(id, out var name) ? name : id).ToList())).ToList();

        InstanceLoader = new InstanceLoaderSection(this, need, rows);
    }

    /// <summary>The loader rows lead to the Game settings, where a loader is installed or updated.</summary>
    [RelayCommand]
    private Task OpenLoaderSettingsAsync() => OpenGameSetupAsync();
}

/// <summary>The Mod loader section of one instance.</summary>
public sealed class InstanceLoaderSection
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
    /// The line for an instance whose mods need no loader. It still starts
    /// through one, because only a loader points the game at an instance.
    /// </summary>
    public string? NoneText => _need.IsNone ? _owner.Localization.InstanceLoaderNone : null;

    /// <summary>The line when the mods need more than one loader, which one launch cannot start.</summary>
    public string? DifferentText => _need.NeedsDifferentLoaders
        ? _owner.Localization.FormatInstanceLoaderDifferent(string.Join(", ", Rows.Select(row => row.Name)))
        : null;
}

public enum InstanceLoaderState
{
    /// <summary>Installed, in a version every mod accepts.</summary>
    Installed,

    /// <summary>Installed, and Borea does not know which version it is.</summary>
    VersionUnknown,

    /// <summary>Installed, in a version outside what the mods accept.</summary>
    WrongVersion,

    /// <summary>Not installed.</summary>
    NotInstalled,

    /// <summary>The mods ask for versions that do not overlap.</summary>
    Conflict,
}

/// <summary>One loader the mods of the instance need.</summary>
public sealed class InstanceLoaderRow
{
    private readonly MainViewModel _owner;
    private readonly NeededLoader _loader;
    private readonly LoaderInstallation? _installation;
    private readonly IReadOnlyList<string> _neededBy;

    public InstanceLoaderRow(MainViewModel owner, NeededLoader loader, DiscoverItem? listing, LoaderInstallation? installation, IReadOnlyList<string> neededBy)
    {
        _owner = owner;
        _loader = loader;
        _installation = installation;
        _neededBy = neededBy;
        Name = listing?.Name ?? loader.LoaderId;
        Icon = listing?.Icon;
        State = StateOf(loader, installation);
    }

    public string Name { get; }

    public ListingImage? Icon { get; }

    public InstanceLoaderState State { get; }

    /// <summary>"0.4.5 or newer", or "0.4.5 to 0.8.0".</summary>
    public string RangeText => _loader.MaxVersion is { } max
        ? _owner.Localization.FormatInstanceLoaderRange(_loader.MinVersion.ToString(), max.ToString())
        : _owner.Localization.FormatInstanceLoaderMinimum(_loader.MinVersion.ToString());

    public string NeededByText => _owner.Localization.FormatInstanceLoaderNeededBy(string.Join(", ", _neededBy));

    public string StatusText => State switch
    {
        InstanceLoaderState.Installed => _owner.Localization.FormatInstanceLoaderInstalled(_installation!.Version!.Value.ToString()),
        InstanceLoaderState.VersionUnknown => _owner.Localization.InstanceLoaderVersionUnknown,
        InstanceLoaderState.WrongVersion => _owner.Localization.FormatInstanceLoaderWrongVersion(_installation!.Version!.Value.ToString()),
        InstanceLoaderState.NotInstalled => _owner.Localization.InstanceLoaderNotInstalled,
        _ => _owner.Localization.InstanceLoaderConflict,
    };

    public bool IsInstalled => State == InstanceLoaderState.Installed;

    public bool IsWarning => State is InstanceLoaderState.WrongVersion or InstanceLoaderState.NotInstalled;

    public bool IsConflict => State == InstanceLoaderState.Conflict;

    /// <summary>A loader that is missing or in the wrong version is fixed in the Game settings.</summary>
    public bool CanFix => IsWarning;

    private static InstanceLoaderState StateOf(NeededLoader loader, LoaderInstallation? installation)
    {
        if (loader.HasConflict)
            return InstanceLoaderState.Conflict;
        if (installation is null)
            return InstanceLoaderState.NotInstalled;
        if (installation.Version is not { } version)
            return InstanceLoaderState.VersionUnknown;
        return loader.Accepts(version) ? InstanceLoaderState.Installed : InstanceLoaderState.WrongVersion;
    }
}
