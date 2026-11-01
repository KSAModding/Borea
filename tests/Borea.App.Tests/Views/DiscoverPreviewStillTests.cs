using System.Collections.Concurrent;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;
using Borea.App.Views.Preview;
using Borea.Composition;
using Borea.Core.Instances;
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

    /// <summary>A host that keeps its archives outside GitHub and serves the release URL of the rover.</summary>
    private const string HostWithoutACount = """{ "files": { "kind": "share", "name": "files.example", "api": "https://files.example/api/", "data": "https://data.example/", "downloads": ["https://example.test/"], "key": "x", "types": ["vehicle"] } }""";

    /// <summary>A host with GitHub storage, whose release assets count every download, under the same URL.</summary>
    private const string HostWithACount = """{ "files": { "kind": "share", "name": "files.example", "api": "https://files.example/api/", "storage": "owner/repo", "downloads": ["https://example.test/"], "types": ["vehicle"] } }""";

    private static readonly ResolvedPlaceholder Box = new(Matrix4x4.Identity, Vector3.One, "Wing", PlaceholderReason.ModNotLoaded, "Wings");

    private static readonly VehiclePreviewMissingMod MissingWings = new("Wings", null, null, null, CanFetch: false);

    [Fact]
    public async Task AVehicleRowWithoutAnIcon_FetchesOnceFromAHostWithoutACount_AndDrawsItsStill()
    {
        var service = new FakeVehiclePreview { PlanState = VehiclePreviewPlanState.NeedsVehicleFile, Vehicle = VehiclePreviewViewModelTests.TriangleVehicle(), Files = VehiclePreviewViewModelTests.TriangleFiles };
        using var harness = await StillHarnessAsync(service, HostWithoutACount);

        var shown = await ShowVehiclesAsync(harness, async (viewModel, page) =>
        {
            var still = page.GetVisualDescendants().OfType<PreviewStillView>().Single();
            await still.Loading;

            // a second ask of the same version costs nothing
            viewModel.PreviewStills.Refresh();
            await viewModel.PreviewStills.Idle;
            return still.HasStill;
        });

        Assert.True(shown);
        Assert.Equal(["files"], service.ShareFetches);
        Assert.Equal((false, false), Assert.Single(service.Loads));
        Assert.DoesNotContain(harness.Requests, uri => uri.AbsoluteUri == RoverUrl);
    }

    [Theory]
    [InlineData(HostWithACount)]
    [InlineData("""{ "other": { "kind": "share", "name": "other.example", "api": "https://other.example/api/", "data": "https://data.example/", "downloads": ["https://other.example/"], "key": "x", "types": ["vehicle"] } }""")]
    public async Task AVehicleRowOfAnotherHost_FetchesNothing_AndKeepsTheNormalIcon(string hosts)
    {
        var service = new FakeVehiclePreview { PlanState = VehiclePreviewPlanState.NeedsVehicleFile };
        using var harness = await StillHarnessAsync(service, hosts);

        var path = await ShowVehiclesAsync(harness, (viewModel, _) => Task.FromResult(viewModel.DiscoverItems.Single().PreviewStillPath));

        Assert.Null(path);
        Assert.Empty(service.ShareFetches);
        Assert.Empty(service.Loads);
    }

    [Fact]
    public async Task AVehicleFileAboveTheLimit_IsNotFetched()
    {
        var service = new FakeVehiclePreview { PlanState = VehiclePreviewPlanState.NeedsVehicleFile };
        using var harness = await StillHarnessAsync(service, HostWithoutACount, entry => entry.Replace("\"size\": " + RoverArchive().Length, "\"size\": " + (VehiclePreviewViewModel.AutoFetchVehicleBytes + 1), StringComparison.Ordinal));

        await ShowVehiclesAsync(harness, (_, _) => Task.FromResult(true));

        Assert.Empty(service.ShareFetches);
    }

    [Fact]
    public async Task TheSetting_StopsTheFetchOfARow_AndTurningItOnLetsTheRowAskAgain()
    {
        var service = new FakeVehiclePreview { PlanState = VehiclePreviewPlanState.NeedsVehicleFile, Vehicle = VehiclePreviewViewModelTests.TriangleVehicle(), Files = VehiclePreviewViewModelTests.TriangleFiles };
        using var harness = await StillHarnessAsync(service, HostWithoutACount);
        harness.ViewModel.LoadImagesFromAuthorHosts = false;

        var (off, on) = await ShowVehiclesAsync(harness, async (viewModel, _) =>
        {
            var whileOff = service.ShareFetches.Count;
            viewModel.LoadImagesFromAuthorHosts = true;
            await viewModel.PreviewStills.Idle;
            return (whileOff, service.ShareFetches.Count);
        });

        Assert.Equal((0, 1), (off, on));
    }

    [Fact]
    public async Task TheSetting_TurnedOffDuringAFetch_StopsIt_AndStartsNoOther()
    {
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeVehiclePreview
        {
            PlanState = VehiclePreviewPlanState.NeedsVehicleFile,
            HoldShareFetch = token =>
            {
                token.Register(() => stopped.TrySetCanceled(token));
                started.TrySetResult();
                return stopped.Task;
            },
        };
        using var harness = await StillHarnessAsync(service, HostWithoutACount);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);

        var cancelled = await HeadlessApp.RunAsync(harness, async () =>
        {
            var page = new DiscoverPage { DataContext = viewModel };
            var window = new Window { Width = 1000, Height = 800, Content = page, DataContext = viewModel };
            window.Show();
            window.UpdateLayout();
            await started.Task;

            viewModel.LoadImagesFromAuthorHosts = false;
            var stoppedByTheSetting = stopped.Task.IsCanceled;
            stopped.TrySetCanceled();
            await viewModel.PreviewStills.Idle;
            window.Close();
            return stoppedByTheSetting;
        });

        Assert.True(cancelled);
        Assert.Single(service.ShareFetches);
    }

    [Fact]
    public async Task AFailedFetch_IsNotTriedAgain_AndMakesThePageOfTheVehicleWaitForAClick()
    {
        var service = new FakeVehiclePreview { PlanState = VehiclePreviewPlanState.NeedsVehicleFile, ShareFetchFailure = new DownloadFailedException("no") };
        using var harness = await StillHarnessAsync(service, HostWithoutACount);
        var viewModel = harness.ViewModel;
        viewModel.VehiclePreviewAutoFetchDelay = TimeSpan.Zero;

        await ShowVehiclesAsync(harness, async (_, _) =>
        {
            viewModel.RefreshPreviewStills();
            await viewModel.PreviewStills.Idle;
            return true;
        });
        await viewModel.OpenContentAsync(viewModel.DiscoverItems.Single());
        await viewModel.WhenVehiclePreviewLoadedAsync();

        Assert.Single(service.ShareFetches);
        Assert.Equal(VehiclePreviewState.Idle, viewModel.VehiclePreview!.State);
        Assert.Empty(service.Loads);
    }

    [Fact]
    public async Task LeavingDiscover_StopsTheDrawThatRuns_AsksForNothingOnOtherPages_AndTheRowAsksAgainOnReturn()
    {
        var held = new TaskCompletionSource<VehiclePreviewResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeVehiclePreview
        {
            Vehicle = VehiclePreviewViewModelTests.TriangleVehicle(),
            Files = VehiclePreviewViewModelTests.TriangleFiles,
            HoldLoad = _ => started.TrySetResult() ? held.Task : null,
        };
        using var harness = await StillHarnessAsync(service, HostWithoutACount);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);

        var (stopped, asksWhileHidden, path) = await HeadlessApp.RunAsync(harness, async () =>
        {
            var page = new DiscoverPage { DataContext = viewModel };
            var window = new Window { Width = 1000, Height = 800, Content = page, DataContext = viewModel };
            window.Show();
            window.UpdateLayout();
            await started.Task;

            viewModel.CurrentWindowDiscover = false;
            page.IsVisible = false;
            window.UpdateLayout();
            var token = Assert.Single(service.LoadTokens).Token;
            var stoppedOnLeave = token.IsCancellationRequested;
            held.TrySetCanceled();
            await viewModel.PreviewStills.Idle;

            var asked = service.Prepares.Count;
            viewModel.RefreshPreviewStills();
            await viewModel.PreviewStills.Idle;
            var askedWhileHidden = service.Prepares.Count - asked;

            viewModel.CurrentWindowDiscover = true;
            page.IsVisible = true;
            window.UpdateLayout();
            await viewModel.PreviewStills.Idle;
            var result = viewModel.DiscoverItems.Single().PreviewStillPath;
            window.Close();
            return (stoppedOnLeave, askedWhileHidden, result);
        });

        Assert.True(stopped);
        Assert.Equal(0, asksWhileHidden);
        Assert.NotNull(path);
    }

    [Fact]
    public async Task TheSetting_MakesThePageOfASmallVehicleWaitForAClick()
    {
        var service = new FakeVehiclePreview { PlanState = VehiclePreviewPlanState.NeedsVehicleFile };
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        viewModel.VehiclePreviewServiceOf = _ => service;
        viewModel.VehiclePreviewAutoFetchDelay = TimeSpan.Zero;
        viewModel.LoadImagesFromAuthorHosts = false;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);

        await viewModel.OpenContentAsync(viewModel.DiscoverItems.Single());
        await viewModel.WhenVehiclePreviewLoadedAsync();

        Assert.Equal(VehiclePreviewState.Idle, viewModel.VehiclePreview!.State);
        Assert.Empty(service.Loads);
    }

    [Fact]
    public async Task ARowWithAListingIcon_ShowsTheIcon_AndAsksForNothing()
    {
        var service = new FakeVehiclePreview();
        using var harness = await StillHarnessAsync(service, HostWithoutACount, ImageViewModelTests.WithImages("ExampleRover", $$"""{ "icon": {{ImageViewModelTests.Icon("https://images.example/rover.png")}} }"""));
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        var sha256 = (await harness.Services.UserContent.GetReleaseAsync("ExampleRover", ModVersion.Parse("1.1.0")))!.Download.Sha256!;
        var build = PreviewGameBuild.Of(harness.Services.InstalledVersion.GetInstalledVersion())!;
        harness.Services.PreviewThumbnails.Store(sha256, build, "scene", [1]);
        await harness.ViewModel.RefreshCompatibilityAsync();

        var path = await ShowVehiclesAsync(harness, (viewModel, _) => Task.FromResult(viewModel.DiscoverItems.Single().PreviewStillPath));

        Assert.Null(path);
        Assert.Empty(service.Prepares);
    }

    [Fact]
    public async Task MissingMods_DrawTheRestWithANote_AndTheStillIsDrawnAgainWithoutItOnceTheModsAreThere()
    {
        var service = new FakeVehiclePreview { Vehicle = VehiclePreviewViewModelTests.TriangleVehicle(Box), Files = VehiclePreviewViewModelTests.TriangleFiles, Missing = [MissingWings] };
        using var harness = await StillHarnessAsync(service, HostWithoutACount);
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await harness.ViewModel.LoadAsync();

        var (before, noteText, after, noteShown) = await ShowVehiclesAsync(harness, async (viewModel, page) =>
        {
            var row = viewModel.DiscoverItems.Single();
            var still = page.GetVisualDescendants().OfType<PreviewStillView>().Single();
            await still.Loading;
            page.UpdateLayout();
            var missingNow = row.PreviewStillMissingMods;
            var text = page.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Classes.Contains("still-note")).Text;

            service.Missing = [];
            service.Vehicle = VehiclePreviewViewModelTests.TriangleVehicle();
            service.SceneKey = "withwings";
            var fresh = await harness.Services.Instances.GetByIdAsync(instance.InstanceId);
            var release = new ModVersionMetadata(1, "Wings", ModVersion.Parse("1.0.0"), ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, "2026.9.7.5402", 5402, new DownloadInfo("https://example.invalid/wings.zip", new string('A', 64), null, "application/zip"), null, []);
            fresh!.AddMod(new InstalledMod("Wings", release.Version, InstallReason.Manual, DateTimeOffset.UtcNow, release));
            await harness.Services.Instances.SaveAsync(fresh);
            await viewModel.LoadAsync();
            await viewModel.PreviewStills.Idle;
            await still.Loading;
            page.UpdateLayout();
            return (missingNow, text, row.PreviewStillMissingMods, page.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Classes.Contains("still-note")).IsVisible);
        });

        Assert.True(before);
        Assert.Equal(harness.Localization.DiscoverStillMissingMods, noteText);
        Assert.False(after);
        Assert.False(noteShown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AModThatArrivesOutsideTheActiveInstance_DrawsTheStillAgainOnReturnToDiscover(bool onlyMissingParts)
    {
        var service = new FakeVehiclePreview { Vehicle = onlyMissingParts ? new ResolvedVehicle([], [], [], [Box], [], 1, 0) : VehiclePreviewViewModelTests.TriangleVehicle(Box), Files = VehiclePreviewViewModelTests.TriangleFiles, Missing = [MissingWings] };
        using var harness = await StillHarnessAsync(service, HostWithoutACount);

        var (before, after, note) = await ShowVehiclesAsync(harness, async (viewModel, page) =>
        {
            var row = viewModel.DiscoverItems.Single();
            var hadPath = row.PreviewStillPath is not null;

            service.Missing = [];
            service.Vehicle = VehiclePreviewViewModelTests.TriangleVehicle();
            service.SceneKey = "withwings";
            viewModel.CurrentWindowDiscover = false;
            viewModel.CurrentWindowDiscover = true;
            await viewModel.PreviewStills.Idle;
            return (hadPath, row.PreviewStillPath is not null, row.PreviewStillMissingMods);
        });

        Assert.Equal(!onlyMissingParts, before);
        Assert.True(after);
        Assert.False(note);
    }

    [Fact]
    public async Task AVehicleOfOnlyMissingParts_KeepsTheNormalIcon()
    {
        var onlyBoxes = new ResolvedVehicle([], [], [], [Box], [], 1, 0);
        var service = new FakeVehiclePreview { Vehicle = onlyBoxes, Missing = [MissingWings] };
        using var harness = await StillHarnessAsync(service, HostWithoutACount);

        var path = await ShowVehiclesAsync(harness, (viewModel, _) => Task.FromResult(viewModel.DiscoverItems.Single().PreviewStillPath));

        Assert.Null(path);
        Assert.Single(service.Loads);
    }

    [Fact]
    public async Task OnlyRowsNearTheWindow_AskForAStill_AndARowInViewAsksFirst()
    {
        var service = new FakeVehiclePreview { PlanState = VehiclePreviewPlanState.Failed };
        using var harness = await StillHarnessAsync(service, HostWithoutACount);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);
        var release = viewModel.DiscoverItems.Single().LatestInChannel!;

        var (asked, inView) = await HeadlessApp.RunAsync(harness, async () =>
        {
            viewModel.DiscoverItems.Clear();
            for (var number = 0; number < 60; number++)
            {
                var id = $"Vehicle{number:D2}";
                var listing = new ModMetadata(1, id, "index", id, ["Kitten"], "A vehicle.", "MIT", new Dictionary<string, string>(), "2026.8.19.5261", type: ContentType.Vehicle);
                var numbered = new ModVersionMetadata(1, id, release.Version, ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, release.GameMin, release.GameMinRevision, new DownloadInfo(release.Download.Url, $"{number:D64}", release.Download.SizeBytes, "application/zip"), null, [], ContentType.Vehicle, install: new InstallInfo(null, derived: false, folder: id));
                viewModel.DiscoverItems.Add(new DiscoverItem(viewModel, listing) { LatestInChannel = numbered });
            }

            var page = new DiscoverPage { DataContext = viewModel };
            var window = new Window { Width = 1000, Height = 800, Content = page, DataContext = viewModel };
            window.Show();
            window.UpdateLayout();
            await viewModel.PreviewStills.Idle;
            var scroller = page.GetVisualDescendants().OfType<ScrollViewer>().First();
            var visible = page.GetVisualDescendants().OfType<PreviewStillView>()
                .Where(view => view.TranslatePoint(default, scroller) is { } at && at.Y + view.Bounds.Height > 0 && at.Y < scroller.Viewport.Height)
                .Select(view => ((DiscoverItem)view.DataContext!).ModId)
                .ToList();
            window.Close();
            return (service.Prepares.Select(prepared => prepared.ModId).ToList(), visible);
        });

        Assert.Contains(asked[0], inView);
        Assert.Equal(asked.Count, asked.Distinct().Count());
        Assert.InRange(asked.Count, inView.Count, inView.Count * 2 + 2);
        Assert.DoesNotContain("Vehicle59", asked);
    }

    [Fact]
    public async Task AnOperationOnTheFiles_StopsTheDrawThatRuns_WaitsForIt_AndStartsNoOther()
    {
        var held = new TaskCompletionSource<VehiclePreviewResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeVehiclePreview
        {
            Vehicle = VehiclePreviewViewModelTests.TriangleVehicle(),
            Files = VehiclePreviewViewModelTests.TriangleFiles,
            HoldLoad = _ =>
            {
                started.TrySetResult();
                return held.Task;
            },
        };
        using var harness = await StillHarnessAsync(service, HostWithoutACount);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);

        var (stopped, endedBefore) = await HeadlessApp.RunAsync(harness, async () =>
        {
            var page = new DiscoverPage { DataContext = viewModel };
            var window = new Window { Width = 1000, Height = 800, Content = page, DataContext = viewModel };
            window.Show();
            window.UpdateLayout();
            await started.Task;

            var reads = viewModel.WhenVehiclePreviewReadsEndedAsync();
            var token = Assert.Single(service.LoadTokens).Token;
            var ended = reads.IsCompleted;
            held.SetResult(service.Ready(new FakeAssets(service.Files)));
            await reads;
            window.Close();
            return (token.IsCancellationRequested, ended);
        });

        Assert.True(stopped);
        Assert.False(endedBefore);
        Assert.Single(service.Loads);
    }

    /// <summary>A harness whose only vehicle row can draw, over the given share hosts and a game whose build the fake result names.</summary>
    private static async Task<ViewModelHarness> StillHarnessAsync(FakeVehiclePreview service, string hosts, Func<string, string>? editEntry = null)
    {
        var harness = await VehicleHarnessAsync(editSnapshot: ViewModelHarness.ShareHosts(hosts), seed: WithGameAsync, editEntry: editEntry);
        var viewModel = harness.ViewModel;
        viewModel.VehiclePreviewServiceOf = _ => service;
        viewModel.VehiclePreviewEncoder = new AvaloniaPngEncoder();
        viewModel.CurrentWindowDiscover = true;
        var sha256 = (await harness.Services.UserContent.GetReleaseAsync("ExampleRover", ModVersion.Parse("1.1.0")))!.Download.Sha256!;
        service.ResultNames = (sha256.ToLowerInvariant(), PreviewGameBuild.Of(harness.Services.InstalledVersion.GetInstalledVersion())!);
        return harness;
    }

    /// <summary>Shows the Vehicles tab in a window, waits until the queue is idle and runs the body in the same dispatch.</summary>
    private static async Task<T> ShowVehiclesAsync<T>(ViewModelHarness harness, Func<MainViewModel, DiscoverPage, Task<T>> body)
    {
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);
        return await HeadlessApp.RunAsync(harness, async () =>
        {
            var page = new DiscoverPage { DataContext = viewModel };
            var window = new Window { Width = 1000, Height = 800, Content = page, DataContext = viewModel };
            window.Show();
            window.UpdateLayout();
            await viewModel.PreviewStills.Idle;
            var result = await body(viewModel, page);
            window.Close();
            return result;
        });
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
