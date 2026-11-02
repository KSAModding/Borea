using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class ConflictModalTests
{
    [Fact]
    public async Task Modal_ShowsWhyAndBothMods_AndItsButtonRemovesTheOtherMod()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ConflictDialogTests.Declares("AdvancedFlightComputer", "KSArmory"));
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "KSArmory", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(row => row.ModId == "AdvancedFlightComputer").InstallCommand.ExecuteAsync(null);
        var conflict = Assert.Single(viewModel.ConflictDialog!.Rows);

        var texts = await HeadlessApp.RunAsync(harness, async () =>
        {
            var modal = new ConflictModal { DataContext = viewModel };
            var window = new Window { Width = 1280, Height = 480, Content = modal, DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var shown = modal.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
                var remove = modal.GetVisualDescendants().OfType<Button>().Single(button => button.Command == conflict.RemoveCommand && button.IsEffectivelyVisible);
                var point = remove.TranslatePoint(new Point(remove.Bounds.Width / 2, remove.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                await conflict.RemoveCommand.ExecutionTask!;
                return shown;
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains(harness.Localization.ConflictTitle, texts);
        Assert.Contains(harness.Localization.ConflictWhy, texts);
        Assert.Contains(harness.Localization.ConflictAgain, texts);
        Assert.Contains(conflict.Text, texts);
        Assert.Contains(conflict.RemoveText, texts);
        Assert.False(viewModel.IsConflictOpen);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
    }

    [Fact]
    public async Task Modal_IsDrawnOverThePlanOfAnImport()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ConflictDialogTests.Declares("MeasureTools", "KSArmory"));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var text = harness.Services.ModListFormat.Write(new ModList("Shared", [new ModListEntry("KSArmory", ModVersion.Parse("0.8.44"), enabled: true), new ModListEntry("MeasureTools", ModVersion.Parse("1.1.10"), enabled: true)]));
        await viewModel.BeginImportAsync("shared.toml", text);
        Assert.True(viewModel.IsReviewingModList);

        var top = await HeadlessApp.RunAsync(harness, () =>
        {
            var window = new MainWindow { DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var modal = window.GetVisualDescendants().OfType<Border>().Last(border => border.Classes.Contains("modal") && border.IsEffectivelyVisible);
                return Task.FromResult(modal.GetVisualAncestors().OfType<UserControl>().First().GetType());
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal(typeof(ConflictModal), top);
    }
}
