using System.Net;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.App.Tests.Views;

/// <summary>
/// The tabs of the instance page show their rows and their empty text in a
/// bordered table with a header, and the table keeps its action inside it at
/// every window width of the design.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class InstanceTablesTests
{
    private static Task<T> OnInstancePageAsync<T>(ViewModelHarness harness, double width, Func<Control, T> read) =>
        HeadlessApp.RunAsync(harness, () =>
        {
            var page = new InstancePage();
            var window = new Window { Width = width, Height = 900, DataContext = harness.ViewModel, Content = page };
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

    private static async Task<ViewModelHarness> EmptyInstanceAsync()
    {
        var harness = await ViewModelHarness.CreateAsync();
        await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.Instances.Single().OpenCommand.ExecuteAsync(null);
        return harness;
    }

    /// <summary>Two empty instances, Main and the active Second, on the page of the one named.</summary>
    private static async Task<ViewModelHarness> TwoEmptyInstancesAsync(string open)
    {
        var harness = await ViewModelHarness.CreateAsync();
        await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        var second = (await harness.Services.Instances.CreateAsync("Second", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(second.InstanceId);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.Instances.Single(instance => instance.Name == open).OpenCommand.ExecuteAsync(null);
        return harness;
    }

    /// <summary>The bordered table around the visible text, or null when the text stands on its own.</summary>
    private static Border? TableAround(Control page, string text)
        => page.GetVisualDescendants().OfType<TextBlock>()
            .Single(block => block.IsEffectivelyVisible && block.Text == text)
            .GetVisualAncestors().OfType<Border>()
            .FirstOrDefault(border => border.BorderThickness == new Thickness(1) && border.CornerRadius == new CornerRadius(16));

    private static List<string> TextsIn(Visual table)
        => table.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.IsEffectivelyVisible && block.Text is not null)
            .Select(block => block.Text!)
            .ToList();

    /// <summary>The visible button that shows the label, as its content or as the text inside it.</summary>
    private static Button ButtonIn(Visual table, string label)
        => table.GetVisualDescendants().OfType<Button>()
            .Single(button => button.IsEffectivelyVisible && (button.Content as string == label || button.Content is TextBlock { Text: var text } && text == label));

    private static bool Holds(Visual table, Visual inner)
    {
        var corner = inner.TranslatePoint(default, table)!.Value;
        return corner.X >= 0 && corner.X + inner.Bounds.Width <= table.Bounds.Width;
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task NoContent_ShowsTheEmptyTextInTheModsTableWithTheWayToDiscover(double width)
    {
        using var harness = await EmptyInstanceAsync();
        var viewModel = harness.ViewModel;
        var localization = harness.Localization;

        var (texts, command, fits) = await OnInstancePageAsync(harness, width, page =>
        {
            var table = TableAround(page, localization.InstanceEmptyContent)!;
            var discover = ButtonIn(table, localization.HomeDiscoverMods);
            return (TextsIn(table), discover.Command, Holds(table, discover));
        });

        Assert.Contains(localization.InstanceGroupMods, texts);
        Assert.Same(viewModel.SetMainWindowDiscoverCommand, command);
        Assert.True(fits);
    }

    [Fact]
    public async Task RecordedVehicle_TheUpdateLinkStandsInTheListingLine_AndAPlanShowsNoButtonWithoutARun()
    {
        using var harness = await UserContentViewModelTests.VehicleHarnessAsync();
        await UserContentViewModelTests.ActiveInstanceWithRoverAsync(harness);
        var viewModel = harness.ViewModel;
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = Assert.Single(viewModel.VehiclesSection.Items);

        var (inListingLine, iconButtons, indeterminate) = await OnInstancePageAsync(harness, 860, page =>
        {
            var link = page.GetVisualDescendants().OfType<Button>().Single(button => button.Command == row.UpdateCommand);
            var inLine = link.IsEffectivelyVisible && link.GetVisualAncestors().OfType<WrapPanel>().Any();
            row.IsInstalling = true;
            page.UpdateLayout();
            var icons = page.GetVisualDescendants().OfType<Button>().Count(button => button.IsEffectivelyVisible && button.DataContext == row && button.Classes.Contains("icon"));
            return (inLine, icons, page.GetVisualDescendants().OfType<ProgressBar>().Single(bar => bar.IsEffectivelyVisible && bar.DataContext == row).IsIndeterminate);
        });

        Assert.True(inListingLine);
        Assert.Equal(0, iconButtons);
        Assert.True(indeterminate);
    }

    [Fact]
    public async Task NoContent_OnAnInactiveInstance_OffersNoWayToDiscover()
    {
        using var harness = await TwoEmptyInstancesAsync(open: "Main");
        var localization = harness.Localization;
        Assert.False(harness.ViewModel.SelectedInstance!.IsActive);

        var buttons = await OnInstancePageAsync(harness, 1280, page =>
            TableAround(page, localization.InstanceEmptyContent)!.GetVisualDescendants().OfType<Button>().Count(button => button.IsEffectivelyVisible));

        Assert.Equal(0, buttons);
    }

    [Theory]
    [InlineData("Second", true)]
    [InlineData("Main", false)]
    public async Task NoVehiclesOrSaves_OnlyTheActiveInstanceOffersFind_AndEveryInstanceOffersOpenFolder(string open, bool offersFind)
    {
        using var harness = await TwoEmptyInstancesAsync(open);
        var viewModel = harness.ViewModel;

        var tables = await OnInstancePageAsync(harness, 860, page => new[] { viewModel.VehiclesSection, viewModel.SavesSection }.Select(section =>
        {
            var table = TableAround(page, section.EmptyText)!;
            var buttons = table.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).ToList();
            return (section, Commands: buttons.Select(button => button.Command).ToList(), Fits: buttons.All(button => Holds(table, button)));
        }).ToList());

        Assert.All(tables, table =>
        {
            Assert.Equal(offersFind, table.Commands.Contains(table.section.FindOnDiscoverCommand));
            Assert.Contains(table.section.OpenFolderCommand, table.Commands);
            Assert.True(table.Fits);
        });
    }

    private static async Task<ViewModelHarness> ManagedModAndFoldersAsync(params string[] folders)
    {
        var harness = await ViewModelHarness.CreateAsync();
        var instance = await InstalledContent.AddAsync(harness, "KSArmory", activate: true, ownership: ModInstallOwnership.Borea);
        foreach (var name in folders)
        {
            var folder = Directory.CreateDirectory(Path.Combine(harness.Services.Paths.GetInstanceModsFolder(instance.InstanceId), name)).FullName;
            File.WriteAllText(Path.Combine(folder, "mod.toml"), $"name = \"{name}\"");
        }

        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.Instances.Single().OpenCommand.ExecuteAsync(null);
        return harness;
    }

    private static List<(string? Header, ICommand? Command)> MenuOf(Control page, object row)
    {
        var button = page.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && button.DataContext == row && button.Flyout is MenuFlyout);
        button.Flyout!.ShowAt(button);
        page.UpdateLayout();
        var items = ((MenuFlyout)button.Flyout).Items.OfType<MenuItem>().Where(item => item.IsVisible).Select(item => (item.Header as string, item.Command)).ToList();
        button.Flyout.Hide();
        return items;
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task ForeignFolder_ShowsInATableOfItsOwnBelowTheManagedMods(double width)
    {
        using var harness = await ManagedModAndFoldersAsync("LocalOnly");
        var viewModel = harness.ViewModel;
        var localization = harness.Localization;
        Assert.True(viewModel.IsContentTab);
        var managed = viewModel.ContentGroups.SelectMany(group => group.Items).Single().Name;

        var (managedTexts, foreignTexts, below, info, infoFits, open, openFits) = await OnInstancePageAsync(harness, width, page =>
        {
            var managedTable = TableAround(page, managed)!;
            var foreignTable = TableAround(page, "LocalOnly")!;
            var info = foreignTable.GetVisualDescendants().OfType<InfoButton>().Single(button => button.IsEffectivelyVisible);
            var open = ButtonIn(foreignTable, localization.GameDataOpenFolder);
            var below = foreignTable.TranslatePoint(default, page)!.Value.Y >= managedTable.TranslatePoint(default, page)!.Value.Y + managedTable.Bounds.Height;
            return (TextsIn(managedTable), TextsIn(foreignTable), below, info.Text, Holds(foreignTable, info), open.Command, Holds(foreignTable, open));
        });

        Assert.DoesNotContain("LocalOnly", managedTexts);
        Assert.DoesNotContain(managed, foreignTexts);
        Assert.Contains(localization.InstanceGroupNotManaged, foreignTexts);
        Assert.Contains(localization.ManualInstallsNoUpdates, foreignTexts);
        Assert.True(below);
        Assert.Equal(localization.ManualInstallsInfo, info);
        Assert.True(infoFits);
        Assert.True(openFits);

        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;
        open!.Execute(null);

        Assert.Equal([harness.Services.Paths.GetInstanceModsFolder(viewModel.SelectedInstance!.InstanceId)], opened);
        Assert.Empty(viewModel.Toasts.Items);
    }

    [Fact]
    public async Task NoForeignFolder_ShowsTheEmptyTableWithItsHintAndOpenFolder()
    {
        using var harness = await ManagedModAndFoldersAsync();
        var localization = harness.Localization;

        var (texts, open) = await OnInstancePageAsync(harness, 1280, page => (TextsIn(page), ButtonIn(TableAround(page, localization.ManualInstallsEmptyHint)!, localization.GameDataOpenFolder).Command));

        Assert.Contains(harness.ViewModel.ContentGroups.SelectMany(group => group.Items).Single().Name, texts);
        Assert.Contains(localization.InstanceGroupNotManaged, texts);
        Assert.Contains(localization.ManualInstallsEmptyHint, texts);
        Assert.DoesNotContain(localization.ManualInstallsNoUpdates, texts);
        Assert.Same(harness.ViewModel.OpenInstanceModsFolderCommand, open);
    }

    [Fact]
    public async Task ForeignFolder_TheRowOffersEveryActionOfAFolderThatBoreaDidNotInstall()
    {
        using var harness = await ManagedModAndFoldersAsync("MeasureTools", "LocalOnly");
        var viewModel = harness.ViewModel;
        var localization = harness.Localization;
        var known = viewModel.ManualInstallItems.Single(item => item.FolderName == "MeasureTools");
        var local = viewModel.ManualInstallItems.Single(item => item.FolderName == "LocalOnly");

        var (menu, manage, localHasMenu) = await OnInstancePageAsync(harness, 1280, page =>
            (MenuOf(page, known), ButtonIn(TableAround(page, "MeasureTools")!, localization.ManualInstallsManage).Command,
                page.GetVisualDescendants().OfType<Button>().Any(button => button.IsEffectivelyVisible && button.DataContext == local && button.Flyout is not null)));

        Assert.Equal([(localization.ManualInstallsReplace, known.BeginReplaceCommand)], menu);
        Assert.Same(known.ManageCommand, manage);
        Assert.False(localHasMenu);

        await known.BeginReplaceCommand.ExecuteAsync(null);
        Assert.True(known.IsConfirmingReplace);
        var (replace, cancelReplace) = await OnInstancePageAsync(harness, 1280, page =>
        {
            var row = TableAround(page, "MeasureTools")!;
            return (ButtonIn(row, localization.ManualInstallsDeleteAndReplace).Command, ButtonIn(row, localization.LibraryCancel).Command);
        });

        Assert.Same(known.ConfirmReplaceCommand, replace);
        Assert.Same(known.CancelReplaceCommand, cancelReplace);

        await known.ConfirmReplaceCommand.ExecuteAsync(null);
        Assert.NotNull(known.InstallWarning);
        var (install, cancelInstall) = await OnInstancePageAsync(harness, 1280, page =>
        {
            var row = TableAround(page, "MeasureTools")!;
            return (ButtonIn(row, localization.InstallAnyway).Command, ButtonIn(row, localization.LibraryCancel).Command);
        });

        Assert.Same(known.ConfirmInstallCommand, install);
        Assert.Same(known.CancelInstallCommand, cancelInstall);
    }

    [Fact]
    public async Task ManagedMod_TheMenuOffersPinVersion_AndAPinnedRowShowsTheMarkAndOffersUnpin()
    {
        using var harness = await ManagedModAndFoldersAsync();
        var viewModel = harness.ViewModel;
        var localization = harness.Localization;
        var row = viewModel.ContentGroups.SelectMany(group => group.Items).Single();

        var (menu, marked, pinned, pinnedMenu, pinnedMarked) = await HeadlessApp.RunAsync(harness, async () =>
        {
            var page = new InstancePage();
            var window = new Window { Width = 1280, Height = 900, DataContext = viewModel, Content = page };
            window.Show();
            try
            {
                page.UpdateLayout();
                var before = (MenuOf(page, row), PinMarkShown(page, row));
                await row.PinCommand.ExecuteAsync(null);
                page.UpdateLayout();
                var pinned = viewModel.ContentGroups.SelectMany(group => group.Items).Single();
                return (before.Item1, before.Item2, pinned, MenuOf(page, pinned), PinMarkShown(page, pinned));
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains((localization.ContentPinVersion, row.PinCommand), menu);
        Assert.DoesNotContain(menu, item => item.Header == localization.ContentUnpin);
        Assert.False(marked);
        Assert.Contains((localization.ContentUnpin, pinned.UnpinCommand), pinnedMenu);
        Assert.DoesNotContain(pinnedMenu, item => item.Header == localization.ContentPinVersion);
        Assert.True(pinnedMarked);

        static bool PinMarkShown(Control page, ContentItem item)
            => page.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()
                .Any(path => path.IsEffectivelyVisible && path.DataContext == item && ToolTip.GetTip(path) as string == item.PinnedText && item.PinnedText is not null);
    }

    [Fact]
    public async Task PackMod_TheMenuOffersDetach_AndTheHeaderThenNamesTheDifference()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var pack = (await harness.Services.Instances.CreateAsync("Tools", new InstanceSource.FromModPack("tools-pack", ModVersion.Parse("1.0.0")))).Instance;
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true, reason: InstallReason.ModPack, ownership: ModInstallOwnership.Borea, into: pack);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.Instances.Single().OpenCommand.ExecuteAsync(null);
        var viewModel = harness.ViewModel;
        var localization = harness.Localization;
        var row = viewModel.ContentGroups.SelectMany(group => group.Items).Single();

        var (menu, before, detachedMenu, after) = await HeadlessApp.RunAsync(harness, async () =>
        {
            var page = new InstancePage();
            var window = new Window { Width = 1280, Height = 900, DataContext = viewModel, Content = page };
            window.Show();
            try
            {
                page.UpdateLayout();
                var shown = (MenuOf(page, row), TextsIn(page));
                await row.DetachCommand.ExecuteAsync(null);
                page.UpdateLayout();
                var detached = viewModel.ContentGroups.SelectMany(group => group.Items).Single();
                return (shown.Item1, shown.Item2, MenuOf(page, detached), TextsIn(page));
            }
            finally
            {
                window.Close();
            }
        });

        // the index does not list the pack, so the header names it by its id
        var difference = localization.FormatInstancePackDifference(1, "tools-pack", "1.0.0");
        Assert.Contains((localization.ContentDetachFromPack, row.DetachCommand), menu);
        Assert.DoesNotContain(menu, item => item.Header == localization.ContentAttachToPack);
        Assert.DoesNotContain(difference, before);
        Assert.DoesNotContain(detachedMenu, item => item.Header == localization.ContentDetachFromPack);
        Assert.Contains(detachedMenu, item => item.Header == localization.ContentAttachToPack);
        Assert.Contains(difference, after);
    }

    [Fact]
    public async Task ForeignFolder_WhileAnUpdateOfTheInstanceRuns_TheRowActionsAreOff()
    {
        const string archiveHost = "archives.test";
        using var download = new ManualResetEventSlim();
        var downloading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = await ViewModelHarness.CreateAsync(respond: request =>
        {
            if (request.RequestUri?.Host != archiveHost)
                return null;

            downloading.TrySetResult();
            download.Wait(TimeSpan.FromSeconds(30));
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });
        harness.SpaceDock.Releases.AddRange(new[] { "1.0.0", "1.1.0" }.Select(version => new ModVersionMetadata(
            specVersion: 1,
            modId: ViewModelHarness.FakeSpaceDock.OwnId,
            version: ModVersion.Parse(version),
            releaseStatus: ReleaseStatus.Stable,
            releaseDate: DateTimeOffset.UnixEpoch,
            gameMin: "2026.1.1.1",
            gameMinRevision: 1,
            download: new DownloadInfo($"https://{archiveHost}/{version}.zip", sha256: null, sizeBytes: null, contentType: "application/zip"),
            installSizeBytes: null,
            dependencies: [])));
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, ViewModelHarness.FakeSpaceDock.OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        foreach (var name in new[] { "AdvancedFlightComputer", "KSArmory", "MeasureTools" })
        {
            var folder = Directory.CreateDirectory(Path.Combine(harness.Services.Paths.GetInstanceModsFolder(instance.InstanceId), name)).FullName;
            File.WriteAllText(Path.Combine(folder, "mod.toml"), $"name = \"{name}\"");
        }

        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var rows = viewModel.ManualInstallItems.ToDictionary(item => item.FolderName);
        Assert.All(rows.Values, row => Assert.True(row.CanAct));
        rows["KSArmory"].IsConfirmingReplace = true;
        rows["AdvancedFlightComputer"].InstallWarning = "A warning";
        var content = viewModel.ContentGroups.Single().Items.Single();
        await content.UpdateCommand.ExecuteAsync(null);

        var update = content.ConfirmUpdateCommand.ExecuteAsync(null);
        (bool Menu, bool Replace, bool Install, bool Cancel) enabled;
        try
        {
            await downloading.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(viewModel.CanChangeContent);

            enabled = await HeadlessApp.RunAsync(() =>
            {
                var page = new InstancePage();
                var window = new Window { Width = 1280, Height = 900, DataContext = viewModel, Content = page };
                window.Show();
                try
                {
                    page.UpdateLayout();
                    var buttons = page.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).ToList();
                    return Task.FromResult((
                        buttons.Single(button => button.DataContext == rows["MeasureTools"] && button.Flyout is MenuFlyout).IsEffectivelyEnabled,
                        buttons.Single(button => button.Command == rows["KSArmory"].ConfirmReplaceCommand).IsEffectivelyEnabled,
                        buttons.Single(button => button.Command == rows["AdvancedFlightComputer"].ConfirmInstallCommand).IsEffectivelyEnabled,
                        buttons.Single(button => button.Command == rows["KSArmory"].CancelReplaceCommand).IsEffectivelyEnabled));
                }
                finally
                {
                    // the update ends off the headless thread, so no binding may still listen to its rows
                    window.Content = null;
                    window.DataContext = null;
                    window.Close();
                }
            });
        }
        finally
        {
            download.Set();
            await update;
        }

        Assert.False(enabled.Menu);
        Assert.False(enabled.Replace);
        Assert.False(enabled.Install);
        Assert.True(enabled.Cancel);
    }

    [Fact]
    public async Task TabBar_OffersContentGameDataAndLog()
    {
        using var harness = await EmptyInstanceAsync();
        var localization = harness.Localization;

        var tabs = await OnInstancePageAsync(harness, 1280, page =>
            page.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("tab") && button.IsEffectivelyVisible).Select(button => button.Content as string).ToList());

        Assert.Equal([localization.InstanceTabContent, localization.InstanceTabGameData, localization.InstanceTabLog], tabs);
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task NoLog_ShowsTheEmptyTextInATableWithThePathAndReload(double width)
    {
        using var harness = await EmptyInstanceAsync();
        var viewModel = harness.ViewModel;
        var localization = harness.Localization;
        await viewModel.ShowInstanceLogCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsGameLogMissing);

        var (texts, command, fits) = await OnInstancePageAsync(harness, width, page =>
        {
            var table = TableAround(page, localization.GameLogMissing)!;
            var reload = ButtonIn(table, localization.GameLogReload);
            return (TextsIn(table), reload.Command, Holds(table, reload));
        });

        Assert.Contains(viewModel.GameLogPathText!, texts);
        Assert.Same(viewModel.ReloadGameLogCommand, command);
        Assert.True(fits);
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task GameData_RowsShareOneTableWithAHeader(double width)
    {
        using var harness = await EmptyInstanceAsync();
        var viewModel = harness.ViewModel;
        var localization = harness.Localization;
        await viewModel.ShowInstanceGameDataCommand.ExecuteAsync(null);
        var names = viewModel.GameDataItems.Select(item => item.Name).ToList();
        Assert.NotEmpty(names);

        var (tables, texts, fits) = await OnInstancePageAsync(harness, width, page =>
        {
            var tables = names.Select(name => TableAround(page, name)).Distinct().ToList();
            var table = tables[0]!;
            var buttons = table.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).ToList();
            return (tables, TextsIn(table), buttons.Count == names.Count && buttons.All(button => Holds(table, button)));
        });

        Assert.NotNull(Assert.Single(tables));
        Assert.Contains(localization.InstanceTabGameData, texts);
        Assert.True(fits);
    }
}
