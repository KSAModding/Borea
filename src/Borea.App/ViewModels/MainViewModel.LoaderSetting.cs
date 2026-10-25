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
/// The modal that sets the mod loader of an instance: Automatic, which starts the newest installed version that fits,
/// or one exact version, which Borea installs first when it is missing.
/// </summary>
public partial class MainViewModel
{
    /// <summary>What the choices of the open modal are rated with, read once when it opens.</summary>
    private LoaderSettingContext? _loaderSetting;

    /// <summary>What Automatic starts with the chosen loader, or null before it is read.</summary>
    private LoaderVersionResolution? _loaderSettingAutomatic;

    /// <summary>The release that Automatic installs before it saves, or null when Automatic starts an installed version.</summary>
    private ModVersionMetadata? _loaderSettingAutomaticMissing;

    private Task _loaderSettingChoices = Task.CompletedTask;

    /// <summary>The instance whose loader the modal sets. Null while the modal is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoaderSettingModalOpen), nameof(LoaderSettingHint))]
    private InstanceItem? _editingLoaderSetting;

    /// <summary>The loader listings that can start an instance.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoaderSettingLoaders))]
    private IReadOnlyList<ModMetadata> _loaderSettingLoaders = [];

    [ObservableProperty]
    private ModMetadata? _loaderSettingLoader;

    /// <summary>True when the modal keeps one exact version, false for Automatic.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoaderSettingAutomatic), nameof(IsLoaderSettingPinned), nameof(LoaderSettingNeedsInstall), nameof(LoaderSettingActionText))]
    private bool _pinsLoaderVersion;

    /// <summary>"Starts StarMap 0.4.8", or what Automatic does when that version is not installed.</summary>
    public string? LoaderSettingAutomaticText => (_loaderSettingAutomatic, LoaderSettingLoader) switch
    {
        (null, _) or (_, null) => null,
        ({ Start: { } start }, { } loader) => Localization.FormatInstanceLoaderAutomaticTarget(loader.Name, start.Version?.ToString() ?? start.RawVersion ?? string.Empty),
        ({ Missing: { } missing }, { } loader) => Localization.FormatInstanceLoaderAutomaticNotInstalled(loader.Name, missing.Version.ToString()),
        (_, { } loader) => Localization.FormatInstanceLoaderAutomaticNone(loader.Name),
    };

    /// <summary>The versions a pin can name: the installed ones and the releases the channel offers, newest first.</summary>
    [ObservableProperty]
    private IReadOnlyList<LoaderReleaseChoice> _loaderSettingReleases = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoaderSettingNeedsInstall), nameof(LoaderSettingActionText))]
    private LoaderReleaseChoice? _selectedLoaderSettingRelease;

    public bool IsLoaderSettingModalOpen => EditingLoaderSetting is not null;

    public string? LoaderSettingHint => EditingLoaderSetting is { } item ? Localization.FormatInstanceLoaderModalHint(item.Name) : null;

    /// <summary>The loader is a choice only once the index lists more than one loader that can start an instance.</summary>
    public bool ShowLoaderSettingLoaders => LoaderSettingLoaders.Count > 1;

    /// <summary>
    /// The first radio button. A radio button that unchecks writes false, which changes nothing here,
    /// because only the button that the player checks chooses the mode.
    /// </summary>
    public bool IsLoaderSettingAutomatic
    {
        get => !PinsLoaderVersion;
        set
        {
            if (value)
                PinsLoaderVersion = false;
        }
    }

    /// <summary>The second radio button, the opposite of <see cref="IsLoaderSettingAutomatic"/>.</summary>
    public bool IsLoaderSettingPinned
    {
        get => PinsLoaderVersion;
        set
        {
            if (value)
                PinsLoaderVersion = true;
        }
    }

    /// <summary>True when the chosen version is not installed and Borea can install it, so a save installs it first.</summary>
    public bool LoaderSettingNeedsInstall => IsLoaderSettingAutomatic
        ? _loaderSettingAutomaticMissing is not null
        : SelectedLoaderSettingRelease is { CanInstall: true };

    public string LoaderSettingActionText => LoaderSettingNeedsInstall ? Localization.InstanceLoaderSaveAndInstall : Localization.LibrarySave;

    /// <summary>Completes when the choices for the chosen loader are built.</summary>
    internal Task WhenLoaderSettingChoicesBuiltAsync() => _loaderSettingChoices;

    /// <summary>
    /// Opens the modal with the setting of <paramref name="item"/>. The choices are rated with the listings and the
    /// releases that Play uses, so the modal and the launch agree on what starts.
    /// </summary>
    internal async Task BeginEditLoaderSettingAsync(InstanceItem item)
    {
        if (_services is not { } services || IsLoaderSettingModalOpen)
            return;

        InstanceError = null;
        SetupError = null;
        SetupMessage = null;
        try
        {
            var instance = await services.Instances.GetByIdAsync(item.InstanceId)
                ?? throw new InvalidOperationException(Localization.LaunchInstanceMissing);
            var resolution = await ResolveLaunchLoaderAsync(services, instance);
            var loaders = resolution.Listings
                .Where(listing => listing.Type == ContentType.ModLoader && listing.Provides?.Instance is not null)
                .OrderBy(listing => listing.ModId, ModIds.Comparer)
                .ToList();
            _loaderSetting = new LoaderSettingContext(
                instance,
                await services.LoaderVersions.ListAsync(),
                resolution.Listings,
                await HeldLoaderReleasesAsync(services),
                services.Settings.ReleaseChannel,
                services.InstanceGames.Resolve(instance).Version?.Version,
                resolution.Version?.LoaderId);
            LoaderSettingLoaders = loaders;
            PinsLoaderVersion = instance.Loader.IsPinned;

            // the loader the setting names, else the one that starts the instance now
            var current = instance.Loader.LoaderId ?? resolution.Version?.LoaderId;
            // the choices are built again also when the same listing is chosen as at the last open
            LoaderSettingLoader = null;
            LoaderSettingLoader = loaders.FirstOrDefault(listing => ModIds.Equals(listing.ModId, current)) ?? loaders.FirstOrDefault();
            await _loaderSettingChoices;
            EditingLoaderSetting = item;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or HttpRequestException or TaskCanceledException)
        {
            _loaderSetting = null;
            ShowErrorToast(() => Localization.FormatToastInstanceLoaderFailed(item.Name), exception.Message);
        }
    }

    partial void OnLoaderSettingLoaderChanged(ModMetadata? value)
    {
        // the answer for the last loader does not describe this one
        _loaderSettingAutomatic = null;
        _loaderSettingAutomaticMissing = null;
        _loaderSettingChoices = BuildLoaderSettingChoicesAsync(value);
    }

    /// <summary>What Automatic starts with the chosen loader, and the versions a pin can name.</summary>
    private async Task BuildLoaderSettingChoicesAsync(ModMetadata? loader)
    {
        if (_loaderSetting is not { } context || loader is null)
        {
            _loaderSettingAutomatic = null;
            _loaderSettingAutomaticMissing = null;
            OnPropertyChanged(nameof(LoaderSettingAutomaticText));
            LoaderSettingReleases = [];
            SelectedLoaderSettingRelease = null;
            return;
        }

        var instance = context.Instance;
        var need = LoaderNeed.For(instance).Loaders.FirstOrDefault(needed => ModIds.Equals(needed.LoaderId, loader.ModId));
        var automatic = await LoaderVersionChoice.ResolveAsync(
            AutomaticSetting(context, loader.ModId), loader.ModId, need, context.Installed, context.Listings, context.Releases, context.Channel, context.Game);

        var history = new List<ModVersionMetadata>();
        if (context.Releases is { } releases)
        {
            try
            {
                history.AddRange(await releases.GetReleaseHistoryAsync(loader.ModId));
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException)
            {
                // the installed versions can still be pinned
            }
        }

        if (!ReferenceEquals(loader, LoaderSettingLoader))
            return;

        _loaderSettingAutomatic = automatic;
        _loaderSettingAutomaticMissing = automatic.Start is null ? automatic.Missing : null;
        OnPropertyChanged(nameof(LoaderSettingAutomaticText));

        // an installed copy can always be pinned, but Borea never installs a yanked release or one whose download is gone
        ModVersion? pin = instance.Loader.Version is { } pinned && ModIds.Equals(instance.Loader.LoaderId, loader.ModId) ? pinned : null;
        var installed = InstalledLoaderVersion.Of(context.Installed, loader.ModId).Select(version => version.Version).OfType<ModVersion>().ToHashSet();
        var choices = installed
            .Select(version => Choice(version, history.FirstOrDefault(release => release.Version == version), isInstalled: true))
            .Concat(history
                .Where(release => !installed.Contains(release.Version) && (release.Version == pin || release.IsOffered && context.Channel.Includes(release.ReleaseStatus)))
                .Select(release => Choice(release.Version, release, isInstalled: false)))
            .ToList();

        // the current pin stays a choice also when it is outside the channel or the index no longer lists it,
        // so that a save without a change keeps it and does not move it to another version
        if (pin is { } kept && !choices.Any(choice => choice.Version == kept))
            choices.Add(Choice(kept, null, isInstalled: false));
        choices = [.. choices.OrderByDescending(choice => choice.Version)];
        LoaderSettingReleases = choices;

        var chosen = pin ?? automatic.Start?.Version ?? automatic.Missing?.Version;
        SelectedLoaderSettingRelease = choices.FirstOrDefault(choice => choice.Version == chosen) ?? choices.FirstOrDefault();
        OnPropertyChanged(nameof(LoaderSettingNeedsInstall));
        OnPropertyChanged(nameof(LoaderSettingActionText));

        LoaderReleaseChoice Choice(ModVersion version, ModVersionMetadata? release, bool isInstalled) => new(
            this,
            version,
            release,
            isInstalled,
            release is null ? GameCompatibility.Unknown : Compatibility.Evaluate(release, context.Game),
            need?.Requirements
                .Where(requirement => version.CompareTo(requirement.MinVersion) < 0 || requirement.MaxVersion is { } max && version.CompareTo(max) > 0)
                .ToList() ?? [],
            requirement => Localization.FormatInstanceLoaderModNeeds(ModName(instance, requirement.ModId), LoaderBoundsText(requirement.MinVersion, requirement.MaxVersion)));
    }

    /// <summary>
    /// Automatic names a loader only when the player chose another loader than the one Borea chooses, or the setting
    /// already names one, so an instance keeps following Borea's choice until the player makes one.
    /// </summary>
    private LoaderSetting AutomaticSetting(LoaderSettingContext context, string loaderId) =>
        ShowLoaderSettingLoaders && (context.Instance.Loader.LoaderId is not null || !ModIds.Equals(loaderId, context.AutomaticLoaderId))
            ? LoaderSetting.ForLoader(loaderId)
            : LoaderSetting.Automatic;

    private static string ModName(Instance instance, string modId) =>
        instance.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, modId))?.Metadata.Listing?.Name ?? modId;

    /// <summary>The texts of the open modal in the new language.</summary>
    private void RefreshLoaderSettingText()
    {
        OnPropertyChanged(nameof(LoaderSettingHint));
        OnPropertyChanged(nameof(LoaderSettingAutomaticText));
        OnPropertyChanged(nameof(LoaderSettingActionText));
        foreach (var choice in LoaderSettingReleases)
            choice.RefreshText();
    }

    [RelayCommand]
    private void CancelLoaderSettingModal()
    {
        if (IsSetupBusy)
            return;

        EditingLoaderSetting = null;
        _loaderSetting = null;
        InstanceError = null;
        SetupError = null;
    }

    /// <summary>
    /// Installs the chosen version when it is missing, then saves the setting. A failed install keeps the modal open
    /// with the error and saves nothing, so the instance never names a version that is not there.
    /// </summary>
    [RelayCommand]
    private async Task SaveLoaderSettingAsync()
    {
        if (EditingLoaderSetting is not { } item || _loaderSetting is not { } context || LoaderSettingLoader is not { } loader || IsSetupBusy)
            return;

        LoaderSetting setting;
        ModVersionMetadata? install;
        if (IsLoaderSettingAutomatic)
        {
            setting = AutomaticSetting(context, loader.ModId);
            install = _loaderSettingAutomaticMissing;
        }
        else
        {
            if (SelectedLoaderSettingRelease is not { } choice)
                return;

            setting = LoaderSetting.Pinned(loader.ModId, choice.Version);
            // a pin that Borea cannot install stays as it is, and the launch says that it is missing
            install = choice.CanInstall ? choice.Release : null;
        }

        if (install is not null && !await InstallLoaderVersionAsync(loader.ModId, install))
            return;

        if (EditingLoaderSetting != item)
            return;

        await RunModalInstanceOperationAsync(instances => instances.UpdateAsync(item.InstanceId, instance =>
        {
            instance.SetLoader(setting);
            return true;
        }), () => EditingLoaderSetting == item, () => Localization.FormatToastInstanceLoaderFailed(item.Name));

        if (InstanceError is null && EditingLoaderSetting == item)
        {
            EditingLoaderSetting = null;
            _loaderSetting = null;
        }
    }

    /// <summary>The inputs of the open modal.</summary>
    /// <param name="AutomaticLoaderId">The loader that Borea chooses for the instance, or null when it cannot choose one.</param>
    private sealed record LoaderSettingContext(
        Instance Instance,
        IReadOnlyList<InstalledLoaderVersion> Installed,
        IReadOnlyList<ModMetadata> Listings,
        IModRepository? Releases,
        ReleaseChannel Channel,
        GameVersion? Game,
        string? AutomaticLoaderId);
}

/// <summary>One version a pin can name, with what Borea knows about it.</summary>
public sealed class LoaderReleaseChoice : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly IReadOnlyList<ModLoaderBounds> _rejectedBy;
    private readonly Func<ModLoaderBounds, string> _markOf;

    /// <param name="release">The release in the index, or null for an installed version the index does not list.</param>
    /// <param name="rejectedBy">What each mod of the instance that rejects this version asks for.</param>
    /// <param name="markOf">The line that names one of those mods and what it asks for.</param>
    public LoaderReleaseChoice(MainViewModel owner, ModVersion version, ModVersionMetadata? release, bool isInstalled, GameCompatibility compatibility, IReadOnlyList<ModLoaderBounds> rejectedBy, Func<ModLoaderBounds, string> markOf)
    {
        _owner = owner;
        Version = version;
        Release = release;
        IsInstalled = isInstalled;
        Compatibility = compatibility;
        _rejectedBy = rejectedBy;
        _markOf = markOf;
    }

    public ModVersion Version { get; }

    public ModVersionMetadata? Release { get; }

    public bool IsInstalled { get; }

    /// <summary>
    /// True when a save installs this version first. A pin that is not installed and is yanked, gone or not in the
    /// index cannot install, so a save keeps it without an install.
    /// </summary>
    public bool CanInstall => !IsInstalled && Release is { IsOffered: true };

    public GameCompatibility Compatibility { get; }

    public string VersionText => Version.ToString();

    public string InstalledText => _owner.Localization.DiscoverInstalled;

    /// <summary>The release status, shown only when it is not stable. Null for a version the index does not list.</summary>
    public string? StatusText => Release is { ReleaseStatus: not ReleaseStatus.Stable } release ? _owner.ReleaseStatusText(release.ReleaseStatus) : null;

    public bool HasStatus => StatusText is not null;

    public string CompatibilityText => _owner.CompatibilityText(Compatibility);

    public bool IsCompatible => Compatibility == GameCompatibility.Compatible;

    public bool IsIncompatible => Compatibility == GameCompatibility.Incompatible;

    /// <summary>"KSArmory needs 0.4.6 or newer" when a mod of the instance does not accept this version, else null.</summary>
    public string? Mark => _rejectedBy.Count == 0 ? null : string.Join("; ", _rejectedBy.Select(_markOf));

    public bool HasMark => _rejectedBy.Count > 0;

    /// <summary>Every text in the new language.</summary>
    internal void RefreshText() => OnPropertyChanged(string.Empty);

    /// <summary>The version, which the closed box and screen readers show.</summary>
    public override string ToString() => VersionText;
}
