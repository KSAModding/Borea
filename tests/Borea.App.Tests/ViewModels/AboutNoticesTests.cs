using System.ComponentModel;
using System.Xml.Linq;
using Borea.App.ViewModels;

namespace Borea.App.Tests.ViewModels;

public sealed class AboutNoticesTests
{
    private static string RepositoryRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public async Task NoticesPath_IsNextToTheRunningProgram()
    {
        using var harness = await ViewModelHarness.CreateAsync();

        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt"), harness.ViewModel.ThirdPartyNoticesPath);
    }

    [Fact]
    public async Task OpenNotices_LocalBuildWithoutTheFile_SaysSoInsteadOfStarting()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var opened = new List<string>();
        viewModel.NoticesDirectory = harness.Root;
        viewModel.OpenNoticesFile = opened.Add;
        viewModel.NoticesError = "old";

        viewModel.OpenThirdPartyNoticesCommand.Execute(null);

        Assert.Equal(harness.Localization.AboutNoticesMissing, viewModel.NoticesMessage);
        Assert.Null(viewModel.NoticesError);
        Assert.Empty(opened);
        Assert.Null(viewModel.AboutError);
    }

    [Fact]
    public async Task OpenNotices_FileNextToTheProgram_OpensIt()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var opened = new List<string>();
        var notices = Path.Combine(harness.Root, MainViewModel.ThirdPartyNoticesFileName);
        File.WriteAllText(notices, "Third-party notices");
        viewModel.NoticesDirectory = harness.Root;
        viewModel.OpenNoticesFile = opened.Add;
        viewModel.NoticesMessage = "old";
        viewModel.NoticesError = "old";

        viewModel.OpenThirdPartyNoticesCommand.Execute(null);

        Assert.Equal([notices], opened);
        Assert.Null(viewModel.NoticesMessage);
        Assert.Null(viewModel.NoticesError);
    }

    [Fact]
    public async Task OpenNotices_SystemCannotOpenTheFile_ReportsTheReasonAsAnError()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        File.WriteAllText(Path.Combine(harness.Root, MainViewModel.ThirdPartyNoticesFileName), "Third-party notices");
        viewModel.NoticesDirectory = harness.Root;
        viewModel.OpenNoticesFile = _ => throw new Win32Exception("No application is associated with the file.");

        viewModel.OpenThirdPartyNoticesCommand.Execute(null);

        Assert.Equal("No application is associated with the file.", viewModel.NoticesError);
        Assert.Null(viewModel.NoticesMessage);
    }

    [Fact]
    public async Task ShowAbout_ClearsTheNoticesLines()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.NoticesMessage = "old";
        viewModel.NoticesError = "old";

        viewModel.ShowAboutSettingsCommand.Execute(null);

        Assert.Null(viewModel.NoticesMessage);
        Assert.Null(viewModel.NoticesError);
    }

    [Theory]
    [InlineData("Resources.resx")]
    [InlineData("Resources.de.resx")]
    public void Hints_NameTheFileTheReleaseWorkflowWrites(string resourceFile)
    {
        var resources = XDocument.Load(Path.Combine(RepositoryRoot, "src", "Borea.App", "Localization", "Resources", resourceFile))
            .Root!
            .Elements("data")
            .ToDictionary(element => (string)element.Attribute("name")!, element => (string)element.Element("value")!);
        var workflow = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", "release.yml"));

        Assert.Contains(MainViewModel.ThirdPartyNoticesFileName, resources["AboutCreditsHint"]);
        Assert.Contains(MainViewModel.ThirdPartyNoticesFileName, resources["AboutNoticesMissing"]);
        Assert.Contains($"/{MainViewModel.ThirdPartyNoticesFileName}\"", workflow);
    }
}
