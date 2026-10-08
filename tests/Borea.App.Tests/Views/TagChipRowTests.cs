using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Borea.App.Views;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class TagChipRowTests
{
    [Fact]
    public async Task UpToSixTagsThatFit_AllShow_WithoutTheCountChip()
    {
        var seen = await ShowAsync(["Parts", "Gameplay", "Audio", "Weapons", "Physics", "Tools"], width: 2000);

        Assert.Equal(["Parts", "Gameplay", "Audio", "Weapons", "Physics", "Tools"], seen.Shown);
        Assert.Null(seen.More);
        Assert.True(seen.InsideRow);
    }

    [Fact]
    public async Task MoreThanSixTags_ShowSix_AndTheCountChipNamesTheRest()
    {
        var seen = await ShowAsync(["Parts", "Gameplay", "Audio", "Weapons", "Physics", "Tools", "Visual", "Library"], width: 2000);

        Assert.Equal(["Parts", "Gameplay", "Audio", "Weapons", "Physics", "Tools"], seen.Shown);
        Assert.Equal("+2", seen.More);
        Assert.Equal("Visual, Library", seen.MoreTip);
        Assert.True(seen.InsideRow);
    }

    [Fact]
    public async Task TagsThatDoNotFit_GiveWayToTheCountChip_AndNoChipIsCut()
    {
        string[] tags = ["Parts", "Gameplay", "User Interface", "Audio", "Weapons", "Physics"];
        var seen = await ShowAsync(tags, width: 220);

        Assert.InRange(seen.Shown.Count, 1, 5);
        Assert.Equal(tags[..seen.Shown.Count], seen.Shown);
        Assert.Equal($"+{tags.Length - seen.Shown.Count}", seen.More);
        Assert.Equal(string.Join(", ", tags[seen.Shown.Count..]), seen.MoreTip);
        Assert.True(seen.InsideRow);
    }

    [Fact]
    public async Task NoTags_TakeNoHeight()
    {
        var seen = await ShowAsync([], width: 400);

        Assert.Empty(seen.Shown);
        Assert.Null(seen.More);
        Assert.Equal(0, seen.Height);
    }

    [Fact]
    public async Task Lead_ComesFirst_AndCountsAsOneOfTheSix()
    {
        var seen = await ShowAsync(["Parts", "Gameplay", "Audio", "Weapons", "Physics", "Tools"], width: 2000, lead: "Installed");

        Assert.Equal(["Installed", "Parts", "Gameplay", "Audio", "Weapons", "Physics"], seen.Shown);
        Assert.Equal("+1", seen.More);
        Assert.Equal("Tools", seen.MoreTip);
        Assert.True(seen.LeadPositive);
        Assert.True(seen.InsideRow);
    }

    [Fact]
    public async Task ManyTagsWithALead_KeepTheLeadFirst_AndTheCountChipCountsTheTagsThatDoNotFit()
    {
        string[] tags = ["Parts", "Gameplay", "User Interface", "Audio", "Weapons", "Physics", "Tools", "Visual"];
        var seen = await ShowAsync(tags, width: 260, lead: "Installed");

        Assert.Equal("Installed", seen.Shown[0]);
        var shownTags = seen.Shown.Count - 1;
        Assert.InRange(shownTags, 1, 4);
        Assert.Equal(tags[..shownTags], seen.Shown[1..]);
        Assert.Equal($"+{tags.Length - shownTags}", seen.More);
        Assert.Equal(string.Join(", ", tags[shownTags..]), seen.MoreTip);
        Assert.True(seen.InsideRow);
    }

    [Fact]
    public async Task ANarrowingRowWithALead_GivesUpTheTagsThenTheCountChipThenTheLead_AndNoChipIsCut()
    {
        string[] tags = ["Parts", "Gameplay", "Audio"];
        var steps = await NarrowAsync(tags, "Installed", Enumerable.Range(0, 111).Select(step => 220 - 2.0 * step));

        foreach (var seen in steps)
        {
            Assert.True(seen.InsideRow, $"A chip is cut at {seen.Width} px.");
            if (seen.Shown.Count > 0)
                Assert.Equal("Installed", seen.Shown[0]);
            var shownTags = Math.Max(0, seen.Shown.Count - 1);
            Assert.Equal(tags[..shownTags], seen.Shown.Skip(1));
            if (seen.More is not null)
                Assert.Equal($"+{tags.Length - shownTags}", seen.More);
        }

        // a narrower row never shows more chips than a wider one
        Assert.All(steps.Zip(steps.Skip(1)), pair => Assert.True(pair.Second.Shown.Count <= pair.First.Shown.Count));
        Assert.Contains(steps, seen => seen.Shown.Count == 2 && seen.More == "+2");
        Assert.Contains(steps, seen => seen.Shown is ["Installed"] && seen.More == "+3");
        Assert.Contains(steps, seen => seen.Shown is ["Installed"] && seen.More is null);
        Assert.Contains(steps, seen => seen.Shown.Count == 0 && seen.More is null);
    }

    [Fact]
    public async Task ALeadWithoutTags_ShowsAlone()
    {
        var seen = await ShowAsync([], width: 400, lead: "Installed");

        Assert.Equal(["Installed"], seen.Shown);
        Assert.Null(seen.More);
    }

    [Fact]
    public async Task ALeadThatIsOff_LeavesTheTagsAsTheyAre()
    {
        var seen = await ShowAsync(["Parts", "Gameplay"], width: 2000, lead: "Installed", showsLead: false);

        Assert.Equal(["Parts", "Gameplay"], seen.Shown);
        Assert.Null(seen.More);
    }

    /// <summary>Shows the row alone in a window of the width and reads the chips that have room, and whether each lies whole inside the row.</summary>
    private static Task<(List<string> Shown, string? More, string? MoreTip, bool InsideRow, double Height, bool LeadPositive)> ShowAsync(string[] tags, double width, string? lead = null, bool showsLead = true) =>
        HeadlessApp.RunAsync(() =>
        {
            var row = new TagChipRow { Tags = tags, Lead = lead, ShowsLead = lead is not null && showsLead, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var window = new Window { Width = width, Height = 200, Content = row };
            window.Show();
            window.UpdateLayout();

            var withRoom = row.Children.OfType<Border>().Where(chip => chip.Bounds.Width > 0).OrderBy(chip => chip.Bounds.X).ToList();
            var shown = withRoom.Where(chip => chip != row.MoreChip).Select(chip => ((TextBlock)chip.Child!).Text!).ToList();
            var more = withRoom.Contains(row.MoreChip) ? ((TextBlock)row.MoreChip.Child!).Text : null;
            var tip = more is null ? null : ToolTip.GetTip(row.MoreChip) as string;
            var inside = withRoom.All(chip => chip.Bounds.Right <= row.Bounds.Width + 0.5 && chip.Bounds.X >= 0);
            var result = (shown, more, tip, inside, row.Bounds.Height, row.LeadChip.Classes.Contains("positive"));
            window.Close();
            return Task.FromResult(result);
        });

    /// <summary>Shows the row with a lead in a host of each width in turn, and reads the chips that have room at each width.</summary>
    private static Task<List<(double Width, List<string> Shown, string? More, bool InsideRow)>> NarrowAsync(string[] tags, string lead, IEnumerable<double> widths) =>
        HeadlessApp.RunAsync(() =>
        {
            var row = new TagChipRow { Tags = tags, Lead = lead, ShowsLead = true, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var host = new Border { Child = row, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var window = new Window { Width = 400, Height = 200, Content = host };
            window.Show();
            var steps = new List<(double, List<string>, string?, bool)>();
            foreach (var width in widths)
            {
                host.Width = width;
                window.UpdateLayout();
                var withRoom = row.Children.OfType<Border>().Where(chip => chip.Bounds.Width > 0).OrderBy(chip => chip.Bounds.X).ToList();
                var shown = withRoom.Where(chip => chip != row.MoreChip).Select(chip => ((TextBlock)chip.Child!).Text!).ToList();
                var more = withRoom.Contains(row.MoreChip) ? ((TextBlock)row.MoreChip.Child!).Text : null;
                var inside = withRoom.All(chip => chip.Bounds.Right <= Math.Min(row.Bounds.Width, width) + 0.5 && chip.Bounds.X >= 0);
                steps.Add((width, shown, more, inside));
            }

            window.Close();
            return Task.FromResult(steps);
        });
}
