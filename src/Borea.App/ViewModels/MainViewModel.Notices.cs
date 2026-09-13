using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>The third-party notices button of the About section. Only release archives have the file.</summary>
public partial class MainViewModel
{
    public const string ThirdPartyNoticesFileName = "THIRD-PARTY-NOTICES.txt";

    /// <summary>The line under the notices button when this build has no notices file.</summary>
    [ObservableProperty]
    private string? _noticesMessage;

    /// <summary>The line under the notices button when the system cannot open the file.</summary>
    [ObservableProperty]
    private string? _noticesError;

    /// <summary>The folder of the running program.</summary>
    internal string NoticesDirectory { get; set; } = AppContext.BaseDirectory;

    /// <summary>Opens a file through the system. Tests replace it.</summary>
    internal Action<string> OpenNoticesFile { get; set; } = path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public string ThirdPartyNoticesPath => Path.Combine(NoticesDirectory, ThirdPartyNoticesFileName);

    [RelayCommand]
    private void OpenThirdPartyNotices()
    {
        NoticesMessage = null;
        NoticesError = null;

        var path = ThirdPartyNoticesPath;
        if (!File.Exists(path))
        {
            NoticesMessage = Localization.AboutNoticesMissing;
            return;
        }

        try
        {
            OpenNoticesFile(path);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            NoticesError = exception.Message;
        }
    }
}
