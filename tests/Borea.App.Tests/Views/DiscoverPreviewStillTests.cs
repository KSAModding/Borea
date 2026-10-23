using System.Collections.Concurrent;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;
using Borea.App.Views.Preview;
using Borea.Composition;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.Preview;
using Borea.Preview.Scene;
using static Borea.App.Tests.ViewModels.UserContentFixtures;
using static Borea.App.Tests.ViewModels.UserContentViewModelTests;

namespace Borea.App.Tests.Views;

/// <summary>The still of a vehicle preview on its Discover row (RFC 0085).</summary>
[Collection(HeadlessCollection.Name)]
public sealed class DiscoverPreviewStillTests
{
    [Fact]
    public async Task AVehicleRowWithAStoredStill_ShowsIt_AndListingVehiclesNeitherPreparesNorPlansNorDownloads()
    {
        var plans = new ConcurrentQueue<InstallPlanningRequest>();
        using var harness = await VehicleHarnessAsync(seed: WithGameAsync, wrapInstallPlanner: inner => new RecordingPlanner(inner, plans));
        var viewModel = harness.ViewModel;
        var service = new FakeVehiclePreview();
        viewModel.VehiclePreviewServiceOf = _ => service;
        var build = PreviewGameBuild.Of(harness.Services.InstalledVersion.GetInstalledVersion());
        Assert.NotNull(build);
        var sha256 = (await harness.Services.UserContent.GetReleaseAsync("ExampleRover", ModVersion.Parse("1.1.0")))!.Download.Sha256!;
        var png = await HeadlessApp.RunAsync(() =>
            Task.FromResult(new AvaloniaPngEncoder().Encode(VehiclePreviewViewModel.RenderStill(PreviewTestScenes.StackedBoxes(), CancellationToken.None))));
        harness.Services.PreviewThumbnails.Store(sha256, build, "scene", png);

        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);
        var row = Assert.Single(viewModel.DiscoverItems);
        Assert.NotNull(row.PreviewStillPath);
        Assert.Equal(harness.Services.PreviewThumbnails.TryGetPath(sha256, build), row.PreviewStillPath);

        var shown = await HeadlessApp.RunAsync(harness, async () =>
        {
            var page = new DiscoverPage { DataContext = viewModel };
            var window = new Window { Width = 1000, Height = 800, Content = page, DataContext = viewModel };
            window.Show();
            window.UpdateLayout();
            var still = page.GetVisualDescendants().OfType<PreviewStillView>().Single();
            await still.Loading;
            window.UpdateLayout();

            var background = ((ISolidColorBrush)still.Background!).Color;
            var area = ContentPagePreviewTests.BoundsIn(window, still);
            var changed = 0;
            var total = 0;
            ContentPagePreviewTests.ReadFrame(window, (x, y, color) =>
            {
                if (!area.Contains(new Avalonia.Point(x + 0.5, y + 0.5)))
                    return;

                total++;
                if (Math.Max(Math.Abs(color.R - background.R), Math.Max(Math.Abs(color.G - background.G), Math.Abs(color.B - background.B))) > 16)
                    changed++;
            }, shotName: "discover-vehicle-row");
            var result = (
                still.HasStill,
                IconVisible: still.Child!.IsVisible,
                Changed: changed,
                Total: total,
                Radii: (still.CornerRadius.TopLeft, ((ListingImageView)still.Child).CornerRadius.TopLeft),
                Clipped: still.Clip is not null);
            window.Close();
            return result;
        });

        Assert.True(shown.HasStill);
        Assert.False(shown.IconVisible);
        Assert.Equal((7.5, 7.5), shown.Radii);
        Assert.True(shown.Clipped);
        Assert.True(shown.Changed > shown.Total / 10, $"Only {shown.Changed} of {shown.Total} pixels of the thumbnail show the still.");
        Assert.Empty(service.Prepares);
        Assert.Empty(service.Loads);
        Assert.Empty(plans);
        Assert.DoesNotContain(harness.Requests, uri => uri.AbsoluteUri == RoverUrl);
    }

    /// <summary>Gives the services a game folder whose KSA.dll has a build, so a still can be stored for it.</summary>
    private static async Task WithGameAsync(BoreaServices services)
    {
        var game = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Game")).FullName;
        File.Copy(Path.Combine(AppContext.BaseDirectory, "GameVersionFixture.dll"), Path.Combine(game, "KSA.dll"));
        await services.SettingsRepository.SaveAsync(services.Settings.WithGameDirectory(game));
    }

    private sealed class RecordingPlanner(IInstallPlanner inner, ConcurrentQueue<InstallPlanningRequest> requests) : IInstallPlanner
    {
        public Task<InstallPlan> PlanAsync(InstallPlanningRequest request, CancellationToken cancellationToken = default)
        {
            requests.Enqueue(request);
            return inner.PlanAsync(request, cancellationToken);
        }
    }
}
