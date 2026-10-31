using Avalonia.Controls;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views.Pages;
using Borea.Core.Mods;

namespace Borea.App.Tests.Views;

/// <summary>The install and uninstall steps as the pages draw them.</summary>
[Collection(HeadlessCollection.Name)]
public sealed class InstallStepsViewTests
{
    [Fact]
    public async Task ContentPage_TheDescriptionTabShowsTheSteps_AndAListingWithoutThemShowsNone()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => InstallStepsTests.WithInstall(json, "MeasureTools", [InstallStepsTests.QemuStep], [InstallStepsTests.UninstallStep]));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        await InstallStepsTests.OpenAsync(viewModel, "MeasureTools");
        var withSteps = await ShownTextsAsync(harness, () => new Borea.App.Views.Pages.ContentPage());
        await InstallStepsTests.OpenAsync(viewModel, "AdvancedFlightComputer");
        var without = await ShownTextsAsync(harness, () => new Borea.App.Views.Pages.ContentPage());

        Assert.Contains(harness.Localization.ContentInstallSteps, withSteps);
        Assert.Contains(InstallStepsTests.QemuStep, withSteps);
        Assert.Contains(harness.Localization.ContentUninstallSteps, withSteps);
        Assert.Contains(InstallStepsTests.UninstallStep, withSteps);
        Assert.Contains("1.", withSteps);
        Assert.DoesNotContain(harness.Localization.ContentInstallSteps, without);
        Assert.DoesNotContain(harness.Localization.ContentUninstallSteps, without);
    }

    [Fact]
    public async Task ContentPage_TheInstallConfirmationAndTheNoticeAfterTheInstallShowTheSteps()
    {
        using var harness = await InstallStepsTests.CreateWithGameAsync(json => InstallStepsTests.WithInstall(json, "MeasureTools", [InstallStepsTests.QemuStep]));
        await InstallStepsTests.ActivateInstanceAsync(harness);
        var viewModel = harness.ViewModel;
        await InstallStepsTests.OpenAsync(viewModel, "MeasureTools");
        var item = viewModel.SelectedContent!;
        var heading = harness.Localization.FormatInstallStepsFor(item.Name);

        await item.InstallCommand.ExecuteAsync(null);
        var confirming = await ShownTextsAsync(harness, () => new Borea.App.Views.Pages.ContentPage());
        await item.ConfirmInstallCommand.ExecuteAsync(null);
        var installed = await ShownTextsAsync(harness, () => new Borea.App.Views.Pages.ContentPage());
        var dismiss = await ShownButtonsAsync(harness, () => new Borea.App.Views.Pages.ContentPage());

        Assert.Contains(heading, confirming);
        Assert.Contains(InstallStepsTests.QemuStep, confirming);
        Assert.Contains(heading, installed);
        Assert.Contains(harness.Localization.InstallStepsDismiss, dismiss);
    }

    [Fact]
    public async Task DiscoverRow_TheRemoveConfirmationShowsTheUninstallSteps()
    {
        using var harness = await InstalledWithUninstallStepAsync();
        var viewModel = harness.ViewModel;
        var item = viewModel.DiscoverItems.Single(row => row.ModId == "AdvancedFlightComputer");

        var before = await ShownTextsAsync(harness, () => new DiscoverPage());
        item.BeginRemoveCommand.Execute(null);
        var confirming = await ShownTextsAsync(harness, () => new DiscoverPage());

        Assert.DoesNotContain(InstallStepsTests.UninstallStep, before);
        Assert.Contains(harness.Localization.ContentRemoveSteps, confirming);
        Assert.Contains(InstallStepsTests.UninstallStep, confirming);
    }

    [Fact]
    public async Task ContentPage_TheRemoveConfirmationShowsTheUninstallSteps()
    {
        using var harness = await InstalledWithUninstallStepAsync();
        var viewModel = harness.ViewModel;
        await InstallStepsTests.OpenAsync(viewModel, "AdvancedFlightComputer");

        var before = await ShownTextsAsync(harness, () => new Borea.App.Views.Pages.ContentPage());
        viewModel.SelectedContent!.BeginRemoveCommand.Execute(null);
        var confirming = await ShownTextsAsync(harness, () => new Borea.App.Views.Pages.ContentPage());

        // the Description tab lists the step once, and the confirmation lists it again
        Assert.DoesNotContain(harness.Localization.ContentRemoveSteps, before);
        Assert.Single(before, text => text == InstallStepsTests.UninstallStep);
        Assert.Contains(harness.Localization.ContentRemoveSteps, confirming);
        Assert.Equal(2, confirming.Count(text => text == InstallStepsTests.UninstallStep));
    }

    [Fact]
    public async Task InstancePage_TheRemoveConfirmationOfARowShowsTheUninstallSteps()
    {
        using var harness = await InstalledWithUninstallStepAsync();
        var viewModel = harness.ViewModel;
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.SelectMany(group => group.Items).Single(item => item.ModId == "AdvancedFlightComputer");

        var before = await ShownTextsAsync(harness, () => new InstancePage());
        row.BeginRemoveCommand.Execute(null);
        var confirming = await ShownTextsAsync(harness, () => new InstancePage());

        Assert.DoesNotContain(InstallStepsTests.UninstallStep, before);
        Assert.Contains(harness.Localization.ContentRemoveSteps, confirming);
        Assert.Contains(InstallStepsTests.UninstallStep, confirming);
    }

    [Fact]
    public async Task PackPage_TheInstallConfirmationAndTheNoticeAfterTheInstallShowTheMemberSteps()
    {
        using var harness = await InstallStepsTests.CreateWithGameAsync(json => InstallStepsTests.WithInstall(InstallStepsTests.WithPacks(json), "MeasureTools", [InstallStepsTests.QemuStep]));
        await InstallStepsTests.ActivateInstanceAsync(harness);
        var viewModel = harness.ViewModel;
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = viewModel.DiscoverPacks.Single(item => item.PackId == "tools-pack");
        await pack.OpenCommand.ExecuteAsync(null);
        var heading = harness.Localization.FormatInstallStepsFor("MeasureTools");

        await pack.InstallCommand.ExecuteAsync(null);
        var confirming = await ShownTextsAsync(harness, () => new PackPage());
        var confirmButtons = await ShownButtonsAsync(harness, () => new PackPage());
        var confirmText = pack.ConfirmInstallText;
        await pack.ConfirmInstallCommand.ExecuteAsync(null);
        var installed = await ShownTextsAsync(harness, () => new PackPage());
        var dismiss = await ShownButtonsAsync(harness, () => new PackPage());

        Assert.Contains(heading, confirming);
        Assert.Contains(InstallStepsTests.QemuStep, confirming);
        Assert.Contains(confirmText, confirmButtons);
        Assert.Contains(heading, installed);
        Assert.Contains(InstallStepsTests.QemuStep, installed);
        Assert.Contains(harness.Localization.InstallStepsDismiss, dismiss);
    }

    [Fact]
    public async Task ModpacksRow_TheInstallConfirmationAndTheNoticeAfterTheInstallShowTheMemberSteps()
    {
        using var harness = await InstallStepsTests.CreateWithGameAsync(json => InstallStepsTests.WithInstall(InstallStepsTests.WithPacks(json), "MeasureTools", [InstallStepsTests.QemuStep]));
        await InstallStepsTests.ActivateInstanceAsync(harness);
        var viewModel = harness.ViewModel;
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = viewModel.DiscoverPacks.Single(item => item.PackId == "tools-pack");
        var heading = harness.Localization.FormatInstallStepsFor("MeasureTools");

        await pack.InstallCommand.ExecuteAsync(null);
        var confirming = await ShownTextsAsync(harness, () => new DiscoverPage());
        await pack.ConfirmInstallCommand.ExecuteAsync(null);
        var installed = await ShownTextsAsync(harness, () => new DiscoverPage());
        var dismiss = await ShownButtonsAsync(harness, () => new DiscoverPage());

        Assert.Contains(heading, confirming);
        Assert.Contains(InstallStepsTests.QemuStep, confirming);
        Assert.Contains(heading, installed);
        Assert.Contains(harness.Localization.InstallStepsDismiss, dismiss);
    }

    /// <summary>An active instance that holds AdvancedFlightComputer from Borea, whose listing has one uninstall step, with Discover loaded.</summary>
    private static async Task<ViewModelHarness> InstalledWithUninstallStepAsync()
    {
        var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => InstallStepsTests.WithInstall(json, "AdvancedFlightComputer", null, [InstallStepsTests.UninstallStep]));
        try
        {
            await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea);
            await harness.ViewModel.LoadAsync();
            await harness.ViewModel.EnsureDiscoverLoadedAsync();
            return harness;
        }
        catch
        {
            harness.Dispose();
            throw;
        }
    }

    private static Task<List<string>> ShownTextsAsync(ViewModelHarness harness, Func<Control> page) =>
        RenderAsync(harness, page, root => root.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.IsEffectivelyVisible && block.Text is not null)
            .Select(block => block.Text!)
            .ToList());

    private static Task<List<string>> ShownButtonsAsync(ViewModelHarness harness, Func<Control> page) =>
        RenderAsync(harness, page, root => root.GetVisualDescendants().OfType<Button>()
            .Where(button => button.IsEffectivelyVisible && button.Content is string)
            .Select(button => (string)button.Content!)
            .ToList());

    private static Task<List<string>> RenderAsync(ViewModelHarness harness, Func<Control> create, Func<Control, List<string>> read) =>
        HeadlessApp.RunAsync(harness, () =>
        {
            var page = create();
            var window = new Window { Width = 1280, Height = 900, DataContext = harness.ViewModel, Content = page };
            window.Show();
            try
            {
                page.UpdateLayout();
                return Task.FromResult(read(page));
            }
            finally
            {
                window.Close();
            }
        });
}
