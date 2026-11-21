using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Borea.Core.Screenshots;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Borea.App.ViewModels;

/// <summary>
/// The screenshots of an instance: the menu item that opens their folder, and the delete modal that copies them to the Pictures folder first.
/// </summary>
public partial class MainViewModel
{
    private const string KeptScreenshotsFolderName = "Kitten Space Agency";

    private IReadOnlyList<Screenshot> _deleteModalScreenshots = [];

    /// <summary>The read of the screenshots that the delete modal started, which a delete waits for so that it never skips the copy.</summary>
    private Task _deleteModalScreenshotsLoad = Task.CompletedTask;

    /// <summary>The Pictures folder of the user. Tests replace it.</summary>
    internal Func<string> PicturesFolder { get; set; } = DefaultPicturesFolder;

    /// <summary>Whether the delete modal copies the screenshots first. It is on, because a delete cannot be undone.</summary>
    [ObservableProperty]
    private bool _keepScreenshotsOnDelete = true;

    /// <summary>Null while the modal is closed, while the screenshots are read, and when the instance has none.</summary>
    public string? DeleteModalScreenshotsText => DeletingInstance is not null && _deleteModalScreenshots.Count > 0
        ? Localization.FormatModalDeleteInstanceScreenshots(_deleteModalScreenshots.Count)
        : null;

    public string? DeleteModalKeepScreenshotsTip => DeletingInstance is { } item && _deleteModalScreenshots.Count > 0
        ? Localization.FormatModalDeleteInstanceKeepScreenshotsTip(KeptScreenshotsFolder(item.Name))
        : null;

    /// <summary>Opens the screenshots folder of the instance, and creates it when it does not exist yet.</summary>
    internal void OpenInstanceScreenshotsFolder(Guid instanceId)
    {
        if (_services is not { } services)
            return;

        OpenCreatingFolder(() => services.Paths.GetInstanceScreenshotsFolder(instanceId), () => Localization.GameDataNameScreenshots);
    }

    internal Task WhenDeleteModalScreenshotsLoadedAsync() => _deleteModalScreenshotsLoad;

    partial void OnDeletingInstanceChanging(InstanceItem? value) => _deleteModalScreenshots = [];

    /// <summary>Starts after the modal opened, because a read that completes at once checks that the modal still shows the instance.</summary>
    private void StartDeleteModalScreenshotsLoad(InstanceItem item)
    {
        KeepScreenshotsOnDelete = true;
        _deleteModalScreenshotsLoad = LoadDeleteModalScreenshotsAsync(item);
    }

    private async Task LoadDeleteModalScreenshotsAsync(InstanceItem item)
    {
        if (_services is not { } services)
            return;

        IReadOnlyList<Screenshot> screenshots;
        try
        {
            screenshots = await services.Screenshots.ListAsync(services.Paths.GetInstanceScreenshotsFolder(item.InstanceId));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (DeletingInstance != item)
            return;

        _deleteModalScreenshots = screenshots;
        OnPropertyChanged(nameof(DeleteModalScreenshotsText));
        OnPropertyChanged(nameof(DeleteModalKeepScreenshotsTip));
    }

    /// <summary>Returns the copy, or null after it showed why the copy failed, so the caller does not delete.</summary>
    private async Task<ScreenshotCopyResult?> TryKeepScreenshotsAsync(IScreenshotStore store, string instanceName, IReadOnlyList<Screenshot> screenshots)
    {
        try
        {
            return await store.CopyAsync(screenshots, KeptScreenshotsFolder(instanceName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowErrorToast(() => Localization.FormatToastScreenshotsKeepFailed(instanceName), exception.Message);
            return null;
        }
    }

    private void ShowScreenshotsKeptToast(ScreenshotCopyResult kept)
        => Toasts.ShowMessage(
            ToastKind.Success,
            () => Localization.FormatToastScreenshotsKept(kept.Copied, PathName(kept.Folder)),
            action: new ToastAction(() => Localization.LibraryOpenFolder, () => ShowOpenError(() => PathName(kept.Folder), TryOpenWithSystem(kept.Folder))));

    private string KeptScreenshotsFolder(string instanceName)
    {
        var name = WithoutInvalidFileNameChars(instanceName).Trim(' ', '.');
        return Path.Combine(PicturesFolder(), KeptScreenshotsFolderName, name.Length > 0 ? name : "Instance");
    }

    private static string DefaultPicturesFolder()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        return pictures.Length > 0 ? pictures : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
    }
}
