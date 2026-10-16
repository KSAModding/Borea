using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.App.Tests.Views;

/// <summary>
/// The rows of the instance tabs share their columns, so each fact starts at the
/// same place in every row and the actions end at the same place, and nothing of
/// a row is cut at the smallest window.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class InstanceColumnsViewTests
{
    private const string LongName = "KSArmory Extended Arsenal for Orbital Science Missions";

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    public async Task ContentTab_LinesUpTheColumnsOfEveryModRowAndEverySaveRow(double windowWidth)
    {
        using var harness = await ContentAsync();
        var items = harness.ViewModel.ContentGroups.SelectMany(group => group.Items).ToList();
        var saves = harness.ViewModel.VehiclesSection.Items.Concat(harness.ViewModel.SavesSection.Items).ToList();
        // a detached row with an update, a pinned row with a long name and a dependency, so the rows have different cells
        Assert.NotNull(items.Single(item => item.ModId == "AdvancedFlightComputer").DetachedText);
        Assert.True(items.Single(item => item.ModId == "AdvancedFlightComputer").HasUpdate);
        Assert.True(items.Single(item => item.ModId == "KSArmory").IsPinned);
        Assert.True(items.Single(item => item.ModId == "MeasureTools").IsDependency);
        Assert.All(items, item => Assert.NotNull(item.SizeText));
        Assert.Contains(saves, item => item.IsOlderBuild);
        Assert.Contains(saves, item => !item.IsOlderBuild);

        var problems = await RenderAsync(harness, windowWidth, page => ContentColumns(page, harness, items.Count, saves.Count));

        Assert.Empty(problems);
    }

    /// <summary>
    /// A row that works or asks for a confirmation keeps the columns of the other rows, and no row loses its name or
    /// has a part outside it at the smallest window, because the confirmation opens below the row.
    /// </summary>
    [Theory]
    [InlineData("en", RowState.Installing)]
    [InlineData("de", RowState.Installing)]
    [InlineData("en", RowState.ConfirmingRemove)]
    [InlineData("de", RowState.ConfirmingRemove)]
    [InlineData("en", RowState.ConfirmingUpdate)]
    [InlineData("de", RowState.ConfirmingUpdate)]
    [InlineData("en", RowState.ConfirmingManage)]
    [InlineData("de", RowState.ConfirmingManage)]
    [InlineData("en", RowState.ChoosingCopyTarget)]
    [InlineData("de", RowState.ChoosingCopyTarget)]
    [InlineData("en", RowState.ConfirmingSaveDelete)]
    [InlineData("de", RowState.ConfirmingSaveDelete)]
    public async Task ContentTab_KeepsEveryRowWholeWhileOneRowWorksOrConfirms(string culture, RowState state)
    {
        using var harness = await ContentAsync();
        Assert.True(harness.Localization.TrySetCulture(culture));
        var viewModel = harness.ViewModel;
        var items = viewModel.ContentGroups.SelectMany(group => group.Items).ToList();
        var saves = viewModel.VehiclesSection.Items.Concat(viewModel.SavesSection.Items).ToList();
        var save = saves.Single(item => item.Name == "Earth Launch 1");
        switch (state)
        {
            case RowState.Installing:
                var installing = items.Single(item => item.ModId == "AdvancedFlightComputer");
                installing.IsInstalling = true;
                installing.Progress = 40;
                break;
            case RowState.ConfirmingRemove:
                items.Single(item => item.ModId == "KSArmory").IsConfirmingRemove = true;
                break;
            case RowState.ConfirmingUpdate:
                items.Single(item => item.ModId == "AdvancedFlightComputer").PackVersionText = harness.Localization.FormatContentAttachVersion("0.7.3", "Starter Pack", "1.0.0");
                break;
            case RowState.ConfirmingManage:
                items.Single(item => item.ModId == "KSArmory").IsConfirmingManage = true;
                break;
            case RowState.ChoosingCopyTarget:
                save.CopyTargets = [viewModel.ActiveInstance!];
                save.CopyTarget = viewModel.ActiveInstance;
                save.IsChoosingTarget = true;
                break;
            case RowState.ConfirmingSaveDelete:
                save.IsConfirmingDelete = true;
                break;
        }

        var problems = await RenderAsync(harness, 860, page => ContentColumns(page, harness, items.Count, saves.Count));

        Assert.Empty(problems);
    }

    public enum RowState
    {
        Installing,
        ConfirmingRemove,
        ConfirmingUpdate,
        ConfirmingManage,
        ChoosingCopyTarget,
        ConfirmingSaveDelete,
    }

    /// <summary>The checks of <see cref="Lined"/> for the mod rows and the vehicle and save rows of the Content tab.</summary>
    private static List<string> ContentColumns(Control page, ViewModelHarness harness, int itemCount, int saveCount)
    {
        var localization = harness.Localization;
        return
        [
            .. Lined(page, "mods", Rows<ContentItem>(page), itemCount, row =>
            {
                var item = (ContentItem)row.DataContext!;
                var slot = row.GetVisualDescendants().OfType<Control>().SingleOrDefault(control => control.IsEffectivelyVisible
                    && (control is Button button && ReferenceEquals(button.Command, item.UpdateCommand) || control is ProgressBar
                        || ToolTip.GetTip(control) as string == localization.InstanceGroupDependencies));
                return
                [
                    new("detached", item.DetachedText is null ? null : Chip(Text(row, localization.ContentDetached))),
                    new("size", Text(row, item.SizeText!)),
                    new("version", Text(row, item.Version)),
                    // the slot after the version holds the update, the progress or the lock, so its cell is what lines up
                    new("slot", slot?.GetVisualAncestors().OfType<Panel>().First(panel => panel.GetVisualParent() is Grid)),
                    new("actions", Menu(row), AtEnd: true),
                ];
            }),
            .. Lined(page, "saves", Rows<GameSaveItem>(page), saveCount, row =>
            {
                var item = (GameSaveItem)row.DataContext!;
                return
                [
                    new("older build", item.IsOlderBuild ? Chip(Text(row, localization.GameSaveOlderBuild)) : null),
                    // a long build of a dev game trims and shows itself whole in the tooltip
                    new("build", Text(row, item.BuildText!), MayTrim: true),
                    new("size", Text(row, item.SizeText)),
                    new("actions", Menu(row), AtEnd: true),
                ];
            }),
        ];
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    public async Task GameDataTab_LinesUpTheColumnsOfTheFoldersAndOfTheBackups(double windowWidth)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var localization = harness.Localization;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var backups = Path.Combine(harness.Services.Paths.GetBackupsRoot(), instance.InstanceId.ToString());
        WriteItem(Path.Combine(backups, "saves", "Orbit-2026-09-01T080000Z"), "Orbit", "v2026.8.3.5117", 10);
        WriteItem(Path.Combine(backups, "saves", "Long Mission Around The Moon-2026-09-02T080000Z"), "Long Mission Around The Moon", "v2026.8.3.5117", 2_000_000);
        Directory.CreateDirectory(Path.Combine(backups, "saves", "copied by hand"));
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.Instances.Single().OpenCommand.ExecuteAsync(null);
        await harness.ViewModel.ShowInstanceGameDataCommand.ExecuteAsync(null);
        var viewModel = harness.ViewModel;
        // a backup without its origin offers Delete only, so its actions are narrower
        Assert.Contains(viewModel.BackupItems, item => item.CanRestore);
        Assert.Contains(viewModel.BackupItems, item => !item.CanRestore);
        Assert.True(viewModel.BackupItems.Select(item => item.SizeText).Distinct().Count() > 1);

        var problems = await RenderAsync(harness, windowWidth, page =>
        [
            .. Lined(page, "game data", Rows<GameDataItem>(page), viewModel.GameDataItems.Count, row =>
            [
                new("size", Text(row, ((GameDataItem)row.DataContext!).SizeText)),
                new("actions", TextButton(row, localization.GameDataOpenFolder), AtEnd: true),
            ]),
            .. Lined(page, "backups", Rows<GameSaveBackupItem>(page), viewModel.BackupItems.Count, row =>
            [
                new("size", Text(row, ((GameSaveBackupItem)row.DataContext!).SizeText)),
                new("actions", TextButton(row, localization.LibraryDelete), AtEnd: true),
            ]),
        ]);

        Assert.Empty(problems);
    }

    /// <summary>
    /// One fact of a row: its column, the part that shows it or null when the row has none, whether the column lines
    /// up at its end, and whether its text may trim.
    /// </summary>
    private readonly record struct Part(string Column, Visual? Visual, bool AtEnd = false, bool MayTrim = false);

    /// <summary>The smallest width that the name of a row keeps, so that a name never shrinks to nothing.</summary>
    private const double MinNameWidth = 64;

    /// <summary>
    /// An instance made from a pack with a detached member that has an update, a pinned mod with a long name, a
    /// dependency, two saves of which one comes from an older game build, and a vehicle with a long build.
    /// </summary>
    private static async Task<ViewModelHarness> ContentAsync()
    {
        var packs = PackViewModelTests.WithPacks(PackViewModelTests.Pack(
            "starter-pack",
            "Starter Pack",
            PackViewModelTests.Version("1.0.0", PackViewModelTests.Pin("AdvancedFlightComputer", "0.7.3"))));
        var harness = await ViewModelHarness.CreateAsync(
            seed: services =>
            {
                var game = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Game")).FullName;
                File.Copy(Path.Combine(AppContext.BaseDirectory, "GameVersionFixture.dll"), Path.Combine(game, "KSA.dll"));
                return services.SettingsRepository.SaveAsync(services.Settings.WithGameDirectory(game));
            },
            editSnapshot: snapshot => PackNewerReleasesViewTests.Rename(packs(snapshot), "KSArmory", LongName));
        var viewModel = harness.ViewModel;
        var source = new InstanceSource.FromModPack("starter-pack", ModVersion.Parse("1.0.0")).WithDetached(["AdvancedFlightComputer"]);
        var instance = (await harness.Services.Instances.CreateAsync("Starter", source)).Instance;
        instance = await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea, version: "0.7.4", into: instance);
        instance = await InstalledContent.AddAsync(harness, "KSArmory", activate: true, ownership: ModInstallOwnership.Borea, into: instance);
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, reason: InstallReason.Dependency, ownership: ModInstallOwnership.Borea, into: instance);
        await harness.Services.Instances.UpdateAsync(instance.InstanceId, saved => saved.SetPinned("KSArmory", true));
        var root = harness.Services.Paths.GetInstanceRoot(instance.InstanceId);
        WriteItem(Path.Combine(root, "saves", "Old"), "Old", "v2026.7.3.4826", 10);
        WriteItem(Path.Combine(root, "saves", "Earth Launch 1"), "Earth Launch 1", "v2026.8.3.5117", 2_000_000);
        WriteItem(Path.Combine(root, "vehicles", "Rocket"), "Rocket", "v2026.8.1.5240-DEBUG--dev-baker", 10, "vehicle.xml");
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();
        await viewModel.WhenInstanceSizesLoadedAsync();
        return harness;
    }

    /// <summary>A folder the way the game writes it, with a meta.toml in the shape of SaveMetaData.</summary>
    private static void WriteItem(string folder, string name, string version, int dataBytes, string dataFile = "universe.xml")
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "meta.toml"), $"""
            name = "{name}"
            created = 2026-07-03T09:15:02.1200000
            updated = 2026-08-01T14:34:32.4054896
            version = "{version}"
            systems = [ "Sol", ]

            """);
        File.WriteAllBytes(Path.Combine(folder, dataFile), new byte[dataBytes]);
    }

    /// <summary>The visible rows whose item is a <typeparamref name="T"/>, each a border around the grid of its cells.</summary>
    private static List<Border> Rows<T>(Control page)
        => [.. page.GetVisualDescendants().OfType<Border>().Where(border => border.IsEffectivelyVisible && border.DataContext is T && border.Child is Grid)];

    /// <summary>The one visible text block of <paramref name="row"/> that shows <paramref name="text"/>, or fails.</summary>
    private static TextBlock Text(Visual row, string text)
        => row.GetVisualDescendants().OfType<TextBlock>().Single(block => block.IsEffectivelyVisible && block.Text == text);

    private static Border Chip(TextBlock text)
        => text.GetVisualAncestors().OfType<Border>().First(border => border.Classes.Contains("chip"));

    /// <summary>The visible menu button of <paramref name="row"/>, or null while the row asks for a confirmation.</summary>
    private static Button? Menu(Visual row)
        => row.GetVisualDescendants().OfType<Button>().SingleOrDefault(button => button.IsEffectivelyVisible && button.Flyout is MenuFlyout);

    private static Button TextButton(Visual row, string label)
        => row.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && button.Content as string == label);

    /// <summary>
    /// Checks that each column of <paramref name="parts"/> starts at the same x in every row that has it, or ends
    /// there for a column that lines up at its end, that no part and no control of a row is outside its row, that no
    /// part trims its text unless it may, and that the name keeps its room and stops before the first part.
    /// </summary>
    private static List<string> Lined(Control page, string table, List<Border> rows, int count, Func<Border, List<Part>> parts)
    {
        var problems = new List<string>();
        if (rows.Count != count)
            problems.Add($"{table}: {rows.Count} rows for {count} items");
        var lines = new Dictionary<string, List<(string Row, double X)>>();
        foreach (var row in rows)
        {
            var name = row.GetVisualDescendants().OfType<TextBlock>().First(block => block.IsEffectivelyVisible && block.Classes.Contains("heading-sm"));
            var nameRight = name.TranslatePoint(new Point(name.Bounds.Width, 0), page)!.Value.X;
            if (name.GetVisualParent<Control>()!.Bounds.Width < MinNameWidth)
                problems.Add($"{table}, {name.Text}: the name has {name.GetVisualParent<Control>()!.Bounds.Width:0.#} px");
            problems.AddRange(row.GetVisualDescendants().OfType<Control>()
                .Where(control => control.IsEffectivelyVisible && control is Button or ToggleSwitch or ProgressBar or ComboBox && Outside(control, row))
                .Select(control => $"{table}, {name.Text}: {(control as ContentControl)?.Content as string ?? control.GetType().Name} is outside the row"));
            foreach (var (column, visual, atEnd, mayTrim) in parts(row))
            {
                if (visual is null)
                    continue;
                var x = visual.TranslatePoint(default, page)!.Value.X;
                if (!lines.TryGetValue(column, out var list))
                    lines[column] = list = [];
                list.Add((name.Text!, atEnd ? x + visual.Bounds.Width : x));
                if (Outside(visual, row))
                    problems.Add($"{table}, {name.Text}: the {column} is outside the row");
                if (nameRight > x + 0.5)
                    problems.Add($"{table}, {name.Text}: the name runs into the {column}");
                if (!mayTrim)
                {
                    problems.AddRange(visual.GetVisualDescendants().Append(visual).OfType<TextBlock>()
                        .Where(block => block.IsEffectivelyVisible && block.TextLayout.TextLines.Any(line => line.HasCollapsed))
                        .Select(block => $"{table}, {name.Text}: {block.Text} is trimmed"));
                }
            }
        }

        foreach (var (column, list) in lines)
        {
            if (list.Max(line => line.X) - list.Min(line => line.X) > 0.5)
                problems.Add($"{table}: the {column} is at {string.Join(", ", list.Select(line => $"{line.Row} {line.X:0.#}"))}");
        }

        return problems;
    }

    private static bool Outside(Visual visual, Visual row)
    {
        var corner = visual.TranslatePoint(default, row)!.Value;
        return corner.X < -0.5 || corner.X + visual.Bounds.Width > row.Bounds.Width + 0.5;
    }

    /// <summary>Renders the instance page next to a navigation rail, as the main window does, and lets the shared columns settle.</summary>
    private static Task<List<string>> RenderAsync(ViewModelHarness harness, double windowWidth, Func<Control, List<string>> read) =>
        HeadlessApp.RunAsync(harness, () =>
        {
            var page = new InstancePage { DataContext = harness.ViewModel };
            Grid.SetColumn(page, 1);
            var body = new Grid { ColumnDefinitions = new ColumnDefinitions($"{PageBodyPanel.NavigationRailWidth},*"), Children = { page } };
            var window = new Window { Width = windowWidth, Height = 900, Content = body, DataContext = harness.ViewModel };
            window.Show();
            try
            {
                // a shared column takes its width after the layout pass that measured its rows
                for (var pass = 0; pass < 3; pass++)
                {
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                }

                return Task.FromResult(read(page));
            }
            finally
            {
                window.Close();
            }
        });
}
