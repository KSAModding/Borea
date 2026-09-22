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
            id => names.TryGetValue(id, out var name) ? name : id)).ToList();

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
public sealed partial class InstanceLoaderRow
{
    private readonly MainViewModel _owner;
    private readonly NeededLoader _loader;
    private readonly DiscoverItem? _listing;
    private readonly LoaderInstallation? _installation;
    private readonly Func<string, string> _nameOf;

    /// <param name="nameOf">The display name of a mod id, as its row on the page shows it.</param>
    public InstanceLoaderRow(MainViewModel owner, NeededLoader loader, DiscoverItem? listing, LoaderInstallation? installation, Func<string, string> nameOf)
    {
        _owner = owner;
        _loader = loader;
        _listing = listing;
        _installation = installation;
        _nameOf = nameOf;
        Name = listing?.Name ?? loader.LoaderId;
        Icon = listing?.Icon;
        State = StateOf(loader, installation);
    }

    public string Name { get; }

    public ListingImage? Icon { get; }

    /// <summary>Whether the row links to the loader's page, like a mod row does. A loader the index does not list has none.</summary>
    public bool CanOpen => _listing is not null;

    /// <summary>Why the row has no link, for the tooltip of its icon and name, the way a mod row says it. Null when it links.</summary>
    public string? NoPageText => CanOpen ? null : _owner.Localization.InstanceLoaderNotInIndex;

    /// <summary>Opens the loader's page with the way back to this instance, the way a mod row opens its page.</summary>
    [RelayCommand]
    private Task OpenAsync() => _listing is null ? Task.CompletedTask : _owner.OpenContentFromInstanceAsync(_listing);

    public InstanceLoaderState State { get; }

    /// <summary>
    /// "Needs 0.4.5 or newer", or "Needs 0.4.5 to 0.8.0". On a conflict the
    /// combined bounds hold no version, so the line names what each mod asks for instead.
    /// </summary>
    public string RangeText => IsConflict
        ? string.Join("; ", _loader.Requirements.Select(requirement =>
            _owner.Localization.FormatInstanceLoaderModNeeds(_nameOf(requirement.ModId), Bounds(requirement.MinVersion, requirement.MaxVersion))))
        : _loader.MaxVersion is { } max
            ? _owner.Localization.FormatInstanceLoaderRange(_loader.MinVersion.ToString(), max.ToString())
            : _owner.Localization.FormatInstanceLoaderMinimum(_loader.MinVersion.ToString());

    /// <summary>The mods that need the loader. Null on a conflict, where the range line already names them.</summary>
    public string? NeededByText => IsConflict
        ? null
        : _owner.Localization.FormatInstanceLoaderNeededBy(string.Join(", ", _loader.NeededBy.Select(_nameOf)));

    /// <summary>A short label, so the chip keeps the size of the other chips on the page.</summary>
    public string StatusText => State switch
    {
        InstanceLoaderState.Installed => _owner.Localization.FormatInstanceLoaderInstalled(_installation!.Version!.Value.ToString()),
        InstanceLoaderState.VersionUnknown => _owner.Localization.InstanceLoaderVersionUnknown,
        InstanceLoaderState.WrongVersion => _owner.Localization.FormatInstanceLoaderWrongVersion(_installation!.Version!.Value.ToString()),
        InstanceLoaderState.NotInstalled => _owner.Localization.InstanceLoaderNotInstalled,
        _ => _owner.Localization.InstanceLoaderConflictShort,
    };

    /// <summary>The sentence behind a short chip. Null where the chip says it all.</summary>
    public string? StatusToolTip => IsConflict ? _owner.Localization.InstanceLoaderConflict : null;

    private string Bounds(ModVersion min, ModVersion? max) => max is { } upper
        ? _owner.Localization.FormatInstanceLoaderBoundsRange(min.ToString(), upper.ToString())
        : _owner.Localization.FormatInstanceLoaderBoundsMinimum(min.ToString());

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
