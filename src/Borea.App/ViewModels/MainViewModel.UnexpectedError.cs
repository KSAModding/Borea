using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The error bar at the bottom of the window, and the two actions that turn an
/// exception no page expected into a bug report.
/// </summary>
public partial class MainViewModel
{
    /// <summary>How much of the message the body of the report carries, so the link stays short enough for GitHub.</summary>
    private const int ReportedMessageLength = 300;

    /// <summary>How much of the message the title of the report carries, which GitHub cuts at 256 characters.</summary>
    private const int ReportedTitleLength = 150;

    private const string ReportedTitlePrefix = "Unexpected error: ";

    private string? _unexpectedErrorDetails;

    /// <summary>Shows an exception in the error bar, with its type in front of its message.</summary>
    internal void ShowUnexpectedError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _unexpectedErrorDetails = exception.ToString();
        UnexpectedError = $"{exception.GetType().Name}: {exception.Message}";
    }

    // the bar holds one error at a time, so dismissing it drops the details behind it as well
    partial void OnUnexpectedErrorChanged(string? value)
    {
        if (value is null)
            _unexpectedErrorDetails = null;
    }

    /// <summary>
    /// What the About page copies, followed by the exception and its stack.
    /// </summary>
    internal string UnexpectedErrorDetailsText()
        => new StringBuilder(DiagnosticsWithLogText())
            .AppendLine()
            .AppendLine()
            .AppendLine("The error:")
            .Append(_unexpectedErrorDetails ?? UnexpectedError)
            .ToString();

    [RelayCommand]
    private async Task CopyUnexpectedErrorAsync()
    {
        if (UnexpectedError is null || WindowServices is not { } window)
            return;

        try
        {
            await window.CopyTextAsync(UnexpectedErrorDetailsText());
            ShowSuccessToast(() => Localization.UnexpectedErrorCopied);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ShowErrorToast(() => Localization.UnexpectedErrorCopyFailed, exception.Message);
        }
    }

    [RelayCommand]
    private void ReportUnexpectedError()
    {
        if (UnexpectedError is not { } message)
            return;

        ShowOpenError(() => ReportBugUrl, TryOpenWithSystem(UnexpectedErrorIssueUrl(message)));
    }

    /// <summary>
    /// The new issue form of the Borea repository, with what Borea knows about
    /// the error already filled in and room for the copied details.
    /// </summary>
    internal static string UnexpectedErrorIssueUrl(string message)
    {
        var body = new StringBuilder()
            .AppendLine("### What failed")
            .AppendLine()
            .AppendLine(Cut(message, ReportedMessageLength))
            .AppendLine()
            .AppendLine("### Borea")
            .AppendLine()
            .AppendLine("```")
            .AppendLine($"Borea {BoreaInformationalVersion}")
            .AppendLine(SystemText)
            .AppendLine("```")
            .AppendLine()
            .AppendLine("### What I did before it")
            .AppendLine()
            .AppendLine()
            .AppendLine("### Details")
            .AppendLine()
            .Append("Paste here what \"Copy details\" in the error bar put on the clipboard.")
            .ToString();

        var title = ReportedTitlePrefix + Cut(message, ReportedTitleLength);
        return $"{ReportBugUrl}?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";
    }

    private static string Cut(string message, int length)
    {
        if (message.Length <= length)
            return message;

        // a cut that ends inside a surrogate pair leaves a half character behind, so it takes one less
        return char.IsHighSurrogate(message[length - 1]) ? message[..(length - 1)] : message[..length];
    }
}
