using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Threading;
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
    public async Task PackPage_ShowsTheUseChipsWithTheirIconAndThePinnedChipInsideTheirRows(double windowWidth)
    {
        using var harness = await CreateWithPackInstanceAsync();
        var use = harness.ViewModel.PackMembers.Single(member => member.ModId == "AdvancedFlightComputer");
        var pinned = harness.ViewModel.PackMembers.Single(member => member.ModId == "MeasureTools");
        // the pinned member offers the newer release too, because its confirmation unpins it
        Assert.Equal((true, true), (use.OffersUseNewer, pinned.OffersUseNewer));
        Assert.NotNull(pinned.PinnedInInstanceText);

        var problems = await RenderAsync(harness, () => new PackPage(), windowWidth, page =>
        {
            var problems = new List<string>();
            var installed = Row(ChipButton(page, use.UseNewerText!)).GetVisualDescendants().OfType<TextBlock>().Single(block => block.IsEffectivelyVisible && block.Text == harness.Localization.DiscoverInstalled);
            var installedChip = installed.GetVisualAncestors().OfType<Border>().First(border => border.Classes.Contains("chip"));
            page.TryFindResource("Icon.ArrowUp", out var arrow);
            foreach (var member in new[] { use, pinned })
            {
                var button = ChipButton(page, member.UseNewerText!);
                var chip = ChipText(button);
                // a folded row keeps the verb, so the chip says what it does at every width
                if (chip.Text != member.UseNewerChipText)
                    problems.Add($"{member.Name}: the use chip shows {chip.Text}");
                var icon = button.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().SingleOrDefault(path => path.IsEffectivelyVisible);
                problems.AddRange(Outside(Row(chip), chip));
                if (!button.Classes.Contains("chip-button") || !button.IsEffectivelyEnabled)
                    problems.Add($"{member.Name}: the use chip is no chip button that acts");
                // the arrow says that the chip acts, in the accent color of its text
                if (icon is null || !ReferenceEquals(icon.Data, arrow) || (icon.Stroke as ISolidColorBrush)?.Color != (chip.Foreground as ISolidColorBrush)?.Color)
                    problems.Add($"{member.Name}: the use chip has no arrow up in its text color");
                if (Math.Abs(button.Bounds.Height - installedChip.Bounds.Height) > 0.5 || chip.FontSize != installed.FontSize)
                    problems.Add($"{member.Name}: the use chip does not match the chips of its row");
                if (ToolTip.GetTip(button) as string != member.UseNewerText || AutomationProperties.GetName(button) != member.UseNewerText)
                    problems.Add($"{member.Name}: the use chip does not name the instance");
                // the use chip takes the place of the plain chip
                if (page.GetVisualDescendants().OfType<TextBlock>().Any(block => block.IsEffectivelyVisible && block.Text == member.NewerText))
                    problems.Add($"{member.Name}: the plain newer chip shows next to the use chip");
            }
            // the Pinned chip sits in the column of the instance and keeps its text at every width, so it does not look like an icon button
            var pinChip = Row(ChipButton(page, pinned.UseNewerText!)).GetVisualDescendants().OfType<Border>()
                .Single(border => border.IsEffectivelyVisible && border.Classes.Contains("chip") && ToolTip.GetTip(border) as string == pinned.PinnedInInstanceText);
            if (AutomationProperties.GetName(pinChip) != pinned.PinnedInInstanceText)
                problems.Add("the pinned chip does not explain the pin");
            if (pinChip.FindAncestorOfType<WrapPanel>() is not { } cell || !cell.Classes.Contains("member-cell") || Grid.GetColumn(cell) != 2)
                problems.Add("the pinned chip is not in the column of the instance");
            var pin = pinChip.GetVisualDescendants().OfType<TextBlock>().Single();
            if (!pin.IsEffectivelyVisible || pin.Text != harness.Localization.PackMemberPinned)
                problems.Add("the Pinned chip has no text");
            // the Pinned text sits in its chip as the text of the other chips does, in the readable color of the plain chips
            page.TryFindResource("Brush.TextSecondary", page.ActualThemeVariant, out var secondary);
            if (Math.Abs(pin.TranslatePoint(new Point(0, 0), pinChip)!.Value.Y - installed.TranslatePoint(new Point(0, 0), installedChip)!.Value.Y) > 0.5)
                problems.Add("the Pinned text is not level with the text of the other chips");
            if ((pin.Foreground as ISolidColorBrush)?.Color != (secondary as ISolidColorBrush)?.Color)
                problems.Add("the Pinned text has another color than the text of the plain chips");
            problems.AddRange(Outside(Row(pin), pin));
            // the long texts are tooltips, not lines under the row
            problems.AddRange(page.GetVisualDescendants().OfType<TextBlock>()
                .Where(block => block.IsEffectivelyVisible && (block.Text == use.UseNewerText || block.Text == pinned.PinnedInInstanceText))
                .Select(block => $"line: {block.Text}"));
            return problems;
        });

        Assert.Empty(problems);
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1100)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task PackPage_LinesUpEveryColumnInTheHeaderAndInEveryRow(double windowWidth)
    {
        using var harness = await CreateWithPackInstanceAsync();
        var viewModel = harness.ViewModel;
        var members = viewModel.PackMembers.ToList();
        // the row of the report, with the use chip, Untested and Installed
        var crowded = members.Single(member => member.ModId == "AdvancedFlightComputer");
        Assert.Equal((true, true, true), (crowded.OffersUseNewer, crowded.IsUntested, crowded.IsInstalled));
        Assert.Contains(members, member => member.Name == LongName);
        Assert.NotNull(viewModel.PackInstanceHeaderText);

        var problems = await RenderAsync(harness, () => new PackPage(), windowWidth, page => Table(harness, page, windowWidth).Problems);

        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("1280 860")]
    [InlineData("860 1280 860")]
    [InlineData("1280 1100")]
    [InlineData("860 1280")]
    public async Task PackPage_KeepsTheColumnsInLineWhenTheWindowChangesItsWidth(string widths)
    {
        var steps = widths.Split(' ').Select(width => double.Parse(width, CultureInfo.InvariantCulture)).ToList();
        using var harness = await CreateWithPackInstanceAsync();

        var fresh = await RenderAsync(harness, () => new PackPage(), steps[^1], page => Table(harness, page, steps[^1]).Starts);
        var resized = await RenderAsync(harness, () => new PackPage(), steps, page =>
        {
            var (problems, starts) = Table(harness, page, steps[^1]);
            // a window that was wide once lays the table out as a new window of the same width does
            return [.. problems, .. starts.Except(fresh).Select(start => $"after resizing: {start}, fresh: {string.Join("; ", fresh)}")];
        });

        Assert.Empty(resized);
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
            var chip = ChipText(ChipButton(page, member.UseNewerText!));
            var confirm = Shown(page, member.ChangeConfirmText!);
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
        Assert.Equal(member.UseNewerConfirmText, member.ChangeConfirmText);
        Assert.Empty(before);
        Assert.Empty(after);
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task PackPage_ShowsTheInUseChipAndTheBackChipOfADetachedMemberInTheirColumns(double windowWidth)
    {
        using var harness = await CreateWithPackInstanceAsync(afcInUse: "0.7.5");
        var member = harness.ViewModel.PackMembers.Single(member => member.ModId == "AdvancedFlightComputer");
        // the instance uses the newer release, so the row offers the pinned version again instead of naming the newer one
        Assert.Equal((false, false, false, true), (member.IsInstalled, member.OffersUseNewer, member.ShowsNewerAvailable, member.OffersBackToPin));

        var problems = await RenderAsync(harness, () => new PackPage(), windowWidth, page =>
        {
            var chip = Chip(page, member.InUseText!);
            var text = ChipText(chip);
            var row = Row(text);
            var button = ChipButton(page, member.BackToPinText!);
            var back = ChipText(button);
            page.TryFindResource("Brush.Positive", page.ActualThemeVariant, out var positive);
            var buttons = row.GetVisualDescendants().OfType<Button>().Where(control => control.IsEffectivelyVisible && control.Classes.Contains("chip-button")).ToList();
            // the back icon draws as large as the arrow of a use chip, inside its chip and before its text
            var use = ChipButton(page, harness.ViewModel.PackMembers.Single(other => other.ModId == "MeasureTools").UseNewerText!);
            var backIcon = Drawn(Icon(button), button);
            var useIcon = Drawn(Icon(use), use);
            var backText = Drawn(back, button);
            return
            [
                .. Outside(row, text),
                .. Outside(row, back),
                .. Math.Abs(backIcon.Width - useIcon.Width) < 0.5 && Math.Abs(backIcon.Height - useIcon.Height) < 0.5 ? [] : new[] { $"the back icon draws at {backIcon.Size}, the use icon at {useIcon.Size}" },
                .. backIcon.X >= 0 && backIcon.Right <= backText.X + 0.5 ? [] : new[] { $"the back icon at {backIcon} runs into its chip border or its text at {backText}" },
                .. Cell(row, 2).GetVisualDescendants().Contains(chip) ? [] : new[] { "the in use chip is not in the column of the instance" },
                .. Cell(row, 3).GetVisualDescendants().Contains(button) ? [] : new[] { "the back chip is not in the column of the newer release" },
                // a release in use that fits the game is installed as much as the pinned one, so it is green like Installed
                .. chip.Classes.Contains("positive") ? [] : new[] { "the in use chip is not green like Installed" },
                .. (text.Foreground as ISolidColorBrush)?.Color == (positive as ISolidColorBrush)?.Color ? [] : new[] { "the in use text is not in the color of Installed" },
                .. Math.Abs(chip.Bounds.Height - button.Bounds.Height) < 0.5 ? [] : new[] { "the in use chip does not match the chips of its row" },
                .. ToolTip.GetTip(chip) as string == member.InUseText && AutomationProperties.GetName(chip) == member.InUseText ? [] : new[] { "the in use chip does not name the instance" },
                .. ToolTip.GetTip(button) as string == member.BackToPinText && AutomationProperties.GetName(button) == member.BackToPinText ? [] : new[] { "the back chip does not name the instance" },
                .. button.IsEffectivelyEnabled ? [] : new[] { "the back chip does not act" },
                .. buttons.Count == 1 ? [] : new[] { $"{buttons.Count} chip buttons in the row" },
                .. row.GetVisualDescendants().OfType<TextBlock>().Any(block => block.IsEffectivelyVisible && (block.Text == harness.Localization.DiscoverInstalled || block.Text == member.NewerText)) ? new[] { "the row shows Installed or the newer release that is in use" } : [],
                .. page.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible && (block.Text == member.InUseText || block.Text == member.BackToPinText)).Select(block => $"line: {block.Text}"),
            ];
        });

        Assert.Empty(problems);
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task PackPage_OpensTheConfirmationOfTheBackChipBelowTheRow(double windowWidth)
    {
        using var harness = await CreateWithPackInstanceAsync(afcInUse: "0.7.5");
        var member = harness.ViewModel.PackMembers.Single(member => member.ModId == "AdvancedFlightComputer");

        await member.BackToPinCommand.ExecuteAsync(null);
        var problems = await RenderAsync(harness, () => new PackPage(), windowWidth, page =>
        {
            var chip = ChipText(ChipButton(page, member.BackToPinText!));
            var confirm = Shown(page, member.ChangeConfirmText!);
            var replace = Shown(page, member.ConfirmInstallText);
            var below = confirm.TranslatePoint(new Point(0, 0), Row(chip))!.Value.Y >= chip.TranslatePoint(new Point(0, chip.Bounds.Height), Row(chip))!.Value.Y;
            return
            [
                .. Outside(Row(chip), confirm),
                .. Outside(Row(chip), replace),
                .. below ? [] : new[] { "the confirmation is not below the chip" },
                .. chip.FindAncestorOfType<Button>()!.IsEffectivelyEnabled ? new[] { "the back chip acts while the change waits" } : [],
            ];
        });

        Assert.True(member.IsConfirmingInstall);
        Assert.Equal(harness.Localization.FormatPackMemberBackToConfirm(member.Name, "Starter", "0.7.3"), member.ChangeConfirmText);
        var replace = member.InstallWarning is null ? harness.Localization.FormatContentReplaceVersion("0.7.5") : harness.Localization.FormatContentReplaceVersionAnyway("0.7.5");
        Assert.StartsWith(replace, member.ConfirmInstallText);
        Assert.Empty(problems);
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task PackPage_TrimsALongVersionInUseInsideItsColumn(double windowWidth)
    {
        // 0.7.4 becomes a long pre-release of 0.7.5, so 0.7.5 stays the newer release
        const string LongVersion = "0.7.5-release-candidate.20261005.build-1234567890";
        using var harness = await CreateWithPackInstanceAsync(afcInUse: LongVersion, editSnapshot: json => Reversion(json, "AdvancedFlightComputer", "0.7.4", LongVersion));
        var member = harness.ViewModel.PackMembers.Single(member => member.ModId == "AdvancedFlightComputer");

        var problems = await RenderAsync(harness, () => new PackPage(), windowWidth, page =>
        {
            var chip = Chip(page, member.InUseText!);
            var text = ChipText(chip);
            var column = Cell(Row(text), 2);
            var corner = chip.TranslatePoint(new Point(0, 0), column)!.Value;
            return
            [
                .. corner.X >= -0.5 && corner.X + chip.Bounds.Width <= column.Bounds.Width + 0.5 ? [] : new[] { "the in use chip is outside the column of the instance" },
                .. text.TextLayout.TextLines.Any(line => line.HasCollapsed) ? [] : new[] { "the long version in use is not trimmed" },
                .. (ToolTip.GetTip(chip) as string)?.Contains(LongVersion, StringComparison.Ordinal) == true ? [] : new[] { "the tooltip does not show the full version" },
            ];
        });

        Assert.Empty(problems);
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task InstancePage_ShowsTheDetachedChipOnlyOnTheRowOfADetachedMember(double windowWidth)
    {
        using var harness = await CreateWithPackInstanceAsync(afcInUse: "0.7.5");
        await harness.ViewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var items = harness.ViewModel.ContentGroups.SelectMany(group => group.Items).ToList();
        var detached = items.Single(item => item.ModId == "AdvancedFlightComputer");
        Assert.Equal(harness.Localization.FormatContentDetachedFrom("Starter Pack", "0.7.3"), detached.DetachedText);
        Assert.Null(items.Single(item => item.ModId == "MeasureTools").DetachedText);

        var problems = await RenderAsync(harness, () => new InstancePage(), windowWidth, page =>
        {
            var texts = page.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible && block.Text == harness.Localization.ContentDetached).ToList();
            if (texts.Count != 1)
                return [$"{texts.Count} Detached chips"];
            var text = texts[0];
            var chip = text.GetVisualAncestors().OfType<Border>().First(border => border.Classes.Contains("chip"));
            var row = text.GetVisualAncestors().OfType<Border>().First(border => border.Classes.Contains("list-row"));
            return
            [
                .. Outside(row, text),
                .. row.DataContext is ContentItem { ModId: "AdvancedFlightComputer" } ? [] : new[] { "the Detached chip is on another row" },
                .. chip.Classes.Contains("muted") ? [] : new[] { "the Detached chip is not muted" },
                .. chip.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Any(path => path.IsEffectivelyVisible) ? [] : new[] { "the Detached chip has no icon" },
                .. ToolTip.GetTip(chip) as string == detached.DetachedText && AutomationProperties.GetName(chip) == detached.DetachedText ? [] : new[] { "the Detached chip does not name the pack" },
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

    /// <summary>
    /// Checks that every column of the Mods tab starts at the same x in the header and in every row, that no cell
    /// runs into the next column and that no chip is cut. Also returns where each column starts, one line per column.
    /// </summary>
    private static (List<string> Problems, List<string> Starts) Table(ViewModelHarness harness, Control page, double windowWidth)
    {
        var viewModel = harness.ViewModel;
        var members = viewModel.PackMembers.ToList();
        // the page body is narrower than 720 px below a window of about 1170 px, where each row folds into two lines
        var folded = windowWidth < 1170;
        var problems = new List<string>();
        var rows = MemberRows(page);
        var table = rows[0].FindAncestorOfType<ItemsControl>()!.GetVisualParent<StackPanel>()!;
        var starts = new Dictionary<string, List<(string Where, double X)>>();
        double X(Visual visual) => visual.TranslatePoint(new Point(0, 0), table)!.Value.X;
        double Right(Visual visual) => X(visual) + visual.Bounds.Width;
        void Add(string column, string where, Visual? first)
        {
            if (first is null)
                return;
            if (!starts.TryGetValue(column, out var list))
                starts[column] = list = [];
            list.Add((where, X(first)));
        }

        // the instance header starts with the instance icon, which tells the instance apart from a pack of the same name
        var instanceHeader = Shown(page, viewModel.PackInstanceHeaderText!).GetVisualParent<DockPanel>()!;
        page.TryFindResource("Icon.Library", out var library);
        if (instanceHeader.Children.OfType<Avalonia.Controls.Shapes.Path>().FirstOrDefault() is not { } icon || !ReferenceEquals(icon.Data, library) || !icon.IsEffectivelyVisible)
            problems.Add("the instance header has no instance icon");
        if (ToolTip.GetTip(instanceHeader) as string != viewModel.ActiveInstance!.Name)
            problems.Add("the instance header does not name the whole instance in its tooltip");
        Add("instance", "header", instanceHeader);
        var versionHeader = table.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == harness.Localization.ContentVersionHeader);
        if (versionHeader.IsEffectivelyVisible == folded)
            problems.Add("the Version header shows on a folded row or hides on a wide one");
        Add("version", "header", versionHeader.IsEffectivelyVisible ? versionHeader : null);
        foreach (var row in rows)
        {
            var member = (PackMemberItem)row.DataContext!;
            var versionCell = row.GetVisualDescendants().OfType<Control>().First(control => control.Classes.Contains("member-version"));
            var version = versionCell.GetVisualChildren().OfType<TextBlock>().Single(block => block.Text == member.Version);
            var instance = Cell(row, 2);
            var newer = Cell(row, 3);
            // every row names the version that the pack pins, also on a small page
            if (!version.IsEffectivelyVisible)
                problems.Add($"{member.Name}: the pinned version hides");
            Add("version", member.Name, version.IsEffectivelyVisible ? version : null);
            Add("fit", member.Name, Chips(versionCell).FirstOrDefault());
            Add("instance", member.Name, instance);
            Add("instance chip", member.Name, Chips(instance).FirstOrDefault());
            Add("newer", member.Name, newer);
            Add("newer chip", member.Name, Chips(newer).FirstOrDefault());
            // a cell ends before the margin of the next column
            foreach (var part in Chips(versionCell).Append(version).Where(part => part.IsEffectivelyVisible))
            {
                if (Right(part) > X(instance) - 16 + 0.5)
                    problems.Add($"{member.Name}: the version cell runs into the column of the instance");
            }
            foreach (var chip in Chips(instance))
            {
                if (Right(chip) > X(newer) - 16 + 0.5)
                    problems.Add($"{member.Name}: the instance cell runs into the column of the newer release");
            }
            foreach (var chip in Chips(row))
            {
                var corner = chip.TranslatePoint(new Point(0, 0), row)!.Value;
                if (corner.X < -0.5 || corner.Y < -0.5 || corner.X + chip.Bounds.Width > row.Bounds.Width + 0.5 || corner.Y + chip.Bounds.Height > row.Bounds.Height + 0.5)
                    problems.Add($"{member.Name}: a chip is cut");
                problems.AddRange(chip.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible && block.TextLayout.TextLines.Any(line => line.HasCollapsed)).Select(block => $"{member.Name}: {block.Text} is trimmed"));
            }
            var name = row.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == member.Name);
            var cellTop = versionCell.TranslatePoint(new Point(0, 0), row)!.Value.Y;
            if (folded && name.TranslatePoint(new Point(0, name.Bounds.Height), row)!.Value.Y > cellTop + 0.5)
                problems.Add($"{member.Name}: the folded row does not show its version below the name");
            if (!folded && name.TranslatePoint(new Point(name.Bounds.Width, 0), row)!.Value.X + 8 > versionCell.TranslatePoint(new Point(0, 0), row)!.Value.X + 0.5)
                problems.Add($"{member.Name}: the name runs into the version");
            if ((windowWidth >= 1920 || member.Name != LongName) && name.TextLayout.TextLines.Any(line => line.HasCollapsed))
                problems.Add($"{member.Name}: the name is cut although it fits");
        }
        foreach (var (column, list) in starts)
        {
            if (list.Max(start => start.X) - list.Min(start => start.X) > 0.5)
                problems.Add($"the {column} column starts at {string.Join(", ", list.Select(start => $"{start.Where} {start.X:0.#}"))}");
        }
        // the chips of the instance and of the newer release show in every row that has them
        if (starts.GetValueOrDefault("instance chip")?.Count != 2 || starts.GetValueOrDefault("newer chip")?.Count != 2)
            problems.Add("a column misses a chip");
        if (rows.Count != members.Count)
            problems.Add($"{rows.Count} rows for {members.Count} members");
        return (problems, [.. starts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key} {pair.Value.Min(start => start.X):0.#}")]);
    }

    /// <summary>The rows of the Mods tab, in their order.</summary>
    private static List<Border> MemberRows(Control page)
        => [.. page.GetVisualDescendants().OfType<Border>().Where(border => border.DataContext is PackMemberItem && border.GetVisualParent() is ContentPresenter)];

    /// <summary>The cell of <paramref name="row"/> in the column of the instance (2) or of the newer release (3).</summary>
    private static Control Cell(Border row, int column)
        => row.GetVisualDescendants().OfType<Control>().First(control => control.Classes.Contains("member-cell") && Grid.GetColumn(control) == column);

    /// <summary>The one visible chip button whose tooltip is <paramref name="tip"/>, or fails.</summary>
    private static Button ChipButton(Control page, string tip)
        => page.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && button.Classes.Contains("chip-button") && ToolTip.GetTip(button) as string == tip);

    /// <summary>The one visible chip whose tooltip is <paramref name="tip"/>, or fails.</summary>
    private static Border Chip(Control page, string tip)
        => page.GetVisualDescendants().OfType<Border>().Single(border => border.IsEffectivelyVisible && border.Classes.Contains("chip") && ToolTip.GetTip(border) as string == tip);

    /// <summary>The one visible text of a chip.</summary>
    private static TextBlock ChipText(Control chip)
        => chip.GetVisualDescendants().OfType<TextBlock>().Single(block => block.IsEffectivelyVisible);

    /// <summary>The one visible icon of a chip.</summary>
    private static Avalonia.Controls.Shapes.Path Icon(Control chip)
        => chip.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(path => path.IsEffectivelyVisible);

    /// <summary>The box that <paramref name="visual"/> draws in, with its render transform, in the coordinates of <paramref name="to"/>.</summary>
    private static Rect Drawn(Visual visual, Visual to)
        => new Rect(visual.Bounds.Size).TransformToAABB(visual.TransformToVisual(to)!.Value);

    /// <summary>The visible chips and chip buttons inside <paramref name="container"/>, in their order.</summary>
    private static IEnumerable<Control> Chips(Visual container)
        => container.GetVisualDescendants().OfType<Control>()
            .Where(control => control.IsEffectivelyVisible && (control is Border border && border.Classes.Contains("chip") || control is Button button && button.Classes.Contains("chip-button")));

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

    /// <summary>Renders the page in one window that takes each width of <paramref name="widths"/> in turn, and reads it at the last one.</summary>
    private static Task<List<string>> RenderAsync(ViewModelHarness harness, Func<Control> createPage, IReadOnlyList<double> widths, Func<Control, List<string>> read) =>
        HeadlessApp.RunAsync(harness, () =>
        {
            var page = createPage();
            page.DataContext = harness.ViewModel;
            Grid.SetColumn(page, 1);
            var body = new Grid { ColumnDefinitions = new ColumnDefinitions($"{PageBodyPanel.NavigationRailWidth},*"), Children = { page } };
            var window = new Window { Width = widths[0], Height = 832, Content = body, DataContext = harness.ViewModel };
            window.Show();
            window.UpdateLayout();
            try
            {
                // a container query changes the styles during a layout pass, so a new width takes a few passes to settle, as frames do in the app
                foreach (var width in widths.Skip(1))
                {
                    window.Width = width;
                    for (var pass = 0; pass < 3; pass++)
                    {
                        Dispatcher.UIThread.RunJobs();
                        window.UpdateLayout();
                    }
                }

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
