using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class PackNewerReleasesViewTests
{
    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task ModpacksRow_ShowsTheCountInsideTheRow(double windowWidth)
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var text = viewModel.DiscoverPacks.Single().NewerReleasesText!;

        var outside = await RenderAsync(harness, () => new DiscoverPage(), windowWidth, page =>
        {
            var row = page.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("card"));
            return [.. Outside(row, Shown(page, text))];
        });

        Assert.Empty(outside);
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task PackPage_ShowsTheCountTheNewerVersionsAndCopyForumListInsideThePage(double windowWidth)
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.DiscoverPacks.Single().OpenCommand.ExecuteAsync(null);
        viewModel.ShowPackModsCommand.Execute(null);
        var count = viewModel.SelectedPack!.NewerReleasesText!;
        var newer = viewModel.PackMembers.Select(member => member.NewerText).OfType<string>().ToList();

        var outside = await RenderAsync(harness, () => new PackPage(), windowWidth, page =>
        {
            var body = page.GetVisualDescendants().OfType<PageBodyPanel>().Single().GetVisualChildren().OfType<StackPanel>().Single();
            var panel = page.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("side-panel"));
            return
            [
                .. Outside(body, Shown(page, count)),
                .. newer.SelectMany(text => Outside(body, Shown(page, text))),
                .. Outside(panel, Shown(page, harness.Localization.PackCopyForumList)),
            ];
        });

        Assert.Empty(outside);
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task PackPage_ShowsTheUseChipAndThePinnedChipInsideTheirRows(double windowWidth)
    {
        using var harness = await CreateWithPackInstanceAsync();
        var use = harness.ViewModel.PackMembers.Single(member => member.ModId == "AdvancedFlightComputer");
        var pinned = harness.ViewModel.PackMembers.Single(member => member.ModId == "MeasureTools");

        var problems = await RenderAsync(harness, () => new PackPage(), windowWidth, page =>
        {
            var chip = Shown(page, use.UseNewerChipText!);
            var button = chip.FindAncestorOfType<Button>()!;
            var pin = Shown(page, harness.Localization.PackMemberPinned);
            var pinChip = pin.GetVisualAncestors().OfType<Border>().First(border => border.Classes.Contains("chip"));
            var installed = Row(chip).GetVisualDescendants().OfType<TextBlock>().Single(block => block.IsEffectivelyVisible && block.Text == harness.Localization.DiscoverInstalled);
            var installedChip = installed.GetVisualAncestors().OfType<Border>().First(border => border.Classes.Contains("chip"));
            // the long texts are tooltips, not lines under the row
            var lines = page.GetVisualDescendants().OfType<TextBlock>()
                .Where(block => block.IsEffectivelyVisible && (block.Text == use.UseNewerText || block.Text == pinned.PinnedInInstanceText))
                .Select(block => $"line: {block.Text}");
            // the use chip takes the place of the plain chip, and the pinned row keeps the plain chip
            var plain = page.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible && block.Text == use.NewerText).Select(block => $"plain chip: {block.Text}");
            // the Pinned text sits in its chip as the text of the other chips does, in the same readable color
            var pinnedNewer = Shown(page, pinned.NewerText!);
            var level = Math.Abs(pin.TranslatePoint(new Point(0, 0), pinChip)!.Value.Y - installed.TranslatePoint(new Point(0, 0), installedChip)!.Value.Y) < 0.5;
            var color = (pin.Foreground as ISolidColorBrush)?.Color == (pinnedNewer.Foreground as ISolidColorBrush)?.Color;
            return
            [
                .. level ? [] : new[] { "the Pinned text is not level with the text of the other chips" },
                .. color ? [] : new[] { "the Pinned text has another color than the text of the other chips" },
                .. Outside(Row(chip), chip),
                .. Outside(Row(pin), pin),
                .. Outside(Row(pin), Shown(page, pinned.NewerText!)),
                .. plain,
                .. button.Classes.Contains("chip-button") && button.IsEffectivelyEnabled ? [] : new[] { "the use chip is no chip button that acts" },
                .. Math.Abs(button.Bounds.Height - installedChip.Bounds.Height) < 0.5 && chip.FontSize == installed.FontSize ? [] : new[] { "the use chip does not match the chips of its row" },
                .. ToolTip.GetTip(button) as string == use.UseNewerText && AutomationProperties.GetName(button) == use.UseNewerText ? [] : new[] { "the use chip does not name the instance" },
                .. ToolTip.GetTip(pinChip) as string == pinned.PinnedInInstanceText ? [] : new[] { "the pinned chip does not explain the pin" },
                .. lines,
            ];
        });

        Assert.Empty(problems);
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task PackPage_KeepsEveryChipInsideItsRowAndTheVersionsUnderTheirHeader(double windowWidth)
    {
        using var harness = await CreateWithPackInstanceAsync();
        var members = harness.ViewModel.PackMembers.ToList();
        // the row of the report, with the use chip, Untested and Installed
        var crowded = members.Single(member => member.ModId == "AdvancedFlightComputer");
        Assert.Equal((true, true, true), (crowded.OffersUseNewer, crowded.IsUntested, crowded.IsInstalled));
        Assert.Contains(members, member => member.Name == LongName);

        var problems = await RenderAsync(harness, () => new PackPage(), windowWidth, page =>
        {
            var problems = new List<string>();
            var rows = page.GetVisualDescendants().OfType<Border>().Where(border => border.DataContext is PackMemberItem && border.GetVisualParent() is ContentPresenter).ToList();
            var table = rows[0].FindAncestorOfType<ItemsControl>()!.GetVisualParent<StackPanel>()!;
            var header = table.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == harness.Localization.ContentVersionHeader);
            foreach (var row in rows)
            {
                var chips = row.GetVisualDescendants().OfType<Control>()
                    .Where(control => control.IsEffectivelyVisible && (control is Border border && border.Classes.Contains("chip") || control is Button button && button.Classes.Contains("chip-button")))
                    .ToList();
                var member = (PackMemberItem)row.DataContext!;
                var column = row.GetVisualDescendants().OfType<WrapPanel>().First();
                foreach (var chip in chips)
                {
                    var corner = chip.TranslatePoint(new Point(0, 0), column)!.Value;
                    var end = chip.TranslatePoint(new Point(chip.Bounds.Width, chip.Bounds.Height), row)!.Value;
                    if (corner.X < -0.5 || corner.Y < -0.5 || corner.X + chip.Bounds.Width > column.Bounds.Width + 0.5 || corner.Y + chip.Bounds.Height > column.Bounds.Height + 0.5
                        || end.X > row.Bounds.Width + 0.5 || end.Y > row.Bounds.Height + 0.5)
                        problems.Add($"{member.Name}: a chip is cut");
                }
                // every line of chips ends at the right edge of the chip column, and a wide window has room for one line
                var lines = chips.GroupBy(chip => Math.Round(chip.TranslatePoint(new Point(0, 0), column)!.Value.Y)).ToList();
                foreach (var line in lines)
                {
                    if (Math.Abs(line.Max(chip => chip.TranslatePoint(new Point(chip.Bounds.Width, 0), column)!.Value.X) - column.Bounds.Width) > 0.5)
                        problems.Add($"{member.Name}: a line of chips is not right-aligned");
                }
                if (windowWidth >= 1280 && lines.Count > 1)
                    problems.Add($"{member.Name}: the chips wrap although the window is wide");
                // the chips take only the room that four chips need, so a long name gets the rest
                if (column.Bounds.Width > 360.5)
                    problems.Add($"{member.Name}: the chips take room that the name needs");
                var version = row.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == member.Version);
                if (version.IsEffectivelyVisible && header.IsEffectivelyVisible
                    && Math.Abs(version.TranslatePoint(new Point(0, 0), table)!.Value.X - header.TranslatePoint(new Point(0, 0), table)!.Value.X) > 0.5)
                    problems.Add($"{member.Name}: the version is not under its header");
                var name = row.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == member.Name);
                if (version.IsEffectivelyVisible && name.TranslatePoint(new Point(name.Bounds.Width, 0), row)!.Value.X + 8 > version.TranslatePoint(new Point(0, 0), row)!.Value.X + 0.5)
                    problems.Add($"{member.Name}: the name runs into the version");
                if ((windowWidth >= 1920 || member.Name != LongName) && name.TextLayout.TextLines.Any(line => line.HasCollapsed))
                    problems.Add($"{member.Name}: the name is cut although it fits");
            }
            if (rows.Count != members.Count)
                problems.Add($"{rows.Count} rows for {members.Count} members");
            return problems;
        });

        Assert.Empty(problems);
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task PackPage_OpensTheConfirmationBelowTheRowOnlyAfterTheClick(double windowWidth)
    {
        using var harness = await CreateWithPackInstanceAsync();
        var member = harness.ViewModel.PackMembers.Single(member => member.ModId == "AdvancedFlightComputer");

        var before = await RenderAsync(harness, () => new PackPage(), windowWidth, page =>
            [.. page.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible && block.Text == member.UseNewerConfirmText).Select(block => $"before: {block.Text}")]);
        await member.UseNewerCommand.ExecuteAsync(null);
        var after = await RenderAsync(harness, () => new PackPage(), windowWidth, page =>
        {
            var chip = Shown(page, member.UseNewerChipText!);
            var confirm = Shown(page, member.UseNewerConfirmText!);
            var replace = Shown(page, member.ConfirmInstallText);
            var below = confirm.TranslatePoint(new Point(0, 0), Row(chip))!.Value.Y >= chip.TranslatePoint(new Point(0, chip.Bounds.Height), Row(chip))!.Value.Y;
            return
            [
                .. Outside(Row(chip), confirm),
                .. Outside(Row(chip), replace),
                .. below ? [] : new[] { "the confirmation is not below the chip" },
                .. chip.FindAncestorOfType<Button>()!.IsEffectivelyEnabled ? new[] { "the use chip acts while the change waits" } : [],
            ];
        });

        Assert.True(member.IsConfirmingInstall);
        Assert.Empty(before);
        Assert.Empty(after);
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task PackPage_ShowsTheInUseChipOfADetachedMemberInsideItsRow(double windowWidth)
    {
        using var harness = await CreateWithPackInstanceAsync(afcInUse: "0.7.5");
        var member = harness.ViewModel.PackMembers.Single(member => member.ModId == "AdvancedFlightComputer");
        Assert.Equal((false, false), (member.IsInstalled, member.OffersUseNewer));

        var problems = await RenderAsync(harness, () => new PackPage(), windowWidth, page =>
        {
            var text = Shown(page, member.InUseChipText!);
            var chip = text.GetVisualAncestors().OfType<Border>().First(border => border.Classes.Contains("chip"));
            var row = Row(text);
            var column = row.GetVisualDescendants().OfType<WrapPanel>().First();
            var chips = row.GetVisualDescendants().OfType<Border>().Where(border => border.IsEffectivelyVisible && border.Classes.Contains("chip")).ToList();
            // the newer chip stays, and the in use chip looks like the Pinned chip, with its text level with and in the color of the other chip texts
            var newer = Shown(page, member.NewerText!);
            var newerChip = newer.GetVisualAncestors().OfType<Border>().First(border => border.Classes.Contains("chip"));
            var level = Math.Abs(text.TranslatePoint(new Point(0, 0), chip)!.Value.Y - newer.TranslatePoint(new Point(0, 0), newerChip)!.Value.Y) < 0.5;
            var color = (text.Foreground as ISolidColorBrush)?.Color == (newer.Foreground as ISolidColorBrush)?.Color;
            var corner = chip.TranslatePoint(new Point(0, 0), column)!.Value;
            var lines = chips.Select(border => Math.Round(border.TranslatePoint(new Point(0, 0), column)!.Value.Y)).Distinct().Count();
            var shownInstalled = row.GetVisualDescendants().OfType<TextBlock>().Any(block => block.IsEffectivelyVisible && block.Text == harness.Localization.DiscoverInstalled);
            var shownUse = row.GetVisualDescendants().OfType<Button>().Any(button => button.IsEffectivelyVisible && button.Classes.Contains("chip-button"));
            return
            [
                .. Outside(row, text),
                .. corner.X >= -0.5 && corner.X + chip.Bounds.Width <= column.Bounds.Width + 0.5 ? [] : new[] { "the in use chip is outside the chip column" },
                .. chip.Classes.Contains("muted") ? [] : new[] { "the in use chip is not muted like the Pinned chip" },
                .. level ? [] : new[] { "the in use text is not level with the text of the other chips" },
                .. color ? [] : new[] { "the in use text has another color than the text of the other chips" },
                .. Math.Abs(chip.Bounds.Height - newerChip.Bounds.Height) < 0.5 ? [] : new[] { "the in use chip does not match the chips of its row" },
                .. windowWidth >= 1280 && lines > 1 ? new[] { "the chips wrap although the window is wide" } : [],
                .. ToolTip.GetTip(chip) as string == member.InUseText && AutomationProperties.GetName(chip) == member.InUseText ? [] : new[] { "the in use chip does not name the instance" },
                .. shownInstalled ? new[] { "the row shows Installed" } : [],
                .. shownUse ? new[] { "the row offers the newer release" } : [],
                .. page.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible && block.Text == member.InUseText).Select(block => $"line: {block.Text}"),
            ];
        });

        Assert.Empty(problems);
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task PackPage_TrimsALongVersionInUseInsideItsChipColumn(double windowWidth)
    {
        // 0.7.4 becomes a long pre-release of 0.7.5, so 0.7.5 stays the newer release
        const string LongVersion = "0.7.5-release-candidate.20261005.build-1234567890";
        using var harness = await CreateWithPackInstanceAsync(afcInUse: LongVersion, editSnapshot: json => Reversion(json, "AdvancedFlightComputer", "0.7.4", LongVersion));
        var member = harness.ViewModel.PackMembers.Single(member => member.ModId == "AdvancedFlightComputer");

        var problems = await RenderAsync(harness, () => new PackPage(), windowWidth, page =>
        {
            var text = Shown(page, member.InUseChipText!);
            var chip = text.GetVisualAncestors().OfType<Border>().First(border => border.Classes.Contains("chip"));
            var column = Row(text).GetVisualDescendants().OfType<WrapPanel>().First();
            var corner = chip.TranslatePoint(new Point(0, 0), column)!.Value;
            return
            [
                .. corner.X >= -0.5 && corner.X + chip.Bounds.Width <= column.Bounds.Width + 0.5 ? [] : new[] { "the in use chip is outside the chip column" },
                .. text.TextLayout.TextLines.Any(line => line.HasCollapsed) ? [] : new[] { "the long version in use is not trimmed" },
                .. (ToolTip.GetTip(chip) as string)?.Contains(LongVersion, StringComparison.Ordinal) == true ? [] : new[] { "the tooltip does not show the full version" },
            ];
        });

        Assert.Empty(problems);
    }

    /// <summary>A pack of three mods where two have a newer release.</summary>
    private static async Task<ViewModelHarness> CreateAsync()
    {
        var harness = await ViewModelHarness.CreateAsync(editSnapshot: PackViewModelTests.WithPacks(PackViewModelTests.Pack(
            "starter-pack",
            "Starter Pack",
            PackViewModelTests.Version("1.0.0", PackViewModelTests.Pin("AdvancedFlightComputer", "0.7.4"), PackViewModelTests.Pin("KSArmory", "0.8.44"), PackViewModelTests.Pin("MeasureTools", "1.1.9")))));
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        harness.ViewModel.ShowDiscoverModpacksCommand.Execute(null);
        return harness;
    }

    /// <summary>
    /// A pack on its Mods tab with an active instance made from it that holds two members at their pins and pins
    /// MeasureTools. The game is 2026.9.7.5402, so the pinned AdvancedFlightComputer 0.7.3 is untested and its row
    /// shows the use chip, Untested and Installed. KSArmory has a long name.
    /// </summary>
    /// <param name="afcInUse">The version of AdvancedFlightComputer that the instance holds detached from the pack, or null to hold it at the pin.</param>
    /// <param name="editSnapshot">Changes the index snapshot after the pack is in it.</param>
    private static async Task<ViewModelHarness> CreateWithPackInstanceAsync(string? afcInUse = null, Func<string, string>? editSnapshot = null)
    {
        var packs = PackViewModelTests.WithPacks(PackViewModelTests.Pack(
            "starter-pack",
            "Starter Pack",
            PackViewModelTests.Version("1.0.0", PackViewModelTests.Pin("AdvancedFlightComputer", "0.7.3"), PackViewModelTests.Pin("KSArmory", "0.8.44"), PackViewModelTests.Pin("MeasureTools", "1.1.9"))));
        var harness = await ViewModelHarness.CreateAsync(editSnapshot: snapshot =>
        {
            var edited = Rename(packs(snapshot), "KSArmory", LongName);
            return editSnapshot?.Invoke(edited) ?? edited;
        });
        var viewModel = harness.ViewModel;
        var source = new InstanceSource.FromModPack("starter-pack", ModVersion.Parse("1.0.0"));
        var instance = (await harness.Services.Instances.CreateAsync("Starter", afcInUse is null ? source : source.WithDetached(["AdvancedFlightComputer"]))).Instance;
        instance = await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, reason: afcInUse is null ? InstallReason.ModPack : InstallReason.Manual, ownership: ModInstallOwnership.Borea, version: afcInUse ?? "0.7.3", into: instance);
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, reason: InstallReason.ModPack, ownership: ModInstallOwnership.Borea, version: "1.1.9", into: instance);
        await harness.Services.Instances.UpdateAsync(instance.InstanceId, saved => saved.SetPinned("MeasureTools", true));
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        Assert.True(GameVersion.TryParse("2026.9.7.5402", out var game));
        await viewModel.RefreshCompatibilityAsync(game);
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        await viewModel.DiscoverPacks.Single().OpenCommand.ExecuteAsync(null);
        viewModel.ShowPackModsCommand.Execute(null);
        return harness;
    }

    private const string LongName = "KSArmory Extended Arsenal for Orbital Science Missions";

    /// <summary>Gives the listing <paramref name="modId"/> and its releases the name <paramref name="name"/>.</summary>
    private static string Rename(string json, string modId, string name)
    {
        var root = JsonNode.Parse(json)!;
        var listing = root["listings"]!.AsArray().Single(node => (string?)node!["id"] == modId)!;
        listing["authored"]!["name"] = name;
        foreach (var release in listing["releases"]!.AsArray())
            release!["listing"]!["name"] = name;
        return root.ToJsonString();
    }

    /// <summary>Gives the release <paramref name="from"/> of <paramref name="modId"/> the version <paramref name="to"/>.</summary>
    private static string Reversion(string json, string modId, string from, string to)
    {
        var root = JsonNode.Parse(json)!;
        var listing = root["listings"]!.AsArray().Single(node => (string?)node!["id"] == modId)!;
        listing["releases"]!.AsArray().Single(release => (string?)release!["version"] == from)!["version"] = to;
        return root.ToJsonString();
    }

    /// <summary>The row of the Mods tab that holds <paramref name="control"/>.</summary>
    private static Border Row(Control control)
        => control.GetVisualAncestors().OfType<Border>().Last(border => border.DataContext is PackMemberItem);

    /// <summary>Renders the page next to a navigation rail, as the main window does.</summary>
    private static Task<List<string>> RenderAsync(ViewModelHarness harness, Func<Control> createPage, double windowWidth, Func<Control, List<string>> read) =>
        HeadlessApp.RunAsync(harness, () =>
        {
            var page = createPage();
            page.DataContext = harness.ViewModel;
            Grid.SetColumn(page, 1);
            var body = new Grid { ColumnDefinitions = new ColumnDefinitions($"{PageBodyPanel.NavigationRailWidth},*"), Children = { page } };
            var window = new Window { Width = windowWidth, Height = 832, Content = body, DataContext = harness.ViewModel };
            window.Show();
            window.UpdateLayout();
            try
            {
                return Task.FromResult(read(page));
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>The one visible text block that shows <paramref name="text"/>, or fails.</summary>
    private static TextBlock Shown(Control page, string text)
        => page.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == text && block.IsEffectivelyVisible);

    private static IEnumerable<string> Outside(Control container, TextBlock text)
    {
        var corner = text.TranslatePoint(new Point(0, 0), container) ?? throw new InvalidOperationException($"{text.Text} is not laid out inside {container}.");
        var fits = corner.X >= 0 && corner.Y >= 0
            && corner.X + text.Bounds.Width <= container.Bounds.Width + 0.5
            && corner.Y + text.Bounds.Height <= container.Bounds.Height + 0.5
            && !text.TextLayout.TextLines.Any(line => line.HasCollapsed);
        return fits ? [] : [text.Text ?? string.Empty];
    }
}
