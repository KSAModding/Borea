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
    private static readonly SupportedCulture Pirate = new("en-QP", "Pirate speak");
    private static readonly IReadOnlyList<SupportedCulture> Cultures = [English, German, Pirate];

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

    public string NavigationBack => Resources.NavigationBack;

    public string TasksEmpty => Resources.TasksEmpty;


    public string TaskIndexRefresh => Resources.TaskIndexRefresh;

    public string FormatTaskInstall(string content)
        => string.Format(CultureInfo.CurrentCulture, Resources.TaskInstallFormat, content);

    public string FormatTaskUpdate(string content)
        => string.Format(CultureInfo.CurrentCulture, Resources.TaskUpdateFormat, content);

    public string TaskUpdateAll => Resources.TaskUpdateAll;

    public string FormatTaskRemove(string content)
        => string.Format(CultureInfo.CurrentCulture, Resources.TaskRemoveFormat, content);

    public string FormatTaskCreateInstance(string instanceName)
        => string.Format(CultureInfo.CurrentCulture, Resources.TaskCreateInstanceFormat, instanceName);

    public string FormatTaskReplace(string folder)
        => string.Format(CultureInfo.CurrentCulture, Resources.TaskReplaceFormat, folder);

    public string FormatTaskManage(string content)
        => string.Format(CultureInfo.CurrentCulture, Resources.TaskManageFormat, content);

    public string FormatTaskLibraryFolder(string folder)
        => string.Format(CultureInfo.CurrentCulture, Resources.TaskLibraryFolderFormat, folder);

    public string FormatTaskBackupRestore(string name)
        => string.Format(CultureInfo.CurrentCulture, Resources.TaskBackupRestoreFormat, name);

    public string FormatTaskBackupDelete(string name)
        => string.Format(CultureInfo.CurrentCulture, Resources.TaskBackupDeleteFormat, name);

    public string TaskWaiting => Resources.TaskWaiting;

    public string TaskRunning => Resources.TaskRunning;

    public string TaskPaused => Resources.TaskPaused;

    public string TaskFinished => Resources.TaskFinished;

    public string TaskStopped => Resources.TaskStopped;

    public string TaskFailed => Resources.TaskFailed;

    public string TaskRetryModMissing => Resources.TaskRetryModMissing;

    public string ToastOpenInstance => Resources.ToastOpenInstance;

    public string FormatToastAdded(string content, string instanceName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastAddedFormat, content, instanceName);

    public string FormatToastUpdated(string content, string version, string instanceName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastUpdatedFormat, content, version, instanceName);

    public string FormatToastUpdatedAll(int count, string instanceName)
        => count == 1
            ? string.Format(CultureInfo.CurrentCulture, Resources.ToastUpdatedAllOneFormat, instanceName)
            : string.Format(CultureInfo.CurrentCulture, Resources.ToastUpdatedAllFormat, count, instanceName);

    public string FormatToastRemoved(string content, string instanceName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastRemovedFormat, content, instanceName);

    public string FormatToastLoaderInstalled(string loader, string version)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastLoaderInstalledFormat, loader, version);

    public string FormatToastInstanceCreated(string instanceName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastInstanceCreatedFormat, instanceName);

    public string FormatToastReplaced(string folder, string instanceName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastReplacedFormat, folder, instanceName);

    public string FormatToastManaged(string content, string instanceName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastManagedFormat, content, instanceName);

    public string FormatToastLibraryFolderChanged(string folder)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastLibraryFolderChangedFormat, folder);

    public string FormatToastBackupRestored(string name, string instanceName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastBackupRestoredFormat, name, instanceName);

    public string FormatToastBackupDeleted(string name)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastBackupDeletedFormat, name);

    public string FormatToastInstallStopped(string content)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastInstallStoppedFormat, content);

    public string ToastLibraryFolderStopped => Resources.ToastLibraryFolderStopped;

    public string FormatToastUpdateStopped(string content)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastUpdateStoppedFormat, content);

    public string FormatToastUpdateAllStopped(string instanceName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastUpdateAllStoppedFormat, instanceName);

    public string FormatToastStoppedInstalled(int completed, int total)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastStoppedInstalledFormat, completed, total);

    public string FormatToastStoppedUpdated(int completed, int total)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastStoppedUpdatedFormat, completed, total);

    public string ToastIndexRefreshed => Resources.ToastIndexRefreshed;

    public string ToastIndexUpToDate => Resources.ToastIndexUpToDate;

    public string ToastIndexRefreshFailed => Resources.ToastIndexRefreshFailed;

    public string FormatToastInstallFailed(string content)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastInstallFailedFormat, content);

    public string FormatToastUpdateFailed(string content)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastUpdateFailedFormat, content);

    public string FormatToastUpdateAllFailed(string instanceName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastUpdateAllFailedFormat, instanceName);

    public string FormatToastRemoveFailed(string content)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastRemoveFailedFormat, content);

    public string FormatToastCreateInstanceFailed(string instanceName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastCreateInstanceFailedFormat, instanceName);

    public string FormatToastReplaceFailed(string folder)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastReplaceFailedFormat, folder);

    public string FormatToastManageFailed(string content)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastManageFailedFormat, content);

    public string FormatToastLibraryFolderFailed(string folder)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastLibraryFolderFailedFormat, folder);

    public string FormatToastBackupRestoreFailed(string name)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastBackupRestoreFailedFormat, name);

    public string FormatToastBackupDeleteFailed(string name)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastBackupDeleteFailedFormat, name);

    public string FormatToastActivateFailed(string instanceName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastActivateFailedFormat, instanceName);

    public string FormatToastDeactivateFailed(string instanceName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastDeactivateFailedFormat, instanceName);

    public string FormatToastDeleteFailed(string name)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastDeleteFailedFormat, name);

    public string FormatToastDuplicateFailed(string instanceName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastDuplicateFailedFormat, instanceName);

    public string FormatToastCreateFailed(string instanceName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastCreateFailedFormat, instanceName);

    public string FormatToastRenameFailed(string instanceName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastRenameFailedFormat, instanceName);

    public string FormatToastLaunchArgumentsFailed(string instanceName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastLaunchArgumentsFailedFormat, instanceName);

    public string FormatToastEnableFailed(string content)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastEnableFailedFormat, content);

    public string FormatToastDisableFailed(string content)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastDisableFailedFormat, content);

    public string FormatToastCheckFailed(string folder)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastCheckFailedFormat, folder);

    public string FormatToastOpenFailed(string target)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastOpenFailedFormat, target);

    public string ToastImportModListFailed => Resources.ToastImportModListFailed;

    public string FormatToastExportModListFailed(string instanceName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastExportModListFailedFormat, instanceName);

    public string FormatToastCopyModListFailed(string instanceName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastCopyModListFailedFormat, instanceName);

    public string FormatToastBackUpFailed(string name)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastBackUpFailedFormat, name);

    public string ToastBackUpAllFailed => Resources.ToastBackUpAllFailed;

    public string FormatToastCopyFailed(string name)
        => string.Format(CultureInfo.CurrentCulture, Resources.ToastCopyFailedFormat, name);

    public string ToastCopyFromProfileFailed => Resources.ToastCopyFromProfileFailed;

    public string PageHomeHeading => Resources.PageHomeHeading;

    public string PageDiscoverHeading => Resources.PageDiscoverHeading;

    public string PageLibraryHeading => Resources.PageLibraryHeading;

    public string SettingsLanguageLabel => Resources.SettingsLanguageLabel;

    public string SettingsRegionalFormatLabel => Resources.SettingsRegionalFormatLabel;

    public string SettingsThemeLabel => Resources.SettingsThemeLabel;

    public string SettingsUpdatesLabel => Resources.SettingsUpdatesLabel;

    public string SettingsCheckForUpdatesAtStart => Resources.SettingsCheckForUpdatesAtStart;

    public string SettingsUpdateChannelLabel => Resources.SettingsUpdateChannelLabel;

    public string SettingsUpdateChannelHint => Resources.SettingsUpdateChannelHint;

    public string UpdateChannelStable => Resources.UpdateChannelStable;

    public string UpdateChannelTesting => Resources.UpdateChannelTesting;

    public string UpdateChannelDev => Resources.UpdateChannelDev;

    public string SettingsNewsLabel => Resources.SettingsNewsLabel;

    public string SettingsFetchAnnouncements => Resources.SettingsFetchAnnouncements;

    public string SettingsLinksLabel => Resources.SettingsLinksLabel;

    public string SettingsOpenBoreaLinks => Resources.SettingsOpenBoreaLinks;

    public string SettingsOpenBoreaLinksHint => Resources.SettingsOpenBoreaLinksHint;

    public string SettingsReleaseChannelLabel => Resources.SettingsReleaseChannelLabel;

    public string SettingsReleaseChannelHint => Resources.SettingsReleaseChannelHint;

    public string SettingsImagesLabel => Resources.SettingsImagesLabel;

    public string SettingsLoadImagesFromAuthorHosts => Resources.SettingsLoadImagesFromAuthorHosts;

    public string SettingsLoadImagesFromAuthorHostsHint => Resources.SettingsLoadImagesFromAuthorHostsHint;

    public string SettingsLibraryFolderLabel => Resources.SettingsLibraryFolderLabel;

    public string SettingsLibraryFolderHint => Resources.SettingsLibraryFolderHint;

    public string SettingsLibraryFolderUseDefault => Resources.SettingsLibraryFolderUseDefault;

    public string SettingsLibraryFolderMove => Resources.SettingsLibraryFolderMove;

    public string SettingsLibraryFolderMoveToolTip => Resources.SettingsLibraryFolderMoveToolTip;

    public string SettingsLibraryFolderOpenToolTip => Resources.SettingsLibraryFolderOpenToolTip;

    public string SettingsLibraryFolderPickerTitle => Resources.SettingsLibraryFolderPickerTitle;

    public string SettingsBackupRetentionLabel => Resources.SettingsBackupRetentionLabel;

    public string SettingsBackupRetentionHint => Resources.SettingsBackupRetentionHint;

    public string BackupRetentionOff => Resources.BackupRetentionOff;

    public string FormatBackupRetentionDays(int days)
        => string.Format(CultureInfo.CurrentCulture, Resources.BackupRetentionDaysFormat, days);

    public string SettingsGitHubLabel => Resources.SettingsGitHubLabel;

    public string SettingsGitHubHint => Resources.SettingsGitHubHint;

    public string SettingsGitHubSignIn => Resources.SettingsGitHubSignIn;

    public string FormatSettingsGitHubSignedInAs(string login)
        => string.Format(CultureInfo.CurrentCulture, Resources.SettingsGitHubSignedInAsFormat, login);

    public string SettingsGitHubSignOutHint => Resources.SettingsGitHubSignOutHint;

    public string SettingsGitHubSignOut => Resources.SettingsGitHubSignOut;

    public string SettingsGitHubManageAccess => Resources.SettingsGitHubManageAccess;

    public string GitHubSignInTitle => Resources.GitHubSignInTitle;

    public string GitHubSignInHint => Resources.GitHubSignInHint;

    public string GitHubSignInCodeWarning => Resources.GitHubSignInCodeWarning;

    public string GitHubSignInGettingCode => Resources.GitHubSignInGettingCode;

    public string GitHubSignInCopyAndOpen => Resources.GitHubSignInCopyAndOpen;

    public string GitHubSignInWaiting => Resources.GitHubSignInWaiting;

    public string GitHubSignInExpired => Resources.GitHubSignInExpired;

    public string GitHubSignInDenied => Resources.GitHubSignInDenied;

    public string GitHubSignInClientRejected => Resources.GitHubSignInClientRejected;

    public string GitHubSignInCodeRejected => Resources.GitHubSignInCodeRejected;

    public string GitHubSignInRequestRejected => Resources.GitHubSignInRequestRejected;

    public string GitHubSignInDeviceFlowDisabled => Resources.GitHubSignInDeviceFlowDisabled;

    public string GitHubSignInNetworkError => Resources.GitHubSignInNetworkError;

    public string GitHubSignInUnexpected => Resources.GitHubSignInUnexpected;

    public string LibraryFolderMoving => Resources.LibraryFolderMoving;

    public string FormatLibraryFolderCopying(int files, int totalFiles, string megabytes, string totalMegabytes)
        => string.Format(CultureInfo.CurrentCulture, Resources.LibraryFolderCopyingFormat, files, totalFiles, megabytes, totalMegabytes);

    public string LibraryFolderRemovingOldFiles => Resources.LibraryFolderRemovingOldFiles;

    public string FormatLibraryFolderMoved(string folder)
        => string.Format(CultureInfo.CurrentCulture, Resources.LibraryFolderMovedFormat, folder);

    public string FormatLibraryFolderMovedOldFilesRemain(string folder, string previousFolder)
        => string.Format(CultureInfo.CurrentCulture, Resources.LibraryFolderMovedOldFilesRemainFormat, folder, previousFolder);

    public string FormatLibraryFolderAdopted(string folder)
        => string.Format(CultureInfo.CurrentCulture, Resources.LibraryFolderAdoptedFormat, folder);

    public string LibraryFolderCancelled => Resources.LibraryFolderCancelled;

    public string FormatLibraryFolderFailed(string reason)
        => string.Format(CultureInfo.CurrentCulture, Resources.LibraryFolderFailedFormat, reason);

    public string FormatLibraryFolderReloadFailed(string folder, string reason)
        => string.Format(CultureInfo.CurrentCulture, Resources.LibraryFolderReloadFailedFormat, folder, reason);

    public string FormatLibraryFolderNotAbsolute(string folder)
        => string.Format(CultureInfo.CurrentCulture, Resources.LibraryFolderNotAbsoluteFormat, folder);

    public string FormatLibraryFolderIsFile(string folder)
        => string.Format(CultureInfo.CurrentCulture, Resources.LibraryFolderIsFileFormat, folder);

    public string LibraryFolderCurrent => Resources.LibraryFolderCurrent;

    public string FormatLibraryFolderInsideCurrent(string currentFolder)
        => string.Format(CultureInfo.CurrentCulture, Resources.LibraryFolderInsideCurrentFormat, currentFolder);

    public string FormatLibraryFolderContainsCurrent(string currentFolder)
        => string.Format(CultureInfo.CurrentCulture, Resources.LibraryFolderContainsCurrentFormat, currentFolder);

    public string FormatLibraryFolderInsideBoreaFolder(string boreaFolder)
        => string.Format(CultureInfo.CurrentCulture, Resources.LibraryFolderInsideBoreaFolderFormat, boreaFolder);

    public string FormatLibraryFolderContainsBoreaFolder(string boreaFolder)
        => string.Format(CultureInfo.CurrentCulture, Resources.LibraryFolderContainsBoreaFolderFormat, boreaFolder);

    public string LibraryFolderInsideGame => Resources.LibraryFolderInsideGame;

    public string LibraryFolderInsideProfile => Resources.LibraryFolderInsideProfile;

    public string FormatLibraryFolderNotWritable(string folder)
        => string.Format(CultureInfo.CurrentCulture, Resources.LibraryFolderNotWritableFormat, folder);

    public string FormatLibraryFolderTargetNotEmpty(string folder)
        => string.Format(CultureInfo.CurrentCulture, Resources.LibraryFolderTargetNotEmptyFormat, folder);

    public string FormatLibraryFolderBothHaveInstances(string currentFolder, string folder)
        => string.Format(CultureInfo.CurrentCulture, Resources.LibraryFolderBothHaveInstancesFormat, currentFolder, folder);

    public string LibraryFolderGameRunning => Resources.LibraryFolderGameRunning;

    public string LibraryFolderBoreaRunning => Resources.LibraryFolderBoreaRunning;

    public string LibraryFolderInstanceBusy => Resources.LibraryFolderInstanceBusy;

    public string LibraryFolderWaitForTask => Resources.LibraryFolderWaitForTask;

    public string LibraryFolderBusy => Resources.LibraryFolderBusy;

    public string UpdateAvailable => Resources.UpdateAvailable;

    public string UpdatePreRelease => Resources.UpdatePreRelease;

    public string UpdateViewRelease => Resources.UpdateViewRelease;

    public string UpdateSeeMore => Resources.UpdateSeeMore;

    public string UpdateDismiss => Resources.UpdateDismiss;

    public string UpdateNotesTitle => Resources.UpdateNotesTitle;

    public string UpdateInstalled => Resources.UpdateInstalled;

    public string UpdateNoNotes => Resources.UpdateNoNotes;

    public string SelfUpdateNow => Resources.SelfUpdateNow;

    public string FormatSelfUpdateDownloading(string version, int percent)
        => string.Format(CultureInfo.CurrentCulture, Resources.SelfUpdateDownloadingFormat, version, percent);

    public string FormatSelfUpdateVerifying(string version)
        => string.Format(CultureInfo.CurrentCulture, Resources.SelfUpdateVerifyingFormat, version);

    public string FormatSelfUpdateUnpacking(string version)
        => string.Format(CultureInfo.CurrentCulture, Resources.SelfUpdateUnpackingFormat, version);

    public string FormatSelfUpdateReady(string version)
        => string.Format(CultureInfo.CurrentCulture, Resources.SelfUpdateReadyFormat, version);

    public string FormatSelfUpdatePackageManaged(string packageManager)
        => string.Format(CultureInfo.CurrentCulture, Resources.SelfUpdatePackageManagedFormat, packageManager);

    public string SelfUpdatePackageManaged => Resources.SelfUpdatePackageManaged;

    public string SelfUpdateReadOnly => Resources.SelfUpdateReadOnly;

    public string SelfUpdateNotAReleaseBuild => Resources.SelfUpdateNotAReleaseBuild;

    public string SelfUpdateUnsupportedPlatform => Resources.SelfUpdateUnsupportedPlatform;

    public string SelfUpdateDownloadFailed => Resources.SelfUpdateDownloadFailed;

    public string SelfUpdateChecksumFailed => Resources.SelfUpdateChecksumFailed;

    public string SelfUpdateUnpackFailed => Resources.SelfUpdateUnpackFailed;

    public string SelfUpdateInstallFailed => Resources.SelfUpdateInstallFailed;

    public string FormatSelfUpdateRestoreFailed(string replacedName, string programName)
        => string.Format(CultureInfo.CurrentCulture, Resources.SelfUpdateRestoreFailedFormat, replacedName, programName);

    public string FormatSelfUpdateStartAgain(string version)
        => string.Format(CultureInfo.CurrentCulture, Resources.SelfUpdateStartAgainFormat, version);

    public string GameBuildAvailable => Resources.GameBuildAvailable;

    public string GameBuildDownload => Resources.GameBuildDownload;

    public string GamePatchNotesTitle => Resources.GamePatchNotesTitle;

    public string GamePatchNotesEmpty => Resources.GamePatchNotesEmpty;

    public string GamePatchNotesNotInstalled => Resources.GamePatchNotesNotInstalled;

    public string GamePatchNotesLoading => Resources.GamePatchNotesLoading;

    public string GamePatchNotesLoadFailed => Resources.GamePatchNotesLoadFailed;

    public string FormatGamePatchNotesCapped(int count)
        => string.Format(CultureInfo.CurrentCulture, Resources.GamePatchNotesCappedFormat, count);

    public string GameShapeBrokenTitle => Resources.GameShapeBrokenTitle;

    public string FormatGameShapeBroken(string? build, string assumptions)
        => build is null
            ? string.Format(CultureInfo.CurrentCulture, Resources.GameShapeBrokenUnknownBuildFormat, assumptions)
            : string.Format(CultureInfo.CurrentCulture, Resources.GameShapeBrokenFormat, build, assumptions);

    public string GameShapeUntestedTitle => Resources.GameShapeUntestedTitle;

    public string FormatGameShapeUntested(string build, string newestVerified)
        => string.Format(CultureInfo.CurrentCulture, Resources.GameShapeUntestedFormat, build, newestVerified);

    public string GameShapeReport => Resources.GameShapeReport;

    public string AnnouncementOpenLink => Resources.AnnouncementOpenLink;

    public string SystemDefaultRegionalFormat => Resources.SystemDefaultRegionalFormat;

    public string HandoverFailed => Resources.HandoverFailed;

    public string HomeCurrentInstall => Resources.HomeCurrentInstall;

    public string HomeRecentlyUpdated => Resources.HomeRecentlyUpdated;

    public string HomeDiscoverMods => Resources.HomeDiscoverMods;

    public string HomeNoGameVersion => Resources.HomeNoGameVersion;

    public string HomeNoInstance => Resources.HomeNoInstance;

    public string HomeNoActiveInstance => Resources.HomeNoActiveInstance;

    public string HomeInstanceSourceCustom => Resources.HomeInstanceSourceCustom;

    public string FormatHomeUpdates(int count) => FormatCount(count, Resources.HomeUpdate, Resources.HomeUpdatesFormat);

    public string ContentTypeMod => Resources.ContentTypeMod;

    public string LibraryNewInstancePlaceholder => Resources.LibraryNewInstancePlaceholder;

    public string LibraryCreate => Resources.LibraryCreate;

    public string LibraryNewInstance => Resources.LibraryNewInstance;

    public string LibraryImportFromProfile => Resources.LibraryImportFromProfile;

    public string LibraryImportFromProfileToolTip => Resources.LibraryImportFromProfileToolTip;

    public string LibraryActivate => Resources.LibraryActivate;

    public string LibraryRename => Resources.LibraryRename;

    public string LibraryLaunchArguments => Resources.LibraryLaunchArguments;

    public string LibraryOpenFolder => Resources.LibraryOpenFolder;

    public string LibraryDelete => Resources.LibraryDelete;

    public string LibrarySave => Resources.LibrarySave;

    public string LibraryCancel => Resources.LibraryCancel;

    public string LibraryDeleteConfirm => Resources.LibraryDeleteConfirm;

    public string LibraryEmpty => Resources.LibraryEmpty;

    public string LibraryNeverPlayed => Resources.LibraryNeverPlayed;

    public string LibrarySortName => Resources.LibrarySortName;

    public string LibrarySortLastPlayed => Resources.LibrarySortLastPlayed;

    public string LibraryActiveHeading => Resources.LibraryActiveHeading;

    public string LibraryOtherInstancesHeading => Resources.LibraryOtherInstancesHeading;

    public string FormatLibraryLastPlayed(string date)
        => string.Format(CultureInfo.CurrentCulture, Resources.LibraryLastPlayedFormat, date);

    public string FormatLibraryNowActive(string instanceName)
        => string.Format(CultureInfo.CurrentCulture, Resources.LibraryNowActiveFormat, instanceName);

    public string LibraryImportModList => Resources.LibraryImportModList;

    public string LibraryMoreActions => Resources.LibraryMoreActions;

    public string LibraryDuplicate => Resources.LibraryDuplicate;

    public string LibraryExportModList => Resources.LibraryExportModList;

    public string LibraryCopyModList => Resources.LibraryCopyModList;

    public string ModListDuplicateTitle => Resources.ModListDuplicateTitle;

    public string ModListFileType => Resources.ModListFileType;

    public string InstanceTabContent => Resources.InstanceTabContent;

    public string InstanceTabManualInstalls => Resources.InstanceTabManualInstalls;

    public string InstanceTabGameData => Resources.InstanceTabGameData;

    public string InstanceTabLog => Resources.InstanceTabLog;

    public string InstanceGroupModpacks => Resources.InstanceGroupModpacks;

    public string InstanceGroupMods => Resources.InstanceGroupMods;

    public string InstanceGroupModLoaders => Resources.InstanceGroupModLoaders;

    public string InstanceGroupOther => Resources.InstanceGroupOther;

    public string InstanceGroupVehicles => Resources.InstanceGroupVehicles;

    public string InstanceGroupSaves => Resources.InstanceGroupSaves;

    public string InstanceGroupDependencies => Resources.InstanceGroupDependencies;

    public string InstanceEmptyContent => Resources.InstanceEmptyContent;

    public string ContentInsideInstance => Resources.ContentInsideInstance;

    public string ContentViewInstance => Resources.ContentViewInstance;

    public string ContentInactiveInstance => Resources.ContentInactiveInstance;

    public string InstanceContentNotInIndex => Resources.InstanceContentNotInIndex;

    public string ManualInstallsEmpty => Resources.ManualInstallsEmpty;

    public string ManualInstallsInIndex => Resources.ManualInstallsInIndex;

    public string ManualInstallsNotInIndex => Resources.ManualInstallsNotInIndex;

    public string ManualInstallsNoMatch => Resources.ManualInstallsNoMatch;

    public string ManualInstallsNotRecorded => Resources.ManualInstallsNotRecorded;

    public string ManualInstallsChecking => Resources.ManualInstallsChecking;

    public string ManualInstallsManage => Resources.ManualInstallsManage;

    public string ManualInstallsReplace => Resources.ManualInstallsReplace;

    public string ManualInstallsDeleteAndReplace => Resources.ManualInstallsDeleteAndReplace;

    public string ManualInstallsInstanceChanged => Resources.ManualInstallsInstanceChanged;

    public string GameDataOpenFolder => Resources.GameDataOpenFolder;

    public string GameDataEmpty => Resources.GameDataEmpty;

    public string GameSaveNoVehicles => Resources.GameSaveNoVehicles;

    public string GameSaveNoSaves => Resources.GameSaveNoSaves;

    public string GameSaveOlderBuild => Resources.GameSaveOlderBuild;

    public string GameSaveBackUp => Resources.GameSaveBackUp;

    public string GameSaveCopyToInstance => Resources.GameSaveCopyToInstance;

    public string GameSaveCopy => Resources.GameSaveCopy;

    public string GameSaveCopyModsNote => Resources.GameSaveCopyModsNote;

    public string GameSaveNoOtherInstance => Resources.GameSaveNoOtherInstance;

    public string GameSaveReplace => Resources.GameSaveReplace;

    public string GameSaveDeleteConfirm => Resources.GameSaveDeleteConfirm;

    public string GameSaveCloseGame => Resources.GameSaveCloseGame;

    public string GameSaveCopyFromProfile => Resources.GameSaveCopyFromProfile;

    public string MoreInformation => Resources.MoreInformation;

    public string FormatGameSaveProfileInfo(string folder)
        => string.Format(CultureInfo.CurrentCulture, Resources.GameSaveProfileInfoFormat, folder);

    public string GameSaveInstanceStartsEmpty => Resources.GameSaveInstanceStartsEmpty;

    public string GameSaveProfileEmpty => Resources.GameSaveProfileEmpty;

    public string FormatGameSaveProfileSelected(int selected, int total)
        => string.Format(CultureInfo.CurrentCulture, Resources.GameSaveProfileSelectedFormat, selected, total);

    public string GameSaveExistsInInstance => Resources.GameSaveExistsInInstance;

    public string GameSaveProfileReplace => Resources.GameSaveProfileReplace;

    public string GameSavesNothingToBackUp => Resources.GameSavesNothingToBackUp;

    public string InstanceBackUpAllSaves => Resources.InstanceBackUpAllSaves;

    public string BackupsTitle => Resources.BackupsTitle;

    public string BackupsEmpty => Resources.BackupsEmpty;

    public string BackupRestore => Resources.BackupRestore;

    public string BackupUnknownOrigin => Resources.BackupUnknownOrigin;

    public string BackupDeleteConfirm => Resources.BackupDeleteConfirm;

    public string GameLogOpen => Resources.GameLogOpen;

    public string GameLogMissing => Resources.GameLogMissing;

    public string GameLogReload => Resources.GameLogReload;

    public string GameLogCopy => Resources.GameLogCopy;

    public string InstancePlay => Resources.InstancePlay;

    public string InstanceUpdateAll => Resources.InstanceUpdateAll;

    public string InstanceNoPlaytime => Resources.InstanceNoPlaytime;

    public string InstancePlaytimeUnknown => Resources.InstancePlaytimeUnknown;

    public string InstancePlaytimeToolTip => Resources.InstancePlaytimeToolTip;

    public string InstancePlaytimeUnknownToolTip => Resources.InstancePlaytimeUnknownToolTip;

    public string FormatInstancePlayed(string duration)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstancePlayedFormat, duration);

    public string FormatInstancePlayedRunning(string duration)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstancePlayedRunningFormat, duration);

    public string FormatInstanceSessions(int count)
        => FormatCount(count, Resources.InstanceSessionOne, Resources.InstanceSessionsFormat);

    /// <summary>"12 h 40 min", or "40 min" under an hour, with the minutes rounded down.</summary>
    public string FormatDuration(TimeSpan duration)
        => duration.TotalHours >= 1
            ? string.Format(CultureInfo.CurrentCulture, Resources.DurationHoursMinutesFormat, (int)duration.TotalHours, duration.Minutes)
            : string.Format(CultureInfo.CurrentCulture, Resources.DurationMinutesFormat, (int)duration.TotalMinutes);

    public string ContentRemove => Resources.ContentRemove;

    public string ContentRemoveConfirm => Resources.ContentRemoveConfirm;

    public string ContentChangeVersion => Resources.ContentChangeVersion;

    public string ContentNotOnDisk => Resources.ContentNotOnDisk;

    public string ContentNotOnDiskDetail => Resources.ContentNotOnDiskDetail;

    public string ContentNotOnDiskEnable => Resources.ContentNotOnDiskEnable;

    public string ContentInstallAgain => Resources.ContentInstallAgain;

    public string ContentRemoveFromList => Resources.ContentRemoveFromList;

    public string ContentRemoveFromListConfirm => Resources.ContentRemoveFromListConfirm;

    public string ToastMissingScanFailed => Resources.ToastMissingScanFailed;

    /// <summary>"The folder 'HudCore' has no mod.toml, so Borea cannot install over it."</summary>
    public string FormatContentFolderInTheWay(string folderName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ContentFolderInTheWayFormat, folderName);

    /// <summary>"HudCore is on disk again, so Borea changed nothing."</summary>
    public string FormatContentBackOnDisk(string content)
        => string.Format(CultureInfo.CurrentCulture, Resources.ContentBackOnDiskFormat, content);

    /// <summary>"3 mods of this instance are not on disk."</summary>
    public string FormatInstanceModsMissing(int count)
        => FormatCount(count, Resources.InstanceModsMissingOne, Resources.InstanceModsMissingFormat);
    public string ContentEnabled => Resources.ContentEnabled;

    public string ContentManage => Resources.ContentManage;

    public string ContentManageConfirmAction => Resources.ContentManageConfirmAction;

    public string DiscoverTabMods => Resources.DiscoverTabMods;

    public string DiscoverTabModpacks => Resources.DiscoverTabModpacks;

    public string DiscoverTabVehicles => Resources.DiscoverTabVehicles;

    public string DiscoverTabSaves => Resources.DiscoverTabSaves;

    public string DiscoverTabLoaders => Resources.DiscoverTabLoaders;

    public string DiscoverSearchPlaceholder => Resources.DiscoverSearchPlaceholder;

    public string DiscoverHideInstalled => Resources.DiscoverHideInstalled;

    public string DiscoverHideIncompatible => Resources.DiscoverHideIncompatible;

    public string DiscoverCategory => Resources.DiscoverCategory;

    public string DiscoverCategoryParts => Resources.DiscoverCategoryParts;

    public string DiscoverCategoryCelestial => Resources.DiscoverCategoryCelestial;

    public string DiscoverCategoryGameplay => Resources.DiscoverCategoryGameplay;

    public string DiscoverCategoryUserInterface => Resources.DiscoverCategoryUserInterface;

    public string DiscoverCategoryVisual => Resources.DiscoverCategoryVisual;

    public string DiscoverCategoryAudio => Resources.DiscoverCategoryAudio;

    public string DiscoverCategoryTools => Resources.DiscoverCategoryTools;

    public string DiscoverCategoryLibrary => Resources.DiscoverCategoryLibrary;

    public string DiscoverCategoryOther => Resources.DiscoverCategoryOther;

    public string DiscoverGameVersionMin => Resources.DiscoverGameVersionMin;

    public string DiscoverGameVersionMax => Resources.DiscoverGameVersionMax;

    public string DiscoverOperatingSystem => Resources.DiscoverOperatingSystem;

    public string DiscoverLicense => Resources.DiscoverLicense;

    public string DiscoverClearAll => Resources.DiscoverClearAll;

    public string DiscoverSortBy => Resources.DiscoverSortBy;

    public string DiscoverSortPopularity => Resources.DiscoverSortPopularity;

    public string DiscoverSortRecentlyUpdated => Resources.DiscoverSortRecentlyUpdated;

    public string DiscoverSortName => Resources.DiscoverSortName;

    public string DiscoverAll => Resources.DiscoverAll;

    public string DiscoverEmpty => Resources.DiscoverEmpty;

    public string DiscoverLoading => Resources.DiscoverLoading;

    public string DiscoverIndexRetry => Resources.DiscoverIndexRetry;

    public string DiscoverRefresh => Resources.DiscoverRefresh;

    public string DiscoverAdd => Resources.DiscoverAdd;

    public string DiscoverInstalled => Resources.DiscoverInstalled;

    public string DiscoverNoInstance => Resources.DiscoverNoInstance;

    public string DiscoverNoActiveInstance => Resources.DiscoverNoActiveInstance;

    public string DiscoverCreateInstance => Resources.DiscoverCreateInstance;

    public string DiscoverOpenLibrary => Resources.DiscoverOpenLibrary;

    public string DiscoverNoRelease => Resources.DiscoverNoRelease;

    public string LinkRefused => Resources.LinkRefused;

    public string FormatLinkNotInIndex(string id)
        => string.Format(CultureInfo.CurrentCulture, Resources.LinkNotInIndexFormat, id);

    public string FormatLinkAlreadyInstalled(string content, string instance)
        => string.Format(CultureInfo.CurrentCulture, Resources.LinkAlreadyInstalledFormat, content, instance);

    public string FormatLinkInstall(string content, string instance)
        => string.Format(CultureInfo.CurrentCulture, Resources.LinkInstallFormat, content, instance);

    public string FormatLinkVersionMissing(string version)
        => string.Format(CultureInfo.CurrentCulture, Resources.LinkVersionMissingFormat, version);

    public string FormatLinkVersionYanked(string version)
        => string.Format(CultureInfo.CurrentCulture, Resources.LinkVersionYankedFormat, version);

    public string DiscoverListYourMod => Resources.DiscoverListYourMod;

    public string DiscoverForModAuthors => Resources.DiscoverForModAuthors;

    public string DiscoverForModAuthorsText => Resources.DiscoverForModAuthorsText;

    public string ListingIntro => Resources.ListingIntro;

    public string ListingNewTitle => Resources.ListingNewTitle;

    public string ListingNewHint => Resources.ListingNewHint;

    public string ListingSourcePlaceholder => Resources.ListingSourcePlaceholder;

    public string ListingReadSource => Resources.ListingReadSource;

    public string ListingStartEmpty => Resources.ListingStartEmpty;

    public string ListingChangeTitle => Resources.ListingChangeTitle;

    public string ListingChangeHint => Resources.ListingChangeHint;

    public string ListingSearchListed => Resources.ListingSearchListed;

    public string ListingYourMod => Resources.ListingYourMod;

    public string ListingNoListedMatch => Resources.ListingNoListedMatch;

    public string ListingLoad => Resources.ListingLoad;

    public string ListingSourceInvalid => Resources.ListingSourceInvalid;

    public string ListingReading => Resources.ListingReading;

    public string ListingStartOver => Resources.ListingStartOver;

    public string ListingNoRelease => Resources.ListingNoRelease;

    public string ListingAbout => Resources.ListingAbout;

    public string ListingLinks => Resources.ListingLinks;

    public string ListingReleases => Resources.ListingReleases;

    public string ListingCompatibility => Resources.ListingCompatibility;

    public string ListingDependencies => Resources.ListingDependencies;

    public string ListingTags => Resources.ListingTags;

    public string ListingImages => Resources.ListingImages;

    public string ListingStatus => Resources.ListingStatus;

    public string ListingId => Resources.ListingId;

    public string ListingIdHint => Resources.ListingIdHint;

    public string ListingName => Resources.ListingName;

    public string ListingAuthors => Resources.ListingAuthors;

    public string ListingAuthorsHint => Resources.ListingAuthorsHint;

    public string ListingAbstract => Resources.ListingAbstract;

    public string ListingAbstractHint => Resources.ListingAbstractHint;

    public string ListingDescription => Resources.ListingDescription;

    public string ListingDescriptionHint => Resources.ListingDescriptionHint;

    public string ListingLicense => Resources.ListingLicense;

    public string ListingLicenseHint => Resources.ListingLicenseHint;

    public string ListingForumsHint => Resources.ListingForumsHint;

    public string ListingReleasesGitHub => Resources.ListingReleasesGitHub;

    public string ListingReleasesSpaceDock => Resources.ListingReleasesSpaceDock;

    public string ListingReleasesAuthority => Resources.ListingReleasesAuthority;

    public string ListingReleasesHint => Resources.ListingReleasesHint;

    public string ListingGameMin => Resources.ListingGameMin;

    public string ListingGameMax => Resources.ListingGameMax;

    public string ListingGameMaxHint => Resources.ListingGameMaxHint;

    public string ListingGameHint => Resources.ListingGameHint;

    public string ListingUsesLoader => Resources.ListingUsesLoader;

    public string ListingLoaderId => Resources.ListingLoaderId;

    public string ListingLoaderMin => Resources.ListingLoaderMin;

    public string ListingLoaderMax => Resources.ListingLoaderMax;

    public string ListingLoaderMaxHint => Resources.ListingLoaderMaxHint;

    public string ListingDependenciesHint => Resources.ListingDependenciesHint;

    public string ListingAddDependency => Resources.ListingAddDependency;

    public string ListingDependencyKind => Resources.ListingDependencyKind;

    public string ListingDependencyMin => Resources.ListingDependencyMin;

    public string ListingDependencyMax => Resources.ListingDependencyMax;

    public string ListingRemove => Resources.ListingRemove;

    public string ListingMoreTags => Resources.ListingMoreTags;

    public string ListingMoreTagsHint => Resources.ListingMoreTagsHint;

    public string ListingIcon => Resources.ListingIcon;

    public string ListingIconHint => Resources.ListingIconHint;

    public string ListingAddIcon => Resources.ListingAddIcon;

    public string ListingDescriptionImages => Resources.ListingDescriptionImages;

    public string ListingAddImage => Resources.ListingAddImage;

    public string ListingImageUrl => Resources.ListingImageUrl;

    public string ListingImageUrlHint => Resources.ListingImageUrlHint;

    public string ListingChooseFile => Resources.ListingChooseFile;

    public string ListingMeasure => Resources.ListingMeasure;

    public string ListingImageFileType => Resources.ListingImageFileType;

    public string ListingImageLicense => Resources.ListingImageLicense;

    public string ListingImageAttribution => Resources.ListingImageAttribution;

    public string ListingImageSource => Resources.ListingImageSource;

    public string ListingImageNotMeasured => Resources.ListingImageNotMeasured;

    public string ListingDeprecated => Resources.ListingDeprecated;

    public string ListingSupersededBy => Resources.ListingSupersededBy;

    public string ListingPreview => Resources.ListingPreview;

    public string ListingPreviewName => Resources.ListingPreviewName;

    public string ListingPreviewAbstract => Resources.ListingPreviewAbstract;

    public string ListingSteps => Resources.ListingSteps;

    public string ListingStepDone => Resources.ListingStepDone;

    public string ListingStepToDo => Resources.ListingStepToDo;

    public string ListingStepOptional => Resources.ListingStepOptional;

    public string ListingStepRecommended => Resources.ListingStepRecommended;

    public string ListingErrorsHeading => Resources.ListingErrorsHeading;

    public string ListingNotesHeading => Resources.ListingNotesHeading;

    public string ListingNoIssues => Resources.ListingNoIssues;

    public string ListingSchemaDownloaded => Resources.ListingSchemaDownloaded;

    public string ListingSchemaCached => Resources.ListingSchemaCached;

    public string ListingSchemaEmbedded => Resources.ListingSchemaEmbedded;

    public string ListingFile => Resources.ListingFile;

    public string ListingCopy => Resources.ListingCopy;

    public string ListingSaveAs => Resources.ListingSaveAs;

    public string ListingFileType => Resources.ListingFileType;

    public string ListingOpenPullRequest => Resources.ListingOpenPullRequest;

    public string ListingNewPullRequestText => Resources.ListingNewPullRequestText;

    public string ListingEditPullRequestText => Resources.ListingEditPullRequestText;

    public string ListingFixErrors => Resources.ListingFixErrors;

    public string ListingCopied => Resources.ListingCopied;

    public string ListingSaveFailed => Resources.ListingSaveFailed;

    public string ListingOpenedWithText => Resources.ListingOpenedWithText;

    public string ListingOpenedPaste => Resources.ListingOpenedPaste;

    public string ListingPublish => Resources.ListingPublish;

    public string ListingUpdatePullRequest => Resources.ListingUpdatePullRequest;

    public string ListingSignInText => Resources.ListingSignInText;

    public string ListingSignIn => Resources.ListingSignIn;

    public string ListingOpenInBrowser => Resources.ListingOpenInBrowser;

    public string ListingOwnershipChecking => Resources.ListingOwnershipChecking;

    public string ListingOwnershipVerified => Resources.ListingOwnershipVerified;

    public string ListingOwnershipSteward => Resources.ListingOwnershipSteward;

    public string ListingOwnershipUnknown => Resources.ListingOwnershipUnknown;

    public string ListingFixNoHost => Resources.ListingFixNoHost;

    public string ListingOpenRepository => Resources.ListingOpenRepository;

    public string ListingOpenSpaceDock => Resources.ListingOpenSpaceDock;

    public string ListingOpenYourPullRequest => Resources.ListingOpenYourPullRequest;

    public string ListingCheckAgain => Resources.ListingCheckAgain;

    public string ListingForkMissing => Resources.ListingForkMissing;

    public string ListingMakeFork => Resources.ListingMakeFork;

    public string ListingAllowOnFork => Resources.ListingAllowOnFork;

    public string ListingStepFindPullRequest => Resources.ListingStepFindPullRequest;

    public string ListingStepFork => Resources.ListingStepFork;

    public string ListingStepBranch => Resources.ListingStepBranch;

    public string ListingStepCommit => Resources.ListingStepCommit;

    public string ListingStepPullRequest => Resources.ListingStepPullRequest;

    public string ListingErrorSignedOut => Resources.ListingErrorSignedOut;

    public string ListingErrorNetwork => Resources.ListingErrorNetwork;

    public string ListingErrorNoChange => Resources.ListingErrorNoChange;

    public string ListingWhatPullRequests => Resources.ListingWhatPullRequests;

    public string ListingWhatFork => Resources.ListingWhatFork;

    public string ListingWhatBranch => Resources.ListingWhatBranch;

    public string ListingWhatFile => Resources.ListingWhatFile;

    public string ListingWhatPullRequest => Resources.ListingWhatPullRequest;

    public string ListingWhatStatus => Resources.ListingWhatStatus;

    public string ListingRefresh => Resources.ListingRefresh;

    public string ListingStateRunning => Resources.ListingStateRunning;

    public string ListingStateMerging => Resources.ListingStateMerging;

    public string ListingStateSteward => Resources.ListingStateSteward;

    public string ListingStateRejected => Resources.ListingStateRejected;

    public string ListingStateNoVerdict => Resources.ListingStateNoVerdict;

    public string ListingStateMerged => Resources.ListingStateMerged;

    public string ListingStateClosed => Resources.ListingStateClosed;

    public string ListingFollowSignedOut => Resources.ListingFollowSignedOut;

    public string InstallAnyway => Resources.InstallAnyway;

    public string FormatInstallAlsoAdds(string mods)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstallAlsoAddsFormat, mods);

    public string FormatInstallAlsoAddsMore(string mods, int more)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstallAlsoAddsMoreFormat, mods, more);

    public string InstallChoicesRecommended => Resources.InstallChoicesRecommended;

    public string InstallChoicesSuggested => Resources.InstallChoicesSuggested;

    public string UpdateAnyway => Resources.UpdateAnyway;

    public string ContentUpdate => Resources.ContentUpdate;

    public string InstallInstanceMissing => Resources.InstallInstanceMissing;

    public string ModalCreateInstanceTitle => Resources.ModalCreateInstanceTitle;

    public string ModalRenameInstanceTitle => Resources.ModalRenameInstanceTitle;

    public string ModalInstanceExplanation => Resources.ModalInstanceExplanation;

    public string ModalNameRequired => Resources.ModalNameRequired;

    public string ModalNameTaken => Resources.ModalNameTaken;

    public string ModalNameLabel => Resources.ModalNameLabel;

    public string ModalLaunchArgumentsLabel => Resources.ModalLaunchArgumentsLabel;

    public string ModalLaunchArgumentsHint => Resources.ModalLaunchArgumentsHint;

    public string ModalLaunchArgumentsNone => Resources.ModalLaunchArgumentsNone;

    public string ModalClose => Resources.ModalClose;

    public string SettingsGeneral => Resources.SettingsGeneral;

    public string SettingsAbout => Resources.SettingsAbout;

    public string SettingsHelp => Resources.SettingsHelp;

    public string AboutVersion => Resources.AboutVersion;

    public string AboutRuntime => Resources.AboutRuntime;

    public string AboutSystem => Resources.AboutSystem;

    public string AboutGame => Resources.AboutGame;

    public string AboutContentIndex => Resources.AboutContentIndex;

    public string AboutIndexNotDownloaded => Resources.AboutIndexNotDownloaded;

    public string AboutOpenBoreaLog => Resources.AboutOpenBoreaLog;

    public string AboutOpenLogFolder => Resources.AboutOpenLogFolder;

    public string AboutCopyDiagnostics => Resources.AboutCopyDiagnostics;

    public string AboutCopyPath => Resources.AboutCopyPath;

    public string AboutFolders => Resources.AboutFolders;

    public string AboutOpenBoreaFolder => Resources.AboutOpenBoreaFolder;

    public string AboutOpenInstancesFolder => Resources.AboutOpenInstancesFolder;

    public string AboutCopied => Resources.AboutCopied;

    public string AboutLinks => Resources.AboutLinks;

    public string AboutSourceCode => Resources.AboutSourceCode;

    public string AboutReportBug => Resources.AboutReportBug;

    public string AboutReleases => Resources.AboutReleases;

    public string AboutCommunity => Resources.AboutCommunity;

    public string AboutDiscord => Resources.AboutDiscord;

    public string AboutCredits => Resources.AboutCredits;

    public string AboutCreditsHint => Resources.AboutCreditsHint;

    public string AboutOpenNotices => Resources.AboutOpenNotices;

    public string AboutNoticesMissing => Resources.AboutNoticesMissing;

    public string HelpAsk => Resources.HelpAsk;

    public string HelpReportHint => Resources.HelpReportHint;

    public string HelpForums => Resources.HelpForums;

    public string HelpFacts => Resources.HelpFacts;


    public string ContentLoaderHint => Resources.ContentLoaderHint;

    public string LinkForum => Resources.LinkForum;

    public string LinkRepository => Resources.LinkRepository;

    public string LinkBugTracker => Resources.LinkBugTracker;

    public string LinkHomepage => Resources.LinkHomepage;

    public string LinkDiscussions => Resources.LinkDiscussions;

    public string ContentTabDescription => Resources.ContentTabDescription;

    public string ContentTabDependencies => Resources.ContentTabDependencies;

    public string ContentTabVersions => Resources.ContentTabVersions;

    public string FormatContentDependenciesOf(string version)
        => string.Format(CultureInfo.CurrentCulture, Resources.ContentDependenciesOfFormat, version);

    public string ContentDependenciesNoRelease => Resources.ContentDependenciesNoRelease;

    public string ContentNoDependencies => Resources.ContentNoDependencies;

    public string FormatContentDependencyLoader(string loader, string bounds)
        => string.Format(CultureInfo.CurrentCulture, Resources.ContentDependencyLoaderFormat, loader, bounds);

    public string ContentDependencyRequired => Resources.ContentDependencyRequired;

    public string ContentDependencyRequiredHint => Resources.ContentDependencyRequiredHint;

    public string ContentDependencyAnyOf => Resources.ContentDependencyAnyOf;

    public string ContentDependencyAnyOfHint => Resources.ContentDependencyAnyOfHint;

    public string ContentDependencyConflicts => Resources.ContentDependencyConflicts;

    public string ContentDependencyConflictsHint => Resources.ContentDependencyConflictsHint;

    public string ContentDependencyRecommended => Resources.ContentDependencyRecommended;

    public string ContentDependencyRecommendedHint => Resources.ContentDependencyRecommendedHint;

    public string ContentDependencySuggested => Resources.ContentDependencySuggested;

    public string ContentDependencySuggestedHint => Resources.ContentDependencySuggestedHint;

    public string ContentDependencyOptional => Resources.ContentDependencyOptional;

    public string ContentDependencyOptionalHint => Resources.ContentDependencyOptionalHint;

    public string ContentDependencyOther => Resources.ContentDependencyOther;

    public string ContentDependencyOtherHint => Resources.ContentDependencyOtherHint;

    public string ContentDependencyOneOf => Resources.ContentDependencyOneOf;

    public string FormatContentDependencyMin(string low)
        => string.Format(CultureInfo.CurrentCulture, Resources.ContentDependencyMinFormat, low);

    public string FormatContentDependencyMax(string high)
        => string.Format(CultureInfo.CurrentCulture, Resources.ContentDependencyMaxFormat, high);

    public string FormatContentDependencyRange(string low, string high)
        => string.Format(CultureInfo.CurrentCulture, Resources.ContentDependencyRangeFormat, low, high);

    public string FormatContentDependencyInstalled(string version)
        => string.Format(CultureInfo.CurrentCulture, Resources.ContentDependencyInstalledFormat, version);

    public string ContentDependencyNotInstalled => Resources.ContentDependencyNotInstalled;

    public string ContentDependencyNotInIndex => Resources.ContentDependencyNotInIndex;

    public string ContentCompatibility => Resources.ContentCompatibility;

    public string CompatibilityCompatible => Resources.CompatibilityCompatible;

    public string CompatibilityUntested => Resources.CompatibilityUntested;

    public string CompatibilityIncompatible => Resources.CompatibilityIncompatible;

    public string CompatibilityUnknown => Resources.CompatibilityUnknown;

    public string ContentLinks => Resources.ContentLinks;

    public string ContentCopyLink => Resources.ContentCopyLink;

    public string ContentLinkCopied => Resources.ContentLinkCopied;

    public string ContentTags => Resources.ContentTags;

    public string ContentAuthor => Resources.ContentAuthor;

    public string ContentDetails => Resources.ContentDetails;

    public string ContentIconCredit => Resources.ContentIconCredit;

    public string ContentIconSource => Resources.ContentIconSource;

    public string ContentAdd => Resources.ContentAdd;

    public string ContentVersionHeader => Resources.ContentVersionHeader;

    public string ContentChannelHeader => Resources.ContentChannelHeader;

    public string ContentGameVersionHeader => Resources.ContentGameVersionHeader;

    public string ContentPublishedHeader => Resources.ContentPublishedHeader;

    public string ContentDownloads => Resources.ContentDownloads;

    public string ContentDownloadSize => Resources.ContentDownloadSize;

    public string ContentSizeHeader => Resources.ContentSizeHeader;

    public string ContentSizeOnDisk => Resources.ContentSizeOnDisk;

    public string InstanceSizeToolTip => Resources.InstanceSizeToolTip;

    public string InstanceLoaderTitle => Resources.InstanceLoaderTitle;

    public string InstanceLoaderNone => Resources.InstanceLoaderNone;

    public string InstanceLoaderNotInstalled => Resources.InstanceLoaderNotInstalled;

    public string InstanceLoaderVersionUnknown => Resources.InstanceLoaderVersionUnknown;

    public string InstanceLoaderConflict => Resources.InstanceLoaderConflict;

    public string InstanceLoaderOpenSettings => Resources.InstanceLoaderOpenSettings;

    public string InstanceLoaderConflictShort => Resources.InstanceLoaderConflictShort;

    public string InstanceLoaderNotInIndex => Resources.InstanceLoaderNotInIndex;

    public string FormatInstanceLoaderModNeeds(string mod, string bounds)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstanceLoaderModNeedsFormat, mod, bounds);

    public string FormatInstanceLoaderBoundsRange(string min, string max)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstanceLoaderBoundsRangeFormat, min, max);

    public string FormatInstanceLoaderBoundsMinimum(string min)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstanceLoaderBoundsMinimumFormat, min);

    public string FormatInstanceLoaderDifferent(string loaders)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstanceLoaderDifferentFormat, loaders);

    public string FormatInstanceLoaderRange(string min, string max)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstanceLoaderRangeFormat, min, max);

    public string FormatInstanceLoaderMinimum(string min)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstanceLoaderMinimumFormat, min);

    public string FormatInstanceLoaderNeededBy(string mods)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstanceLoaderNeededByFormat, mods);

    public string FormatInstanceLoaderInstalled(string version)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstanceLoaderInstalledFormat, version);

    public string FormatInstanceLoaderWrongVersion(string version)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstanceLoaderWrongVersionFormat, version);

    public string ContentShowVersions => Resources.ContentShowVersions;

    public string ContentNoDescription => Resources.ContentNoDescription;

    public string ContentLoadingVersions => Resources.ContentLoadingVersions;

    public string ContentNoVersions => Resources.ContentNoVersions;

    public string ContentNoVersionsInChannel => Resources.ContentNoVersionsInChannel;

    public string ContentChangelog => Resources.ContentChangelog;

    public string ContentTypeModLoader => Resources.ContentTypeModLoader;

    public string ContentTypeModPack => Resources.ContentTypeModPack;

    public string PackMods => Resources.PackMods;

    public string PackNewInstance => Resources.PackNewInstance;

    public string FormatPackCreatesInstance(string instanceName)
        => string.Format(CultureInfo.CurrentCulture, Resources.PackCreatesInstanceFormat, instanceName);

    public string PackUpdate => Resources.PackUpdate;

    public string FormatPackUpdateAvailable(string packName, string version)
        => string.Format(CultureInfo.CurrentCulture, Resources.PackUpdateAvailableFormat, packName, version);

    public string PackUpdateNotNewer => Resources.PackUpdateNotNewer;

    public string FormatPackUpdateAdd(string name, string version)
        => string.Format(CultureInfo.CurrentCulture, Resources.PackUpdateAddFormat, name, version);

    public string FormatPackUpdateChange(string name, string from, string to)
        => string.Format(CultureInfo.CurrentCulture, Resources.PackUpdateChangeFormat, name, from, to);

    public string FormatPackUpdateRemove(string name, string version)
        => string.Format(CultureInfo.CurrentCulture, Resources.PackUpdateRemoveFormat, name, version);

    public string FormatPackUpdateKeep(string name, string version)
        => string.Format(CultureInfo.CurrentCulture, Resources.PackUpdateKeepFormat, name, version);

    public string FormatPackUpdateKeepChange(string name, string from, string to)
        => string.Format(CultureInfo.CurrentCulture, Resources.PackUpdateKeepChangeFormat, name, from, to);

    public string PackUpdateNoModChanges => Resources.PackUpdateNoModChanges;

    public string FormatPackUpdateStillInstalled(string names)
        => string.Format(CultureInfo.CurrentCulture, Resources.PackUpdateStillInstalledFormat, names);

    public string PackModHeader => Resources.PackModHeader;

    public string PackMemberYanked => Resources.PackMemberYanked;

    public string PackMemberUnlisted => Resources.PackMemberUnlisted;

    public string PackDeprecated => Resources.PackDeprecated;

    public string PackStatusUnknown => Resources.PackStatusUnknown;

    public string PackCompatibilityUnknown => Resources.PackCompatibilityUnknown;

    public string PackResultInstalled => Resources.PackResultInstalled;

    public string PackResultReplaced => Resources.PackResultReplaced;

    public string PackResultAlreadyInstalled => Resources.PackResultAlreadyInstalled;

    public string PackResultUnresolved => Resources.PackResultUnresolved;

    public string PackResultFailed => Resources.PackResultFailed;

    public string PackResultNotAttempted => Resources.PackResultNotAttempted;

    public string ReleaseStable => Resources.ReleaseStable;

    public string ReleaseTesting => Resources.ReleaseTesting;

    public string ReleaseDev => Resources.ReleaseDev;

    public string ReleaseUnknown => Resources.ReleaseUnknown;

    public string ReleaseChannelStable => Resources.ReleaseChannelStable;

    public string ReleaseChannelTesting => Resources.ReleaseChannelTesting;

    public string ReleaseChannelDev => Resources.ReleaseChannelDev;

    public string SettingsGame => Resources.SettingsGame;

    public string SetupGameDirectory => Resources.SetupGameDirectory;

    public string SetupGameDirectoryHint => Resources.SetupGameDirectoryHint;

    public string SetupBannerNotSaved => Resources.SetupBannerNotSaved;

    public string SetupBannerFolderMissing => Resources.SetupBannerFolderMissing;

    public string SetupBannerAction => Resources.SetupBannerAction;

    public string SharedProfileCreateInstance => Resources.SharedProfileCreateInstance;

    public string SharedProfileDismiss => Resources.SharedProfileDismiss;

    public string SharedProfileModalTitle => Resources.SharedProfileModalTitle;

    public string SharedProfileModalCopies => Resources.SharedProfileModalCopies;

    public string SharedProfileModalUnchanged => Resources.SharedProfileModalUnchanged;

    public string SharedProfileImporting => Resources.SharedProfileImporting;

    public string SharedProfileInstanceName => Resources.SharedProfileInstanceName;

    public string SetupBrowse => Resources.SetupBrowse;

    public string SetupSave => Resources.SetupSave;

    public string SetupUseThisFolder => Resources.SetupUseThisFolder;

    public string SetupFoundGame => Resources.SetupFoundGame;

    public string SetupFoundGames => Resources.SetupFoundGames;

    public string FormatSetupFoundGame(string version, string directory)
        => string.Format(CultureInfo.CurrentCulture, Resources.SetupFoundGameFormat, version, directory);

    public string SetupUseFoundGame => Resources.SetupUseFoundGame;

    public string SetupUseSelectedGame => Resources.SetupUseSelectedGame;

    public string SetupLater => Resources.SetupLater;

    public string SetupLoader => Resources.SetupLoader;

    public string SetupLoaderDirectory => Resources.SetupLoaderDirectory;

    public string SetupLoaderDirectoryHint => Resources.SetupLoaderDirectoryHint;

    public string SetupInstallLoader => Resources.SetupInstallLoader;

    public string SetupUseExisting => Resources.SetupUseExisting;

    public string SetupFoundLoader => Resources.SetupFoundLoader;

    public string SetupSaved => Resources.SetupSaved;

    public string SetupDirectoryMissing => Resources.SetupDirectoryMissing;

    public string SetupNoLoaderSelected => Resources.SetupNoLoaderSelected;

    public string SetupReinstallLoader => Resources.SetupReinstallLoader;

    public string SetupLoaderInstalledUnknownVersion => Resources.SetupLoaderInstalledUnknownVersion;

    public string LaunchWithoutModLoader => Resources.LaunchWithoutModLoader;

    public string LaunchActiveInstance => Resources.LaunchActiveInstance;

    public string LaunchInstanceMissing => Resources.LaunchInstanceMissing;

    public string FormatLaunchLoaderNotInstalled(string loader)
        => string.Format(CultureInfo.CurrentCulture, Resources.LaunchLoaderNotInstalledFormat, loader);

    public string FormatLaunchNeededLoaderNotInstalled(string loader)
        => string.Format(CultureInfo.CurrentCulture, Resources.LaunchNeededLoaderNotInstalledFormat, loader);

    public string FormatLaunchInstallLoader(string loader)
        => string.Format(CultureInfo.CurrentCulture, Resources.LaunchInstallLoaderFormat, loader);

    public string FormatLaunchSetUpGameFirst(string loader)
        => string.Format(CultureInfo.CurrentCulture, Resources.LaunchSetUpGameFirstFormat, loader);

    public string FormatLaunchDifferentLoadersNeeded(string loaders)
        => string.Format(CultureInfo.CurrentCulture, Resources.LaunchDifferentLoadersNeededFormat, loaders);

    public string FormatLaunchLoaderNotListed(string loaders)
        => string.Format(CultureInfo.CurrentCulture, Resources.LaunchLoaderNotListedFormat, loaders);

    public string LaunchNoLoaderTakesInstance
        => string.Format(CultureInfo.CurrentCulture, Resources.LaunchNoLoaderTakesInstanceFormat, Resources.LaunchWithoutModLoader);

    public string LaunchWithoutModLoaderToolTip => Resources.LaunchWithoutModLoaderToolTip;

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

    public string FormatContentDownloadsExact(string count)
        => string.Format(CultureInfo.CurrentCulture, Resources.ContentDownloadsExactFormat, count);

    public string FormatAboutFolderMissing(string folder)
        => string.Format(CultureInfo.CurrentCulture, Resources.AboutFolderMissingFormat, folder);

    public string FormatAboutCannotOpen(string target)
        => string.Format(CultureInfo.CurrentCulture, Resources.AboutCannotOpenFormat, target);

    public string FormatDiscoverInstalledIn(string instance)
        => string.Format(CultureInfo.CurrentCulture, Resources.DiscoverInstalledInFormat, instance);

    public string FormatDiscoverAddTo(string instance)
        => string.Format(CultureInfo.CurrentCulture, Resources.DiscoverAddToFormat, instance);

    public string FormatContentReplaceVersion(string installedVersion)
        => string.Format(CultureInfo.CurrentCulture, Resources.ContentReplaceVersionFormat, installedVersion);

    public string FormatContentReplaceVersionIn(string installedVersion, string instance)
        => string.Format(CultureInfo.CurrentCulture, Resources.ContentReplaceVersionInFormat, installedVersion, instance);

    public string FormatContentReplaceVersionAnyway(string installedVersion)
        => string.Format(CultureInfo.CurrentCulture, Resources.ContentReplaceVersionAnywayFormat, installedVersion);

    public string FormatDiscoverAddingTo(string instance)
        => string.Format(CultureInfo.CurrentCulture, Resources.DiscoverAddingToFormat, instance);

    /// <summary>The text before and after the instance name of <see cref="FormatDiscoverAddingTo"/>, so a page can style the name.</summary>
    public (string Before, string After) SplitDiscoverAddingTo()
    {
        var format = Resources.DiscoverAddingToFormat;
        var parts = format.Split("{0}");
        return parts.Length == 2 ? (parts[0], parts[1]) : (format, "");
    }

    public string FormatDiscoverInstalledMod(string name)
        => string.Format(CultureInfo.CurrentCulture, Resources.DiscoverInstalledModFormat, name);

    public string FormatDiscoverIndexStale(string age)
        => string.Format(CultureInfo.CurrentCulture, Resources.DiscoverIndexStaleFormat, age);

    public string FormatListingDownloading(string size)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingDownloadingFormat, size);

    public string FormatListingReadFailed(string reason)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingReadFailedFormat, reason);

    public string FormatListingLoadFailed(string reason)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingLoadFailedFormat, reason);

    public string FormatListingArchiveRoot(string release, string folder)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingArchiveRootFormat, release, folder);

    public string FormatListingArchiveCodeMod(string assembly)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingArchiveCodeModFormat, assembly);

    public string FormatListingArchiveNoRoot(string release)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingArchiveNoRootFormat, release);

    public string FormatListingArchiveProblem(string reason)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingArchiveProblemFormat, reason);

    public string FormatListingDependencyPreserved(string ids)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingDependencyPreservedFormat, ids);

    public string FormatListingImageFacts(string width, string height, string size)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingImageFactsFormat, width, height, size);

    public string FormatListingSpaceDockNotNumber(string value)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingSpaceDockNotNumberFormat, value);

    public string FormatListingSaved(string fileName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingSavedFormat, fileName);

    public string FormatListingOpenFailed(string reason)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingOpenFailedFormat, reason);

    public string FormatListingPublishText(string login)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingPublishTextFormat, login);

    public string FormatListingProofOwner(string repository)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingProofOwnerFormat, repository);

    public string FormatListingProofTopic(string repository, string topic)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingProofTopicFormat, repository, topic);

    public string FormatListingProofMarker(string repository)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingProofMarkerFormat, repository);

    public string FormatListingFixTopic(string repository, string topic)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingFixTopicFormat, repository, topic);

    public string FormatListingFixMissing(string repository)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingFixMissingFormat, repository);

    public string FormatListingFixFork(string repository)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingFixForkFormat, repository);

    public string FormatListingFixRenamed(string repository, string newName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingFixRenamedFormat, repository, newName);

    public string FormatListingFixSpaceDockMod(string modId)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingFixSpaceDockModFormat, modId);

    public string FormatListingFixSpaceDockLink(string modId)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingFixSpaceDockLinkFormat, modId);

    public string FormatListingFixOtherFiles(string number)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingFixOtherFilesFormat, number);

    public string FormatListingAppNotOnFork(string repository)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingAppNotOnForkFormat, repository);

    public string FormatListingOpened(string number)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingOpenedFormat, number);

    public string FormatListingUpdated(string number)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingUpdatedFormat, number);

    public string FormatListingUnchanged(string number)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingUnchangedFormat, number);

    public string FormatListingErrorRateLimit(string time)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingErrorRateLimitFormat, time);

    public string FormatListingErrorNotFound(string what)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingErrorNotFoundFormat, what);

    public string FormatListingErrorRefused(string what, string detail)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingErrorRefusedFormat, what, detail);

    public string FormatListingErrorForbidden(string what)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingErrorForbiddenFormat, what);

    public string FormatListingErrorPullRequestNotOnFork(string number)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingErrorPullRequestNotOnForkFormat, number);

    public string FormatListingErrorUnexpected(string what)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingErrorUnexpectedFormat, what);

    public string FormatListingPullRequest(string number)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingPullRequestFormat, number);

    public string FormatIndexUnreachable(string reason)
        => string.Format(CultureInfo.CurrentCulture, Resources.IndexUnreachableFormat, reason);

    public string FormatAboutIndexUpdated(string age)
        => string.Format(CultureInfo.CurrentCulture, Resources.AboutIndexUpdatedFormat, age);

    /// <summary>"just now", "5 minutes ago", "2 days ago", "3 months ago", "2 years ago", with the count rounded down.</summary>
    public string FormatTimeAgo(TimeSpan age)
    {
        if (age.TotalMinutes < 1)
            return Resources.TimeJustNow;
        if (age.TotalHours < 1)
            return FormatCount((int)age.TotalMinutes, Resources.TimeMinuteAgo, Resources.TimeMinutesAgoFormat);
        if (age.TotalDays < 1)
            return FormatCount((int)age.TotalHours, Resources.TimeHourAgo, Resources.TimeHoursAgoFormat);
        if (age.TotalDays < DaysPerMonth)
            return FormatCount((int)age.TotalDays, Resources.TimeDayAgo, Resources.TimeDaysAgoFormat);
        if (age.TotalDays < DaysPerYear)
            return FormatCount((int)(age.TotalDays / DaysPerMonth), Resources.TimeMonthAgo, Resources.TimeMonthsAgoFormat);

        return FormatCount((int)(age.TotalDays / DaysPerYear), Resources.TimeYearAgo, Resources.TimeYearsAgoFormat);
    }

    /// <summary>"just now", "5m ago", "2d ago", "2w ago", "3mo ago", "2y ago", with the count rounded down.</summary>
    public string FormatTimeAgoShort(TimeSpan age)
    {
        if (age.TotalMinutes < 1)
            return Resources.TimeJustNow;
        if (age.TotalHours < 1)
            return FormatShortAge((int)age.TotalMinutes, Resources.TimeMinutesAgoShortFormat);
        if (age.TotalDays < 1)
            return FormatShortAge((int)age.TotalHours, Resources.TimeHoursAgoShortFormat);
        if (age.TotalDays < DaysPerWeek)
            return FormatShortAge((int)age.TotalDays, Resources.TimeDaysAgoShortFormat);
        if (age.TotalDays < DaysPerMonth)
            return FormatShortAge((int)(age.TotalDays / DaysPerWeek), Resources.TimeWeeksAgoShortFormat);
        if (age.TotalDays < DaysPerYear)
            return FormatShortAge((int)(age.TotalDays / DaysPerMonth), Resources.TimeMonthsAgoShortFormat);

        return FormatShortAge((int)(age.TotalDays / DaysPerYear), Resources.TimeYearsAgoShortFormat);
    }

    // a month is a twelfth of a year, so an age just short of a year never reads as twelve months
    private const double DaysPerYear = 365.25;

    private const double DaysPerMonth = DaysPerYear / 12;

    private const double DaysPerWeek = 7;

    private static string FormatCount(int count, string one, string format)
        => count == 1 ? one : string.Format(CultureInfo.CurrentCulture, format, count);

    private static string FormatShortAge(int count, string format)
        => string.Format(CultureInfo.CurrentCulture, format, count);

    public string FormatSharedProfileModCount(int count)
        => FormatCount(count, Resources.SharedProfileBannerOne, Resources.SharedProfileBannerFormat);

    public string FormatSharedProfileImportDisabled(IEnumerable<string> folderNames)
        => string.Format(CultureInfo.CurrentCulture, Resources.SharedProfileImportDisabledFormat, string.Join(", ", folderNames));

    public string FormatSharedProfileImportNotChecked(IEnumerable<string> folderNames)
        => string.Format(CultureInfo.CurrentCulture, Resources.SharedProfileImportNotCheckedFormat, string.Join(", ", folderNames));

    public string FormatModListCopyName(string instanceName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ModListCopyNameFormat, instanceName);

    public string FormatModListInstallCount(int count)
        => string.Format(CultureInfo.CurrentCulture, Resources.ModListInstallCountFormat, count);

    public string FormatModListNotCopied(IReadOnlyCollection<string> folderNames)
        => folderNames.Count switch
        {
            0 => string.Empty,
            1 => string.Format(CultureInfo.CurrentCulture, Resources.ModListNotCopiedFormat, folderNames.First()),
            _ => string.Format(CultureInfo.CurrentCulture, Resources.ModListNotCopiedManyFormat, string.Join(", ", folderNames)),
        };

    public string FormatModListUnknown(string modId, string version)
        => string.Format(CultureInfo.CurrentCulture, Resources.ModListUnknownFormat, modId, version);

    public string FormatModListUnreadable(string fileName, string reason)
        => string.Format(CultureInfo.CurrentCulture, Resources.ModListUnreadableFormat, fileName, reason);

    public string FormatModListNewerFormat(string fileName, int format)
        => string.Format(CultureInfo.CurrentCulture, Resources.ModListNewerFormatFormat, fileName, format);

    public string FormatModListExported(string instanceName, string fileName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ModListExportedFormat, instanceName, fileName);

    public string FormatModListCopied(string instanceName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ModListCopiedFormat, instanceName);

    public string FormatModListNotExported(string folders)
        => string.Format(CultureInfo.CurrentCulture, Resources.ModListNotExportedFormat, folders);

    public string FormatPackModCount(int count)
        => string.Format(CultureInfo.CurrentCulture, Resources.PackModCountFormat, count);

    public string FormatPackIncompatible(string gameMin)
        => string.Format(CultureInfo.CurrentCulture, Resources.PackIncompatibleFormat, gameMin);

    public string FormatPackMemberYanked(string modId, string version, string? reason)
    {
        var text = string.Format(CultureInfo.CurrentCulture, Resources.PackMemberYankedFormat, modId, version);
        return string.IsNullOrWhiteSpace(reason) ? text : $"{text} {reason}";
    }

    public string FormatPackIncomplete(int failed, int total)
        => string.Format(CultureInfo.CurrentCulture, Resources.PackIncompleteFormat, failed, total);

    public string FormatPackUntested(string gameMax)
        => string.Format(CultureInfo.CurrentCulture, Resources.PackUntestedFormat, gameMax);

    public string FormatPackSuperseded(string packId)
        => string.Format(CultureInfo.CurrentCulture, Resources.PackSupersededFormat, packId);

    public string FormatPackDisputed(string? reason)
        => string.IsNullOrWhiteSpace(reason) ? Resources.PackDisputed : $"{Resources.PackDisputed} {reason}";

    public string FormatPackIndexStatusUnknown(string? reason)
        => string.IsNullOrWhiteSpace(reason) ? Resources.PackIndexStatusUnknown : $"{Resources.PackIndexStatusUnknown} {reason}";

    public string FormatInstallDownloading(string content)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstallDownloadingFormat, content);

    public string FormatInstallPaused(string content)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstallPausedFormat, content);

    public string FormatInstallExtracting(string content)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstallExtractingFormat, content);

    public string FormatInstallConfiguring(string content)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstallConfiguringFormat, content);

    public string FormatInstallFinishing(string content)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstallFinishingFormat, content);

    public string FormatInstallStep(string status, int step, int stepCount)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstallStepFormat, status, step, stepCount);

    public string FormatInstallSize(string done, string total)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstallSizeFormat, done, total);

    public string FormatInstallSecondsLeft(int seconds)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstallSecondsLeftFormat, seconds);

    public string FormatInstallMinutesLeft(int minutes)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstallMinutesLeftFormat, minutes);

    public string InstallStop => Resources.InstallStop;

    public string InstallPause => Resources.InstallPause;

    public string InstallResume => Resources.InstallResume;

    public string InstallStopping => Resources.InstallStopping;

    public string InstallStoppingAfterMod => Resources.InstallStoppingAfterMod;

    public string InstallStopped => Resources.InstallStopped;

    public string FormatInstallRecoveryFolder(string folder)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstallRecoveryFolderFormat, folder);

    public string FormatInstallStoppedAfter(int completed, int total)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstallStoppedAfterFormat, completed, total);

    public string UpdateStopped => Resources.UpdateStopped;

    public string FormatUpdateStoppedAfter(int completed, int total)
        => string.Format(CultureInfo.CurrentCulture, Resources.UpdateStoppedAfterFormat, completed, total);

    public string InstallClosing => Resources.InstallClosing;

    public string CloseNowTitle => Resources.CloseNowTitle;

    public string CloseNowText => Resources.CloseNowText;

    public string CloseNowSavingHistory => Resources.CloseNowSavingHistory;

    public string CloseNowInstallWarning => Resources.CloseNowInstallWarning;

    public string CloseNowKeepWaiting => Resources.CloseNowKeepWaiting;

    public string CloseNowConfirm => Resources.CloseNowConfirm;

    public string LaunchShowDetails => Resources.LaunchShowDetails;

    public string LaunchOpenLog => Resources.LaunchOpenLog;

    public string LaunchNoOutput => Resources.LaunchNoOutput;

    public string FormatLaunchStarting(string loader)
        => string.Format(CultureInfo.CurrentCulture, Resources.LaunchStartingFormat, loader);

    public string FormatLaunchModBroke(string mod, string version, string loader)
        => string.Format(CultureInfo.CurrentCulture, Resources.LaunchModBrokeFormat, mod, version, loader);

    public string FormatLaunchModLikelyBroke(string mod, string version, string loader)
        => string.Format(CultureInfo.CurrentCulture, Resources.LaunchModLikelyBrokeFormat, mod, version, loader);

    public string FormatLaunchExitedEarly(string loader, int exitCode)
        => string.Format(CultureInfo.CurrentCulture, Resources.LaunchExitedEarlyFormat, loader, exitCode);

    public string FormatLaunchStoppedLoadingMods(string loader, int exitCode)
        => string.Format(CultureInfo.CurrentCulture, Resources.LaunchStoppedLoadingModsFormat, loader, exitCode);

    public string FormatLaunchExitCode(string exitCode)
        => string.Format(CultureInfo.CurrentCulture, Resources.LaunchExitCodeFormat, exitCode);

    public string FormatLaunchStoppedTitle(string loader)
        => string.Format(CultureInfo.CurrentCulture, Resources.LaunchStoppedTitleFormat, loader);

    public string FormatLaunchUnknownPlatformKey(string loader, string key)
        => string.Format(CultureInfo.CurrentCulture, Resources.LaunchUnknownPlatformKeyFormat, loader, key);

    public string FormatLaunchUnknownRuntime(string loader, string runtime)
        => string.Format(CultureInfo.CurrentCulture, Resources.LaunchUnknownRuntimeFormat, loader, runtime);

    public string FormatLaunchDotnetMissing(string loader)
        => string.Format(CultureInfo.CurrentCulture, Resources.LaunchDotnetMissingFormat, loader);

    public string FormatLaunchTargetMissing(string file, string loader)
        => string.Format(CultureInfo.CurrentCulture, Resources.LaunchTargetMissingFormat, file, loader);

    public string FormatLaunchDisableMod(string mod)
        => string.Format(CultureInfo.CurrentCulture, Resources.LaunchDisableModFormat, mod);

    public string FormatLaunchModDisabled(string mod)
        => string.Format(CultureInfo.CurrentCulture, Resources.LaunchModDisabledFormat, mod);

    public string FormatLaunchArgumentsHandoverFlag(string loader, string flag)
        => string.Format(CultureInfo.CurrentCulture, Resources.LaunchArgumentsHandoverFlagFormat, loader, flag);

    /// <summary>"Published 3 days ago", from an age that <see cref="FormatTimeAgo"/> wrote.</summary>
    public string FormatContentPublished(string age)
        => string.Format(CultureInfo.CurrentCulture, Resources.ContentPublishedFormat, age);

    public string FormatInstanceGroupModpack(string name, string version)
        => string.Format(CultureInfo.CurrentCulture, Resources.InstanceGroupModpackFormat, name, version);

    public string FormatGameSaveUpdated(string time)
        => string.Format(CultureInfo.CurrentCulture, Resources.GameSaveUpdatedFormat, time);

    public string FormatGameSaveOlderBuild(string build, string installedBuild)
        => string.Format(CultureInfo.CurrentCulture, Resources.GameSaveOlderBuildFormat, build, installedBuild);

    public string FormatGameSaveBackedUp(string name, string path)
        => string.Format(CultureInfo.CurrentCulture, Resources.GameSaveBackedUpFormat, name, path);

    public string FormatGameSaveReplace(string name, string instance)
        => string.Format(CultureInfo.CurrentCulture, Resources.GameSaveReplaceFormat, name, instance);

    public string FormatGameSaveCopied(string name, string instance)
        => string.Format(CultureInfo.CurrentCulture, Resources.GameSaveCopiedFormat, name, instance);

    public string FormatGameSaveDeleted(string name)
        => string.Format(CultureInfo.CurrentCulture, Resources.GameSaveDeletedFormat, name);

    public string FormatGameSavesCopiedFromProfile(int count)
        => string.Format(CultureInfo.CurrentCulture, Resources.GameSavesCopiedFromProfileFormat, count);

    public string FormatGameSavesBackedUp(int count, string folder)
        => string.Format(CultureInfo.CurrentCulture, Resources.GameSavesBackedUpFormat, count, folder);

    public string FormatBackupBackedUp(string time)
        => string.Format(CultureInfo.CurrentCulture, Resources.BackupBackedUpFormat, time);

    public string FormatBackupDeleted(string time)
        => string.Format(CultureInfo.CurrentCulture, Resources.BackupDeletedFormat, time);

    public string FormatBackupReplaced(string time)
        => string.Format(CultureInfo.CurrentCulture, Resources.BackupReplacedFormat, time);

    public string FormatBackupMoved(string time)
        => string.Format(CultureInfo.CurrentCulture, Resources.BackupMovedFormat, time);

    public string FormatContentRemoveNotOwned(string modId)
        => string.Format(CultureInfo.CurrentCulture, Resources.ContentRemoveNotOwnedFormat, modId);

    public string FormatContentRemoveRequired(string modId, string dependents)
        => string.Format(CultureInfo.CurrentCulture, Resources.ContentRemoveRequiredFormat, modId, dependents);

    public string FormatContentManageConfirm(string name, string version)
        => string.Format(CultureInfo.CurrentCulture, Resources.ContentManageConfirmFormat, name, version);

    public string FormatManualInstallsReplaceWarning(string folderName)
        => string.Format(CultureInfo.CurrentCulture, Resources.ManualInstallsReplaceWarningFormat, folderName);

    public string FormatContentUpdateTo(string version)
        => string.Format(CultureInfo.CurrentCulture, Resources.ContentUpdateToFormat, version);

    public string FormatListingStepFix(int count)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingStepFixFormat, count);

    public string FormatListingStillMissing(string names)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingStillMissingFormat, names);

    public string FormatListingDependencyNumber(int number)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingDependencyNumberFormat, number);

    public string FormatListingDescriptionImageNumber(int number)
        => string.Format(CultureInfo.CurrentCulture, Resources.ListingDescriptionImageNumberFormat, number);

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

    public string UnexpectedErrorHint => Resources.UnexpectedErrorHint;

    public string UnexpectedErrorCopyDetails => Resources.UnexpectedErrorCopyDetails;

    public string UnexpectedErrorReport => Resources.UnexpectedErrorReport;

    public string UnexpectedErrorCopied => Resources.UnexpectedErrorCopied;

    public string UnexpectedErrorCopyFailed => Resources.UnexpectedErrorCopyFailed;

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
