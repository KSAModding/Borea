using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Borea.App.Localization;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private static readonly SupportedCulture English = new("en", "English");
    private static readonly SupportedCulture German = new("de", "Deutsch");
    private static readonly IReadOnlyList<SupportedCulture> Cultures = [English, German];

    private SupportedCulture _selectedCulture = English;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<SupportedCulture> SupportedCultures => Cultures;

    public SupportedCulture SelectedCulture
    {
        get => _selectedCulture;
        set
        {
            if (value is null)
                return;

            TrySetCulture(value.Name);
        }
    }

    public string SelectedCultureName => SelectedCulture.Name;

    public string NavigationHome => Resources.NavigationHome;

    public string NavigationLibrary => Resources.NavigationLibrary;

    public string NavigationDiscover => Resources.NavigationDiscover;

    public string NavigationSettings => Resources.NavigationSettings;

    public string NavigationTasks => Resources.NavigationTasks;

    public string PageHomeHeading => Resources.PageHomeHeading;

    public string PageDiscoverHeading => Resources.PageDiscoverHeading;

    public string PageLibraryHeading => Resources.PageLibraryHeading;

    public string SettingsLanguageLabel => Resources.SettingsLanguageLabel;

    public string SettingsRegionalFormatLabel => Resources.SettingsRegionalFormatLabel;

    public string SettingsThemeLabel => Resources.SettingsThemeLabel;

    public string SystemDefaultRegionalFormat => Resources.SystemDefaultRegionalFormat;

    public string HomeCurrentInstall => Resources.HomeCurrentInstall;

    public string HomeRecentlyUpdated => Resources.HomeRecentlyUpdated;

    public string HomeDiscoverMods => Resources.HomeDiscoverMods;

    public string HomeNoGameVersion => Resources.HomeNoGameVersion;

    public string HomeNoInstance => Resources.HomeNoInstance;

    public string HomeInstanceSourceCustom => Resources.HomeInstanceSourceCustom;

    public string ContentTypeMod => Resources.ContentTypeMod;

    public string LibraryNewInstancePlaceholder => Resources.LibraryNewInstancePlaceholder;

    public string LibraryCreate => Resources.LibraryCreate;

    public string LibraryNewInstance => Resources.LibraryNewInstance;

    public string LibraryActivate => Resources.LibraryActivate;

    public string LibraryRename => Resources.LibraryRename;

    public string LibraryDelete => Resources.LibraryDelete;

    public string LibrarySave => Resources.LibrarySave;

    public string LibraryCancel => Resources.LibraryCancel;

    public string LibraryDeleteConfirm => Resources.LibraryDeleteConfirm;

    public string LibraryEmpty => Resources.LibraryEmpty;

    public string InstanceTabContent => Resources.InstanceTabContent;

    public string InstanceTabManualInstalls => Resources.InstanceTabManualInstalls;

    public string InstanceTabGameData => Resources.InstanceTabGameData;

    public string InstanceTabLog => Resources.InstanceTabLog;

    public string InstanceGroupMods => Resources.InstanceGroupMods;

    public string InstanceGroupModLoaders => Resources.InstanceGroupModLoaders;

    public string InstanceGroupOther => Resources.InstanceGroupOther;

    public string InstanceGroupDependencies => Resources.InstanceGroupDependencies;

    public string InstanceEmptyContent => Resources.InstanceEmptyContent;

    public string InstancePlay => Resources.InstancePlay;

    public string ContentRemove => Resources.ContentRemove;

    public string ContentRemoveConfirm => Resources.ContentRemoveConfirm;

    public string DiscoverTabMods => Resources.DiscoverTabMods;

    public string DiscoverTabModpacks => Resources.DiscoverTabModpacks;

    public string DiscoverTabVehicles => Resources.DiscoverTabVehicles;

    public string DiscoverTabSaves => Resources.DiscoverTabSaves;

    public string DiscoverTabLoaders => Resources.DiscoverTabLoaders;

    public string DiscoverSearchPlaceholder => Resources.DiscoverSearchPlaceholder;

    public string DiscoverHideInstalled => Resources.DiscoverHideInstalled;

    public string DiscoverHideIncompatible => Resources.DiscoverHideIncompatible;

    public string DiscoverOperatingSystem => Resources.DiscoverOperatingSystem;

    public string DiscoverLicense => Resources.DiscoverLicense;

    public string DiscoverClearAll => Resources.DiscoverClearAll;

    public string DiscoverAll => Resources.DiscoverAll;

    public string DiscoverEmpty => Resources.DiscoverEmpty;

    public string DiscoverLoading => Resources.DiscoverLoading;

    public string DiscoverAdd => Resources.DiscoverAdd;

    public string DiscoverInstalled => Resources.DiscoverInstalled;

    public string DiscoverNoInstance => Resources.DiscoverNoInstance;

    public string DiscoverNoRelease => Resources.DiscoverNoRelease;

    public string InstallAnyway => Resources.InstallAnyway;

    public string InstallInstanceMissing => Resources.InstallInstanceMissing;

    public string ModalCreateInstanceTitle => Resources.ModalCreateInstanceTitle;

    public string ModalNameLabel => Resources.ModalNameLabel;

    public string ModalClose => Resources.ModalClose;

    public string SettingsGeneral => Resources.SettingsGeneral;

    public string SettingsAbout => Resources.SettingsAbout;

    public string AboutVersion => Resources.AboutVersion;

    public string AboutRuntime => Resources.AboutRuntime;

    public string AboutSystem => Resources.AboutSystem;

    public string AboutGame => Resources.AboutGame;

    public string AboutFolders => Resources.AboutFolders;

    public string AboutOpenBoreaFolder => Resources.AboutOpenBoreaFolder;

    public string AboutOpenInstancesFolder => Resources.AboutOpenInstancesFolder;

    public string AboutCopyDiagnostics => Resources.AboutCopyDiagnostics;

    public string AboutCopyPath => Resources.AboutCopyPath;

    public string AboutCopied => Resources.AboutCopied;

    public string AboutLinks => Resources.AboutLinks;

    public string AboutSourceCode => Resources.AboutSourceCode;

    public string AboutReportBug => Resources.AboutReportBug;

    public string AboutReleases => Resources.AboutReleases;

    public string AboutCommunity => Resources.AboutCommunity;

    public string AboutDiscord => Resources.AboutDiscord;

    public string AboutCredits => Resources.AboutCredits;

    public string AboutCreditsHint => Resources.AboutCreditsHint;

    public string ContentLoaderHint => Resources.ContentLoaderHint;

    public string SourceContentIndex => Resources.SourceContentIndex;

    public string LinkForum => Resources.LinkForum;

    public string LinkRepository => Resources.LinkRepository;

    public string LinkBugTracker => Resources.LinkBugTracker;

    public string LinkDiscussions => Resources.LinkDiscussions;

    public string ContentTabDescription => Resources.ContentTabDescription;

    public string ContentTabVersions => Resources.ContentTabVersions;

    public string ContentCompatibility => Resources.ContentCompatibility;

    public string CompatibilityCompatible => Resources.CompatibilityCompatible;

    public string CompatibilityUntested => Resources.CompatibilityUntested;

    public string CompatibilityIncompatible => Resources.CompatibilityIncompatible;

    public string CompatibilityUnknown => Resources.CompatibilityUnknown;

    public string ContentLinks => Resources.ContentLinks;

    public string ContentTags => Resources.ContentTags;

    public string ContentAuthor => Resources.ContentAuthor;

    public string ContentDetails => Resources.ContentDetails;

    public string ContentAdd => Resources.ContentAdd;

    public string ContentVersionHeader => Resources.ContentVersionHeader;

    public string ContentChannelHeader => Resources.ContentChannelHeader;

    public string ContentGameVersionHeader => Resources.ContentGameVersionHeader;

    public string ContentPublishedHeader => Resources.ContentPublishedHeader;

    public string ContentNoDescription => Resources.ContentNoDescription;

    public string ContentLoadingVersions => Resources.ContentLoadingVersions;

    public string ContentNoVersions => Resources.ContentNoVersions;

    public string ContentTypeModLoader => Resources.ContentTypeModLoader;

    public string ReleaseStable => Resources.ReleaseStable;

    public string ReleaseTesting => Resources.ReleaseTesting;

    public string ReleaseDev => Resources.ReleaseDev;

    public string ReleaseUnknown => Resources.ReleaseUnknown;

    public string SettingsGame => Resources.SettingsGame;

    public string SetupGameDirectory => Resources.SetupGameDirectory;

    public string SetupGameDirectoryHint => Resources.SetupGameDirectoryHint;

    public string SetupBannerNotSaved => Resources.SetupBannerNotSaved;

    public string SetupBannerFolderMissing => Resources.SetupBannerFolderMissing;

    public string SetupBannerAction => Resources.SetupBannerAction;

    public string SetupBrowse => Resources.SetupBrowse;

    public string SetupSave => Resources.SetupSave;

    public string SetupLoader => Resources.SetupLoader;

    public string SetupLoaderDirectory => Resources.SetupLoaderDirectory;

    public string SetupLoaderDirectoryHint => Resources.SetupLoaderDirectoryHint;

    public string SetupInstallLoader => Resources.SetupInstallLoader;

    public string SetupUseExisting => Resources.SetupUseExisting;

    public string SetupSaved => Resources.SetupSaved;

    public string SetupDirectoryMissing => Resources.SetupDirectoryMissing;

    public string SetupNoLoaderSelected => Resources.SetupNoLoaderSelected;

    public string SetupReinstallLoader => Resources.SetupReinstallLoader;

    public string SetupLoaderInstalledUnknownVersion => Resources.SetupLoaderInstalledUnknownVersion;

    public LocalizationService()
        : this(CultureInfo.CurrentUICulture)
    {
    }

    public LocalizationService(CultureInfo requestedCulture)
    {
        ArgumentNullException.ThrowIfNull(requestedCulture);
        SetCulture(ResolveSupportedCulture(requestedCulture));
    }

    public bool TrySetCulture(string? cultureName)
    {
        SupportedCulture? supportedCulture = null;

        if (!string.IsNullOrWhiteSpace(cultureName))
        {
            try
            {
                supportedCulture = ResolveSupportedCulture(CultureInfo.GetCultureInfo(cultureName));
            }
            catch (CultureNotFoundException)
            {
            }
        }

        SetCulture(supportedCulture);
        return supportedCulture is not null;
    }

    public string FormatViewNotFound(string viewName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewName);
        return string.Format(CultureInfo.CurrentCulture, Resources.ViewNotFoundFormat, viewName);
    }

    public string FormatSetupLoaderInstalled(string loaderName, string version, string directory)
        => string.Format(CultureInfo.CurrentCulture, Resources.SetupLoaderInstalledFormat, loaderName, version, directory);

    public string FormatSetupLoaderInstalledVersion(string version)
        => string.Format(CultureInfo.CurrentCulture, Resources.SetupLoaderInstalledVersionFormat, version);

    public string FormatSetupUpdateLoader(string version)
        => string.Format(CultureInfo.CurrentCulture, Resources.SetupUpdateLoaderFormat, version);

    public string FormatAboutFolderMissing(string folder)
        => string.Format(CultureInfo.CurrentCulture, Resources.AboutFolderMissingFormat, folder);

    public string FormatAboutCannotOpen(string target)
        => string.Format(CultureInfo.CurrentCulture, Resources.AboutCannotOpenFormat, target);

    public string FormatDiscoverInstalledIn(string instance)
        => string.Format(CultureInfo.CurrentCulture, Resources.DiscoverInstalledInFormat, instance);

    public string FormatContentSource(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        return string.Format(CultureInfo.CurrentCulture, Resources.ContentSourceFormat, source);
    }

    public string FormatContentRemoveNotOwned(string modId)
        => string.Format(CultureInfo.CurrentCulture, Resources.ContentRemoveNotOwnedFormat, modId);

    public string FormatContentRemoveRequired(string modId, string dependents)
        => string.Format(CultureInfo.CurrentCulture, Resources.ContentRemoveRequiredFormat, modId, dependents);

    public string FormatContentByAuthor(string authors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authors);
        return string.Format(CultureInfo.CurrentCulture, Resources.ContentByAuthorFormat, authors);
    }

    public string FormatPreferenceSaveError(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return string.Format(CultureInfo.CurrentCulture, Resources.PreferenceSaveErrorFormat, error);
    }

    private static SupportedCulture? ResolveSupportedCulture(CultureInfo culture)
    {
        for (var candidate = culture; candidate != CultureInfo.InvariantCulture; candidate = candidate.Parent)
        {
            var match = Cultures.FirstOrDefault(item =>
                string.Equals(item.Name, candidate.Name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        return null;
    }

    private void SetCulture(SupportedCulture? culture)
    {
        culture ??= English;
        Resources.Culture = culture.Culture;
        CultureInfo.CurrentUICulture = culture.Culture;

        if (ReferenceEquals(_selectedCulture, culture))
            return;

        _selectedCulture = culture;
        OnPropertyChanged(string.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
