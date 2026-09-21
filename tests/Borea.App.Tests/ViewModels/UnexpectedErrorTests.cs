using System.ComponentModel;
using Borea.App.ViewModels;

namespace Borea.App.Tests.ViewModels;

public sealed class UnexpectedErrorTests
{
    [Fact]
    public async Task UnhandledException_ShowsTheTypeAndTheMessage()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        viewModel.ShowUnexpectedError(new InvalidOperationException("The instance is gone."));

        Assert.Equal("InvalidOperationException: The instance is gone.", viewModel.UnexpectedError);
    }

    [Fact]
    public async Task CopyDetails_PutsTheDiagnosticsAndTheExceptionOnTheClipboard()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var window = new FakeWindowServices();
        viewModel.WindowServices = window;
        viewModel.ShowUnexpectedError(Thrown());

        await viewModel.CopyUnexpectedErrorCommand.ExecuteAsync(null);

        Assert.NotNull(window.CopiedText);
        Assert.StartsWith(viewModel.DiagnosticsText, window.CopiedText);
        Assert.Contains("NullReferenceException: The vehicle has no engine.", window.CopiedText);
        Assert.Contains(nameof(Thrown), window.CopiedText);
        Assert.Equal(harness.Localization.UnexpectedErrorCopied, viewModel.Toasts.Items[^1].Message);
    }

    [Fact]
    public async Task CopyDetails_WhenTheClipboardFails_ShowsAnError()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.WindowServices = new FakeWindowServices { Failure = new InvalidOperationException("The clipboard is busy.") };
        viewModel.ShowUnexpectedError(new InvalidOperationException("The instance is gone."));

        await viewModel.CopyUnexpectedErrorCommand.ExecuteAsync(null);

        var toast = viewModel.Toasts.Items[^1];
        Assert.True(toast.IsFailed);
        Assert.Equal(harness.Localization.UnexpectedErrorCopyFailed, toast.Message);
    }

    [Fact]
    public async Task ReportThis_OpensTheNewIssueFormWithTheVersionAndTheSystem()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;
        viewModel.ShowUnexpectedError(new InvalidOperationException("The instance is gone."));

        viewModel.ReportUnexpectedErrorCommand.Execute(null);

        var url = Assert.Single(opened);
        Assert.StartsWith(MainViewModel.ReportBugUrl + "?title=", url);
        Assert.True(Uri.IsWellFormedUriString(url, UriKind.Absolute));
        var query = new Uri(url).Query;
        Assert.Contains(Uri.EscapeDataString("InvalidOperationException: The instance is gone."), query);
        Assert.Contains(Uri.EscapeDataString(MainViewModel.BoreaInformationalVersion), query);
        Assert.Contains(Uri.EscapeDataString(MainViewModel.SystemText), query);
    }

    [Fact]
    public void IssueUrl_OfAVeryLongMessage_StaysShort()
    {
        var url = MainViewModel.UnexpectedErrorIssueUrl(new string('a', 4000));

        Assert.True(url.Length < 2000, $"The report link is {url.Length} characters long.");
        var title = TitleOf(url);
        Assert.True(title.Length <= 256, $"The prefilled title is {title.Length} characters long.");
    }

    [Fact]
    public void IssueUrl_OfAMessageThatEndsInAnEmoji_KeepsTheTitleWhole()
    {
        var url = MainViewModel.UnexpectedErrorIssueUrl(new string('a', 149) + "\U0001F680" + new string('b', 200));

        var title = TitleOf(url);
        Assert.DoesNotContain('\uFFFD', title);
        Assert.False(char.IsHighSurrogate(title[^1]), "The title ends in half a character.");
    }

    [Fact]
    public async Task ReportThis_WhenNoBrowserOpens_ShowsAnError()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.OpenWithSystem = _ => throw new Win32Exception("No browser is set.");
        viewModel.ShowUnexpectedError(new InvalidOperationException("The instance is gone."));

        viewModel.ReportUnexpectedErrorCommand.Execute(null);

        var toast = viewModel.Toasts.Items[^1];
        Assert.True(toast.IsFailed);
        Assert.Equal(harness.Localization.FormatToastOpenFailed(MainViewModel.ReportBugUrl), toast.Message);
    }

    [Fact]
    public async Task ErrorBar_AfterItWasDismissed_ShowsTheNextError()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var window = new FakeWindowServices();
        viewModel.WindowServices = window;
        viewModel.ShowUnexpectedError(new InvalidOperationException("The first one."));

        viewModel.DismissUnexpectedErrorCommand.Execute(null);
        Assert.Null(viewModel.UnexpectedError);

        await viewModel.CopyUnexpectedErrorCommand.ExecuteAsync(null);
        Assert.Null(window.CopiedText);

        viewModel.ShowUnexpectedError(new InvalidOperationException("The second one."));
        Assert.Equal("InvalidOperationException: The second one.", viewModel.UnexpectedError);

        await viewModel.CopyUnexpectedErrorCommand.ExecuteAsync(null);
        Assert.Contains("The second one.", window.CopiedText);
        Assert.DoesNotContain("The first one.", window.CopiedText);
    }

    private static string TitleOf(string url)
    {
        var query = new Uri(url).Query;
        var title = query[(query.IndexOf("?title=", StringComparison.Ordinal) + "?title=".Length)..];
        return Uri.UnescapeDataString(title[..title.IndexOf("&body=", StringComparison.Ordinal)]);
    }

    private static Exception Thrown()
    {
        try
        {
            throw new NullReferenceException("The vehicle has no engine.");
        }
        catch (NullReferenceException exception)
        {
            return exception;
        }
    }

    private sealed class FakeWindowServices : IWindowServices
    {
        public string? CopiedText { get; private set; }

        public Exception? Failure { get; init; }

        public Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string fileTypeName, string text) => Task.FromResult<string?>(null);

        public Task<PickedTextFile?> OpenTextFileAsync(string title, string fileTypeName) => Task.FromResult<PickedTextFile?>(null);

        public Task<PickedBinaryFile?> OpenImageFileAsync(string title, string fileTypeName, long maxBytes) => Task.FromResult<PickedBinaryFile?>(null);

        public Task CopyTextAsync(string text)
        {
            if (Failure is not null)
                return Task.FromException(Failure);
            CopiedText = text;
            return Task.CompletedTask;
        }
    }
}
