using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class ReleaseNotesModalTests
{
    [Fact]
    public async Task TheWheelOverTheNotes_EasesTheNotesToWhereTheNotchesAsked()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: LongNotesAt("v999.0.0"));
        var viewModel = harness.ViewModel;
        await viewModel.WhenUpdateCheckedAsync();
        viewModel.OpenReleaseNotesCommand.Execute(null);

        var seen = await HeadlessApp.RunAsync(harness, async () =>
        {
            var modal = new ReleaseNotesModal();
            var window = new Window { Width = 1280, Height = 832, DataContext = viewModel, Content = modal };
            window.Show();
            window.UpdateLayout();
            var viewer = modal.GetVisualDescendants().OfType<ScrollViewer>().Single();
            var text = viewer.GetVisualDescendants().OfType<SelectableTextBlock>().First();
            var before = viewer.Offset.Y;
            var target = before + 3 * SmoothScroll.Notch;

            window.MouseWheel(text.TranslatePoint(new Point(8, 8), window)!.Value, new Vector(0, -3));
            var onTheWheel = viewer.Offset.Y;
            await HeadlessApp.FramesUntilAsync(() => viewer.Offset.Y == target);
            var result = (Smooth: SmoothScroll.GetIsEnabled(viewer), Maximum: viewer.ScrollBarMaximum.Y, before, target, onTheWheel, After: viewer.Offset.Y);
            window.Close();
            return result;
        });

        Assert.True(seen.Smooth);
        Assert.True(seen.Maximum > seen.target, "The notes must be long enough to scroll, or the test proves nothing.");
        Assert.True(seen.onTheWheel < seen.target, "The offset must still be on its way rather than already there.");
        Assert.Equal(seen.target, seen.After);
    }

    /// <summary>Answers the release check with one release at <paramref name="tag"/> whose notes are many times longer than the modal.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage?> LongNotesAt(string tag)
    {
        var notes = string.Join("\n", Enumerable.Range(1, 60).Select(line => $"- Change {line} of the release, written long enough to wrap in the window of the notes."));
        var release = $$"""[{"tag_name":"{{tag}}","html_url":"https://github.com/KSAModding/Borea/releases/tag/{{tag}}","draft":false,"prerelease":false,"published_at":"2026-09-13T12:00:00Z","body":{{JsonSerializer.Serialize(notes)}}}]""";
        return request => request.RequestUri?.Host != "api.github.com"
            ? null
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(release, Encoding.UTF8, "application/json") };
    }
}
