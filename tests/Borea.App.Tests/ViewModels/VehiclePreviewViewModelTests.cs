using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Borea.App.Localization;
using Borea.App.ViewModels;
using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Preview;
using Borea.Preview.Loading;
using Borea.Preview.Rendering;
using Borea.Preview.Scene;
using Borea.Preview.Tests.Gltf;
using Borea.Preview.Textures;
using static Borea.App.Tests.ViewModels.UserContentViewModelTests;

namespace Borea.App.Tests.ViewModels;

/// <summary>The 3D preview of a vehicle page, over a fake preview service.</summary>
public sealed class VehiclePreviewViewModelTests
{
    private static readonly LocalizationService Localization = new(CultureInfo.GetCultureInfo("en"));

    private static readonly ModVersionMetadata Rover = new(
        specVersion: 1,
        modId: "ExampleRover",
        version: ModVersion.Parse("1.1.0"),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
        gameMin: "2026.10.19.5638",
        gameMinRevision: 5638,
        download: new DownloadInfo("https://example.invalid/rover.zip", new string('a', 64), 2_500_000, "application/zip"),
        installSizeBytes: null,
        dependencies: []);

    [Fact]
    public async Task CanBuildLocally_BuildsWithoutAClick_AndFetchesNothing()
    {
        var service = new FakeVehiclePreview();
        using var preview = Start(service);
        await preview.Loading;

        Assert.Equal(VehiclePreviewState.Ready, preview.State);
        Assert.NotNull(preview.Scene);
        Assert.Equal([(false, false)], service.Loads);
        Assert.False(preview.HasMissingMods);
    }

    [Theory]
    [InlineData(2_500_000L)]
    [InlineData(VehiclePreviewViewModel.AutoFetchVehicleBytes)]
    public async Task NeedsASmallVehicleFile_FetchesOnlyTheVehicle_WithoutAClick(long size)
    {
        var service = new FakeVehiclePreview { PlanState = VehiclePreviewPlanState.NeedsVehicleFile };
        using var preview = Start(service, release: RoverOfSize(size), autoFetchDelay: TimeSpan.Zero);
        await preview.Loading;

        Assert.Equal([(true, false)], service.Loads);
        Assert.Equal(VehiclePreviewState.Ready, preview.State);
    }

    public static TheoryData<long?, string> LargeOrUnsizedFiles => new()
    {
        { VehiclePreviewViewModel.AutoFetchVehicleBytes + 1, Localization.FormatPreviewShowSize(MainViewModel.SizeText(VehiclePreviewViewModel.AutoFetchVehicleBytes + 1)) },
        { null, Localization.PreviewShow },
    };

    // The rows read the shared resource culture, so they are made when the test runs and not at discovery.
    [Theory]
    [MemberData(nameof(LargeOrUnsizedFiles), DisableDiscoveryEnumeration = true)]
    public async Task NeedsALargeOrUnsizedVehicleFile_LoadsNothingBeforeTheClick_AndTheClickFetchesOnlyTheVehicle(long? size, string showText)
    {
        var service = new FakeVehiclePreview { PlanState = VehiclePreviewPlanState.NeedsVehicleFile };
        using var preview = Start(service, release: RoverOfSize(size), autoFetchDelay: TimeSpan.Zero);
        await preview.Loading;

        Assert.Equal(VehiclePreviewState.Idle, preview.State);
        Assert.Empty(service.Loads);
        Assert.True(preview.ShowCommand.CanExecute(null));
        Assert.Equal(showText, preview.ShowText);

        await preview.ShowCommand.ExecuteAsync(null);

        Assert.Equal([(true, false)], service.Loads);
        Assert.Equal(VehiclePreviewState.Ready, preview.State);
    }

    [Fact]
    public async Task AStageThatArrivesAfterTheLoad_DoesNotTakeThePanelBackFromReady()
    {
        // without a context the stage reports run at once, so the late report below reaches the view model now
        SynchronizationContext.SetSynchronizationContext(null);
        var service = new FakeVehiclePreview();
        using var preview = Start(service);
        await preview.Loading;
        Assert.Equal(VehiclePreviewState.Ready, preview.State);

        service.LastProgress!.Report(VehiclePreviewStage.ReadingParts);

        Assert.Equal(VehiclePreviewState.Ready, preview.State);
    }

    [Fact]
    public async Task LeavingThePageDuringTheDelay_DownloadsNothing()
    {
        var service = new FakeVehiclePreview { PlanState = VehiclePreviewPlanState.NeedsVehicleFile };
        var preview = Start(service, autoFetchDelay: TimeSpan.FromMinutes(5));
        Assert.Equal(VehiclePreviewState.Fetching, preview.State);
        Assert.True(preview.CancelCommand.CanExecute(null));

        preview.Dispose();
        await preview.Loading.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(service.Loads);
    }

    [Fact]
    public async Task NeedsMods_NamesEachModAndTheSummedSize_AndOnlyTheModClickFetchesMods()
    {
        var service = new FakeVehiclePreview
        {
            Missing =
            [
                new VehiclePreviewMissingMod("Wings", ModVersion.Parse("1.0.0"), 3, 40L * 1024 * 1024, true),
                new VehiclePreviewMissingMod("Engines", ModVersion.Parse("2.0.0"), null, 150L * 1024 * 1024, true),
            ],
        };
        using var preview = Start(service);
        await preview.Loading;

        Assert.Equal(VehiclePreviewState.NeedsMods, preview.State);
        Assert.Equal([(false, false)], service.Loads);
        Assert.Equal(Localization.FormatPreviewLoadPartsSize(MainViewModel.SizeText(190L * 1024 * 1024)), preview.FetchModsText);
        Assert.Equal(
            [Localization.FormatPreviewMissingModParts(3, "Wings 1.0.0"), Localization.FormatPreviewMissingMod("Engines 2.0.0")],
            preview.MissingModLines.Select(line => line.Text));
        Assert.Equal([null, Localization.FormatPreviewLargeMod("Engines 2.0.0", MainViewModel.SizeText(150L * 1024 * 1024))], preview.MissingModLines.Select(line => line.Warning));

        await preview.FetchModsCommand.ExecuteAsync(null);

        Assert.Equal([(false, false), (false, true)], service.Loads);
    }

    [Fact]
    public async Task NeedsMods_WithAFetchableModOfUnknownSize_ShowsNoSizeOnLoadParts()
    {
        var service = new FakeVehiclePreview
        {
            Missing =
            [
                new VehiclePreviewMissingMod("Wings", null, null, 12_000_000, true),
                new VehiclePreviewMissingMod("Engines", null, null, null, true),
            ],
        };
        using var preview = Start(service);
        await preview.Loading;

        Assert.Equal(Localization.PreviewLoadParts, preview.FetchModsText);
    }

    [Fact]
    public async Task NoGameFolder_OffersTheSettings()
    {
        var opened = 0;
        var service = new FakeVehiclePreview { PlanState = VehiclePreviewPlanState.NoGameFolder };
        using var preview = Start(service, () => opened++);
        await preview.Loading;

        Assert.Equal(VehiclePreviewState.NoGameFolder, preview.State);
        Assert.True(preview.IsNoGameFolder);
        Assert.Equal(Localization.PreviewNoGameFolder, preview.StatusText);
        Assert.Empty(service.Loads);

        preview.OpenSettingsCommand.Execute(null);
        Assert.Equal(1, opened);
    }

    [Fact]
    public async Task AServiceException_GivesFailed()
    {
        var service = new FakeVehiclePreview { PrepareFailure = new InvalidOperationException("The index is gone.") };
        using var preview = Start(service);
        await preview.Loading;

        Assert.Equal(VehiclePreviewState.Failed, preview.State);
        Assert.Equal(Localization.FormatPreviewFailed("The index is gone."), preview.StatusText);
    }

    [Fact]
    public async Task Page_FollowsLatestVersion_CancelsTheOldPreview_AndDropsItsLateResult()
    {
        var held = new TaskCompletionSource<VehiclePreviewResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeVehiclePreview { HoldLoad = release => release.Version == ModVersion.Parse("1.1.0") ? held.Task : null };
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        viewModel.VehiclePreviewServiceOf = _ => service;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);
        var rover = Assert.Single(viewModel.DiscoverItems);

        // the page sets the item before its release is known, which prepares nothing
        viewModel.SelectedContent = rover;
        Assert.Null(viewModel.VehiclePreview);
        Assert.Empty(service.Prepares);

        await rover.OpenCommand.ExecuteAsync(null);
        var first = viewModel.VehiclePreview!;
        Assert.Equal(VehiclePreviewState.Building, first.State);

        var older = (await harness.Services.UserContent.GetReleaseAsync("ExampleRover", ModVersion.Parse("1.0.0")))!;
        viewModel.LatestVersion = new VersionItem(viewModel, older);
        await viewModel.WhenVehiclePreviewLoadedAsync();

        var late = new FakeAssets();
        held.SetResult(service.Ready(late));
        await first.Loading;

        Assert.True(Assert.Single(service.LoadTokens, token => token.Release == Rover.Version).Token.IsCancellationRequested);
        Assert.True(late.Disposed);
        Assert.Null(first.Scene);
        Assert.NotSame(first, viewModel.VehiclePreview);
        Assert.Equal(["1.1.0", "1.0.0"], service.Prepares.Select(release => release.Version.ToString()));
        Assert.Equal(VehiclePreviewState.Ready, viewModel.VehiclePreview!.State);

        viewModel.SetMainWindowDiscover();
        Assert.Null(viewModel.VehiclePreview);
    }

    [Fact]
    public async Task Page_MadeAgainForTheSameRelease_KeepsARunningAutoFetch()
    {
        var (harness, service, held) = await OpenRoverWithHeldAutoFetchAsync();
        using var _ = harness;
        var viewModel = harness.ViewModel;
        var first = viewModel.VehiclePreview!;

        viewModel.LatestVersion = new VersionItem(viewModel, viewModel.LatestVersion!.Release);

        Assert.Same(first, viewModel.VehiclePreview);
        Assert.False(Assert.Single(service.LoadTokens).Token.IsCancellationRequested);
        held.SetResult(service.Ready(new FakeAssets()));
        await viewModel.WhenVehiclePreviewLoadedAsync();
        Assert.Equal([(true, false)], service.Loads);
        Assert.Equal(VehiclePreviewState.Ready, first.State);
    }

    [Fact]
    public async Task Page_MadeAgainAfterACancel_WaitsForTheClick()
    {
        var (harness, service, held) = await OpenRoverWithHeldAutoFetchAsync();
        using var _ = harness;
        var viewModel = harness.ViewModel;
        var first = viewModel.VehiclePreview!;
        first.CancelCommand.Execute(null);
        held.SetResult(service.Ready(new FakeAssets()));
        await first.Loading;
        Assert.Equal(VehiclePreviewState.Cancelled, first.State);

        viewModel.LatestVersion = new VersionItem(viewModel, viewModel.LatestVersion!.Release);
        await viewModel.WhenVehiclePreviewLoadedAsync();

        Assert.NotSame(first, viewModel.VehiclePreview);
        Assert.Equal(VehiclePreviewState.Idle, viewModel.VehiclePreview!.State);
        Assert.Single(service.Loads);

        // the cancel also holds for the next page refresh and for a rebuild of the services
        viewModel.LatestVersion = new VersionItem(viewModel, viewModel.LatestVersion!.Release);
        await viewModel.WhenVehiclePreviewLoadedAsync();
        Assert.Equal(VehiclePreviewState.Idle, viewModel.VehiclePreview!.State);
        viewModel.SelectedReleaseChannel = viewModel.OptionFor(ReleaseChannel.Testing);
        await viewModel.WhenReleaseChannelSavedAsync();
        await viewModel.WhenVehiclePreviewLoadedAsync();
        Assert.Equal(VehiclePreviewState.Idle, viewModel.VehiclePreview!.State);
        Assert.Single(service.Loads);
    }

    [Fact]
    public async Task Page_OpenedAgainAfterACancel_StillWaitsForTheClick()
    {
        var (harness, service, held) = await OpenRoverWithHeldAutoFetchAsync();
        using var _ = harness;
        var viewModel = harness.ViewModel;
        var first = viewModel.VehiclePreview!;
        first.CancelCommand.Execute(null);
        held.SetResult(service.Ready(new FakeAssets()));
        await first.Loading;

        // the cancel holds for the whole session, so the player leaving the page and opening it again sends no download
        viewModel.SetMainWindowDiscover();
        await Assert.Single(viewModel.DiscoverItems).OpenCommand.ExecuteAsync(null);
        await viewModel.WhenVehiclePreviewLoadedAsync();

        Assert.NotSame(first, viewModel.VehiclePreview);
        Assert.Equal(VehiclePreviewState.Idle, viewModel.VehiclePreview!.State);
        Assert.Single(service.Loads);
    }

    [Fact]
    public async Task Page_MadeAgainAfterAFailedFetch_WaitsForTheClick()
    {
        var (harness, service, held) = await OpenRoverWithHeldAutoFetchAsync();
        using var _ = harness;
        var viewModel = harness.ViewModel;
        var first = viewModel.VehiclePreview!;
        held.SetException(new IOException("The archive has another SHA-256."));
        await first.Loading;
        Assert.Equal(VehiclePreviewState.Failed, first.State);

        viewModel.LatestVersion = new VersionItem(viewModel, viewModel.LatestVersion!.Release);
        await viewModel.WhenVehiclePreviewLoadedAsync();

        Assert.Equal(VehiclePreviewState.Idle, viewModel.VehiclePreview!.State);
        Assert.Single(service.Loads);
    }

    [Fact]
    public async Task Page_AnInstallDuringTheDelay_StopsTheAutoFetch_SoOnlyTheInstallDownloads()
    {
        var (harness, service) = await OpenRoverForAnInstallAsync();
        using var _ = harness;
        var viewModel = harness.ViewModel;
        var row = viewModel.SelectedContent!;
        var waiting = viewModel.VehiclePreview!;
        Assert.True(waiting.IsWaitingToFetch);

        await row.InstallCommand.ExecuteAsync(null);
        await waiting.Loading;
        await viewModel.WhenVehiclePreviewLoadedAsync();
        Assert.NotSame(waiting, viewModel.VehiclePreview);
        Assert.Equal(VehiclePreviewState.Idle, viewModel.VehiclePreview!.State);

        // without a game the install waits for a go, as in the tests of the content install
        Assert.True(row.IsConfirmingInstall);
        await row.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Null(row.InstallError);
        Assert.Empty(service.Loads);
        Assert.Single(harness.Requests, uri => uri.AbsoluteUri == UserContentFixtures.RoverUrl);

        // the install changed the instance, so the page made a preview again, which waits out its delay until the test leaves the page
        viewModel.SetMainWindowDiscover();
    }

    [Fact]
    public async Task Page_MadeAgainWhileAnInstallWaitsForItsConfirmation_FetchesNothing()
    {
        var (harness, service) = await OpenRoverForAnInstallAsync();
        using var _ = harness;
        var viewModel = harness.ViewModel;
        var row = viewModel.SelectedContent!;
        await row.InstallCommand.ExecuteAsync(null);
        Assert.True(row.IsConfirmingInstall);

        viewModel.VehiclePreviewAutoFetchDelay = TimeSpan.Zero;
        viewModel.LatestVersion = new VersionItem(viewModel, viewModel.LatestVersion!.Release);
        await viewModel.WhenVehiclePreviewLoadedAsync();

        Assert.Equal(VehiclePreviewState.Idle, viewModel.VehiclePreview!.State);
        Assert.Empty(service.Loads);
    }

    [Fact]
    public async Task Page_MadeAgainWhileTheNameModalAsksForAnInstall_FetchesNothing()
    {
        var (harness, service) = await OpenRoverForAnInstallAsync();
        using var _ = harness;
        var viewModel = harness.ViewModel;
        var vehicles = harness.Services.Paths.GetInstanceVehiclesFolder(viewModel.ActiveInstance!.InstanceId);
        Directory.CreateDirectory(Path.Combine(vehicles, "Example Rover"));
        await viewModel.SelectedContent!.InstallCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsNameModalOpen);

        viewModel.VehiclePreviewAutoFetchDelay = TimeSpan.Zero;
        viewModel.LatestVersion = new VersionItem(viewModel, viewModel.LatestVersion!.Release);
        await viewModel.WhenVehiclePreviewLoadedAsync();

        Assert.Equal(VehiclePreviewState.Idle, viewModel.VehiclePreview!.State);
        Assert.Empty(service.Loads);
    }

    [Fact]
    public async Task Page_MadeAgainAfterTheInstallConfirmationWasCancelled_FetchesWithoutAClick()
    {
        var (harness, service) = await OpenRoverForAnInstallAsync();
        using var _ = harness;
        var viewModel = harness.ViewModel;
        var row = viewModel.SelectedContent!;
        await row.InstallCommand.ExecuteAsync(null);
        row.CancelInstallCommand.Execute(null);

        viewModel.VehiclePreviewAutoFetchDelay = TimeSpan.Zero;
        viewModel.LatestVersion = new VersionItem(viewModel, viewModel.LatestVersion!.Release);
        await viewModel.WhenVehiclePreviewLoadedAsync();

        Assert.Equal([(true, false)], service.Loads);
    }

    /// <summary>
    /// Opens the rover page over a service that needs the small vehicle file, with an active instance to install it
    /// into and an auto fetch delay that outlasts the test, so only the test makes the page fetch on its own.
    /// </summary>
    private static async Task<(ViewModelHarness Harness, FakeVehiclePreview Service)> OpenRoverForAnInstallAsync()
    {
        var service = new FakeVehiclePreview { PlanState = VehiclePreviewPlanState.NeedsVehicleFile };
        var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        viewModel.VehiclePreviewServiceOf = _ => service;
        viewModel.VehiclePreviewAutoFetchDelay = TimeSpan.FromMinutes(5);
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await viewModel.LoadAsync();
        await OpenRoverPageAsync(viewModel);
        return (harness, service);
    }

    /// <summary>
    /// Opens the rover page over a service that needs the small vehicle file, and waits until the fetch that the page
    /// starts on its own is held by the test.
    /// </summary>
    private static async Task<(ViewModelHarness Harness, FakeVehiclePreview Service, TaskCompletionSource<VehiclePreviewResult> Held)> OpenRoverWithHeldAutoFetchAsync()
    {
        var held = new TaskCompletionSource<VehiclePreviewResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeVehiclePreview
        {
            PlanState = VehiclePreviewPlanState.NeedsVehicleFile,
            HoldLoad = _ =>
            {
                started.TrySetResult();
                return held.Task;
            },
        };
        var harness = await VehicleHarnessAsync();
        harness.ViewModel.VehiclePreviewServiceOf = _ => service;
        await OpenRoverPageAsync(harness.ViewModel);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        return (harness, service, held);
    }

    [Fact]
    public async Task NoGameFolder_ThenARebuildWithAGameFolder_BuildsOnTheNewService()
    {
        var noGame = new FakeVehiclePreview { PlanState = VehiclePreviewPlanState.NoGameFolder };
        var withGame = new FakeVehiclePreview();
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        var firstServices = harness.Services;
        viewModel.VehiclePreviewServiceOf = services => ReferenceEquals(services, firstServices) ? noGame : withGame;
        await OpenRoverAsync(viewModel);
        Assert.Equal(VehiclePreviewState.NoGameFolder, viewModel.VehiclePreview!.State);

        viewModel.SelectedReleaseChannel = viewModel.OptionFor(ReleaseChannel.Testing);
        await viewModel.WhenReleaseChannelSavedAsync();
        await viewModel.WhenVehiclePreviewLoadedAsync();

        Assert.NotSame(firstServices, harness.Services);
        Assert.Equal(VehiclePreviewState.Ready, viewModel.VehiclePreview!.State);
        Assert.NotEmpty(withGame.Prepares);
        Assert.Contains((false, false), withGame.Loads);
        Assert.Empty(noGame.Loads);
    }

    [Fact]
    public async Task AnInstallIntoTheTargetInstance_MakesThePreviewAgain_AndAReloadWithoutAChangeKeepsIt()
    {
        var service = new FakeVehiclePreview();
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        viewModel.VehiclePreviewServiceOf = _ => service;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await viewModel.LoadAsync();
        await OpenRoverAsync(viewModel);
        var first = viewModel.VehiclePreview!;

        await viewModel.LoadAsync();
        Assert.Same(first, viewModel.VehiclePreview);

        var release = (await harness.Services.UserContent.GetReleaseAsync("ExampleRover", ModVersion.Parse("1.0.0")))!;
        Assert.True((await harness.Services.UserContentInstaller.InstallAsync(instance.InstanceId, release, name: null)).IsDone);
        await viewModel.LoadAsync();
        await viewModel.WhenVehiclePreviewLoadedAsync();

        Assert.NotSame(first, viewModel.VehiclePreview);
        Assert.Null(first.Scene);
        Assert.Equal([instance.InstanceId, instance.InstanceId], service.Targets);
    }

    [Fact]
    public async Task Loading_AlsoCoversARunThatANewerRunCancelled()
    {
        var held = new TaskCompletionSource<VehiclePreviewResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var taken = 0;
        var service = new FakeVehiclePreview { HoldLoad = _ => Interlocked.Exchange(ref taken, 1) == 0 ? held.Task : null };
        using var preview = Start(service);

        await preview.Start();

        Assert.Equal(VehiclePreviewState.Ready, preview.State);
        Assert.False(preview.Loading.IsCompleted);

        var late = new FakeAssets();
        held.SetResult(service.Ready(late));
        await preview.Loading;
        Assert.True(late.Disposed);
    }

    [Fact]
    public async Task DeletingAnInstance_WaitsForTheLoadOfAPreviewThatClosed()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await viewModel.LoadAsync();
        var (service, held) = await OpenRoverWithHeldLoadAsync(viewModel);
        var root = harness.Services.Paths.GetInstanceRoot(instance.InstanceId);
        Directory.CreateDirectory(root);

        viewModel.SetMainWindowLibrary();

        Assert.Null(viewModel.VehiclePreview);
        Assert.True(Assert.Single(service.LoadTokens).Token.IsCancellationRequested);
        Assert.False(viewModel.WhenVehiclePreviewReadsEndedAsync().IsCompleted);

        viewModel.BeginDeleteInstance(Assert.Single(viewModel.Instances));
        var delete = viewModel.ConfirmDeleteModalCommand.ExecuteAsync(null);

        Assert.True(Directory.Exists(root));
        Assert.False(delete.IsCompleted);

        held.SetResult(service.Ready(new FakeAssets()));
        await delete;

        Assert.False(Directory.Exists(root));
        Assert.True(viewModel.WhenVehiclePreviewReadsEndedAsync().IsCompleted);
    }

    [Fact]
    public async Task RemovingTheVehicleOnItsPage_StopsAPreviewThatStillReads_AndMakesItAgainAfterwards()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        var release = (await harness.Services.UserContent.GetReleaseAsync("ExampleRover", ModVersion.Parse("1.0.0")))!;
        Assert.True((await harness.Services.UserContentInstaller.InstallAsync(instance.InstanceId, release, name: null)).IsDone);
        await viewModel.LoadAsync();
        var (service, held) = await OpenRoverWithHeldLoadAsync(viewModel);
        var first = viewModel.VehiclePreview!;
        var record = Assert.Single(await harness.Services.InstalledContent.ListAsync(instance.InstanceId));
        var folder = Path.Combine(harness.Services.Paths.GetInstanceVehiclesFolder(instance.InstanceId), record.FolderName);

        var remove = viewModel.RemoveAsync(viewModel.SelectedContent!);

        Assert.Null(viewModel.VehiclePreview);
        Assert.True(Assert.Single(service.LoadTokens).Token.IsCancellationRequested);
        Assert.True(Directory.Exists(folder));
        Assert.False(remove.IsCompleted);

        held.SetResult(service.Ready(new FakeAssets()));
        await remove;
        await viewModel.WhenVehiclePreviewLoadedAsync();

        Assert.False(Directory.Exists(folder));
        Assert.Empty(await harness.Services.InstalledContent.ListAsync(instance.InstanceId));
        Assert.NotNull(viewModel.VehiclePreview);
        Assert.NotSame(first, viewModel.VehiclePreview);
    }

    [Fact]
    public async Task AServiceException_LeavesThePageUsable()
    {
        var service = new FakeVehiclePreview { PrepareFailure = new IOException("The disk is gone.") };
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        viewModel.VehiclePreviewServiceOf = _ => service;
        await OpenRoverAsync(viewModel);

        Assert.Equal(VehiclePreviewState.Failed, viewModel.VehiclePreview!.State);
        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);
        Assert.Equal(["1.1.0", "1.0.0"], viewModel.ContentVersions.Select(version => version.Version));
    }

    [Fact]
    public async Task OnlyACompleteScene_StoresAStill_OncePerSceneKey()
    {
        var thumbnails = new FakeThumbnails();
        var stored = new List<string>();
        var box = new ResolvedPlaceholder(Matrix4x4.Identity, Vector3.One, "Wing", PlaceholderReason.UnknownPart, null);
        var missing = new VehiclePreviewMissingMod("Wings", null, null, null, CanFetch: false);

        foreach (var service in new[]
        {
            new FakeVehiclePreview { Vehicle = TriangleVehicle(box), Files = TriangleFiles },
            new FakeVehiclePreview { Vehicle = TriangleVehicle(), Files = TriangleFiles, Missing = [missing] },
            new FakeVehiclePreview(),
        })
        {
            using var incomplete = Start(service, thumbnails: thumbnails, stillStored: stored.Add);
            await incomplete.Loading;
            Assert.NotNull(incomplete.Scene);
        }

        Assert.Empty(thumbnails.Stores);

        for (var i = 0; i < 2; i++)
        {
            using var complete = Start(new FakeVehiclePreview { Vehicle = TriangleVehicle(), Files = TriangleFiles }, thumbnails: thumbnails, stillStored: stored.Add);
            await complete.Loading;
            Assert.Equal(VehiclePreviewState.Ready, complete.State);
        }

        var still = Assert.Single(thumbnails.Stores);
        Assert.Equal((new string('a', 64), "2026.10.19.5638", "scene"), (still.Sha256, still.GameBuild, still.SceneKey));
        Assert.Equal((VehiclePreviewViewModel.StillWidth, VehiclePreviewViewModel.StillHeight), (still.Width, still.Height));
        Assert.True(still.CoveredPixels > 0);
        Assert.Equal([new string('a', 64)], stored);
    }

    [Fact]
    public async Task AStillWithAFailedTexture_IsPartial_UntilATexturedLoadReplacesIt()
    {
        var thumbnails = new FakeThumbnails();
        var hull = new PreviewAssetRef("Core", "Hull.png");
        var vehicle = TriangleVehicle() with
        {
            Materials = [new PreviewMaterialRef("Hull", hull, null, false)],
            Draws = [new ResolvedDraw(0, 0, Matrix4x4.Identity, "Body", PreviewModelKind.Model)],
        };
        var withoutHull = new Dictionary<string, byte[]> { [TriangleFile.Path] = TriangleGlb(uvs: true) };
        var withHull = new Dictionary<string, byte[]>(withoutHull) { [hull.Path] = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A] };

        // The texture fails, then it loads, then it fails again, which must not replace the complete still.
        foreach (var files in new[] { withoutHull, withHull, withoutHull })
        {
            using var preview = Start(new FakeVehiclePreview { Vehicle = vehicle, Files = files }, thumbnails: thumbnails, decoder: new FlatDecoder());
            await preview.Loading;
            Assert.Equal(VehiclePreviewState.Ready, preview.State);
        }

        Assert.Equal(["scene" + VehiclePreviewViewModel.PartialKeySuffix, "scene"], thumbnails.Stores.Select(still => still.SceneKey));
    }

    [Fact]
    public async Task TheFacts_ShowTheRequiredModsAtOnce_AndThePartsAndStagesAfterTheLoad()
    {
        var service = new FakeVehiclePreview { PlanState = VehiclePreviewPlanState.NeedsVehicleFile, Vehicle = TriangleVehicle() with { PartCount = 3, StageCount = 2 }, Files = TriangleFiles };
        ModDependency[] dependencies = [new("Wings", ModDependencyKind.Required), new("Decals", ModDependencyKind.Optional)];
        using var preview = Start(service, release: RoverOfSize(VehiclePreviewViewModel.AutoFetchVehicleBytes + 1, dependencies));
        await preview.Loading;

        var mods = Localization.FormatShareSummaryMods("1");
        Assert.Equal([mods], preview.Facts);

        await preview.ShowCommand.ExecuteAsync(null);

        Assert.Equal([Localization.FormatPreviewFactParts(3), Localization.FormatPreviewFactStages(2), mods], preview.Facts);
    }

    [Fact]
    public async Task TheFacts_OfAVehicleWithoutStagesOrMods_ShowNoStageCountAndSayThatItNeedsNoMods()
    {
        using var preview = Start(new FakeVehiclePreview { Vehicle = TriangleVehicle(), Files = TriangleFiles });
        await preview.Loading;

        Assert.Equal([Localization.FormatPreviewFactParts(1), Localization.ShareSummaryNoMods], preview.Facts);
    }

    [Theory]
    [InlineData(PlaceholderReason.UnknownPart, false)]
    [InlineData(PlaceholderReason.UnknownSubPart, false)]
    [InlineData(PlaceholderReason.UnknownMesh, true)]
    public async Task TheFacts_SayThatAVehicleNeedsNoMods_OnlyWhenNoPartIsUnknown(PlaceholderReason reason, bool saysNoMods)
    {
        var box = new ResolvedPlaceholder(Matrix4x4.Identity, Vector3.One, "Wing", reason, null);
        using var preview = Start(new FakeVehiclePreview { Vehicle = TriangleVehicle(box), Files = TriangleFiles });
        await preview.Loading;

        Assert.Equal(saysNoMods, preview.Facts.Contains(Localization.ShareSummaryNoMods));
    }

    [Fact]
    public async Task TheFacts_KeepTheCountOfRequiredMods_WhenAPartIsUnknown()
    {
        var box = new ResolvedPlaceholder(Matrix4x4.Identity, Vector3.One, "Wing", PlaceholderReason.UnknownPart, null);
        using var preview = Start(new FakeVehiclePreview { Vehicle = TriangleVehicle(box), Files = TriangleFiles }, release: RoverOfSize(2_500_000, [new("Wings", ModDependencyKind.Required)]));
        await preview.Loading;

        Assert.Contains(Localization.FormatShareSummaryMods("1"), preview.Facts);
    }

    [Fact]
    public async Task SavePicture_WritesThePngOfTheViewToThePickedFile()
    {
        var picks = new List<string>();
        var encoder = new RecordingEncoder();
        using var written = new MemoryStream();
        using var preview = Start(new FakeVehiclePreview(), encoder: encoder, savePicture: async (name, write) =>
        {
            picks.Add(name);
            await write(written);
        });
        await preview.Loading;
        var scene = PreviewTestScenes.StackedBoxes();
        preview.Scene = scene;
        var camera = new OrbitCamera();
        camera.Frame(scene.Bounds, 64f / 36f);

        await preview.SavePictureCommand.ExecuteAsync(new FixedPicture(new VehiclePicture(scene, camera, 64, 36)));

        Assert.Equal(["ExampleRover.png"], picks);
        Assert.Equal(RecordingEncoder.Png, written.ToArray());
        var frame = Assert.Single(encoder.Frames);
        Assert.Equal((64, 36), (frame.Width, frame.Height));

        // The vehicle shows, and the pixels around it stay transparent.
        Assert.InRange(frame.Covered, 1, 64 * 36 - 1);
    }

    [Fact]
    public async Task SavePicture_DrawsNothing_WhenThePlayerCancelsThePick_AndAsksNothing_WithoutAPicture()
    {
        var picks = 0;
        var encoder = new RecordingEncoder();
        using var preview = Start(new FakeVehiclePreview(), encoder: encoder, savePicture: (_, _) =>
        {
            picks++;
            return Task.CompletedTask;
        });
        await preview.Loading;
        var scene = PreviewTestScenes.StackedBoxes();
        preview.Scene = scene;

        await preview.SavePictureCommand.ExecuteAsync(new FixedPicture(null));
        Assert.Equal(0, picks);

        await preview.SavePictureCommand.ExecuteAsync(new FixedPicture(new VehiclePicture(scene, new OrbitCamera(), 8, 8)));
        Assert.Equal(1, picks);
        Assert.Empty(encoder.Frames);
    }

    [Fact]
    public async Task SavePicture_IsOffWithoutAScene()
    {
        using var preview = Start(new FakeVehiclePreview { PlanState = VehiclePreviewPlanState.NoGameFolder });
        await preview.Loading;
        Assert.False(preview.SavePictureCommand.CanExecute(null));

        preview.Scene = PreviewTestScenes.StackedBoxes();
        Assert.True(preview.SavePictureCommand.CanExecute(null));
    }

    [Fact]
    public async Task SavePicture_OnTheOpenPage_ShowsTheSavedFile_OrAnErrorWhenTheFileCannotBeWritten()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        viewModel.VehiclePreviewServiceOf = _ => new FakeVehiclePreview();
        viewModel.VehiclePreviewEncoder = new RecordingEncoder();
        var window = new GameSaveShareTests.ArchiveWindow();
        viewModel.WindowServices = window;
        await OpenRoverAsync(viewModel);
        var preview = viewModel.VehiclePreview!;
        var scene = PreviewTestScenes.StackedBoxes();
        preview.Scene = scene;
        var picture = new FixedPicture(new VehiclePicture(scene, new OrbitCamera(), 8, 8));

        await preview.SavePictureCommand.ExecuteAsync(picture);
        Assert.Equal(harness.Localization.FormatListingSaved("ExampleRover.png"), viewModel.Toasts.Items[^1].Message);
        Assert.Equal(RecordingEncoder.Png, window.Written.ToArray());

        viewModel.WindowServices = new FailingWindow();
        await preview.SavePictureCommand.ExecuteAsync(picture);
        Assert.Equal(harness.Localization.PreviewPictureFailed, viewModel.Toasts.Items[^1].Message);
        Assert.Equal("Disk full.", viewModel.Toasts.Items[^1].Detail);
    }

    private sealed class FixedPicture(VehiclePicture? picture) : IVehiclePictureSource
    {
        public VehiclePicture? CurrentPicture() => picture;
    }

    private sealed class RecordingEncoder : IPngEncoder
    {
        public static readonly byte[] Png = [1, 2, 3];

        public List<(int Width, int Height, int Covered)> Frames { get; } = [];

        public byte[] Encode(FrameBuffer frame)
        {
            var covered = 0;
            for (var i = 3; i < frame.Rgba.Length; i += 4)
            {
                if (frame.Rgba[i] != 0)
                    covered++;
            }

            Frames.Add((frame.Width, frame.Height, covered));
            return Png;
        }
    }

    private sealed class FailingWindow : IWindowServices
    {
        public Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string fileTypeName, string text) => Task.FromResult<string?>(null);

        public Task<string?> SaveFileAsync(string title, string suggestedFileName, string extension, string fileTypeName, Func<Stream, Task> write) => Task.FromException<string?>(new IOException("Disk full."));

        public Task<PickedTextFile?> OpenTextFileAsync(string title, string fileTypeName) => Task.FromResult<PickedTextFile?>(null);

        public Task<PickedBinaryFile?> OpenImageFileAsync(string title, string fileTypeName, long maxBytes) => Task.FromResult<PickedBinaryFile?>(null);

        public Task CopyTextAsync(string text) => Task.CompletedTask;
    }

    internal static async Task OpenRoverAsync(MainViewModel viewModel)
    {
        await OpenRoverPageAsync(viewModel);
        await viewModel.WhenVehiclePreviewLoadedAsync();
    }

    /// <summary>Opens the rover page and returns before its preview has loaded.</summary>
    private static async Task OpenRoverPageAsync(MainViewModel viewModel)
    {
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);
        await Assert.Single(viewModel.DiscoverItems).OpenCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Opens the rover page with a first load that the returned source holds, as a slow read of a part file does. The
    /// test completes the source, because the load holds on to it after a cancel too.
    /// </summary>
    internal static async Task<(FakeVehiclePreview Service, TaskCompletionSource<VehiclePreviewResult> Held)> OpenRoverWithHeldLoadAsync(MainViewModel viewModel)
    {
        var held = new TaskCompletionSource<VehiclePreviewResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var taken = 0;
        var service = new FakeVehiclePreview { HoldLoad = _ => Interlocked.Exchange(ref taken, 1) == 0 ? held.Task : null };
        viewModel.VehiclePreviewServiceOf = _ => service;
        await OpenRoverPageAsync(viewModel);
        Assert.False(viewModel.WhenVehiclePreviewReadsEndedAsync().IsCompleted);
        return (service, held);
    }

    private static VehiclePreviewViewModel Start(
        FakeVehiclePreview service,
        Action? openSettings = null,
        FakeThumbnails? thumbnails = null,
        Action<string>? stillStored = null,
        IRgbaImageDecoder? decoder = null,
        ModVersionMetadata? release = null,
        TimeSpan? autoFetchDelay = null,
        IPngEncoder? encoder = null,
        Func<string, Func<Stream, Task>, Task>? savePicture = null)
    {
        var preview = new VehiclePreviewViewModel(
            service,
            release ?? Rover,
            null,
            Localization,
            openSettings ?? (() => { }),
            new PreviewSceneBuilder(new PreviewTextureLoader(decoder)),
            thumbnails: thumbnails,
            encoder: encoder ?? thumbnails,
            stillStored: stillStored,
            autoFetchDelay: autoFetchDelay,
            savePicture: savePicture);
        preview.Start();
        return preview;
    }

    /// <summary>The Rover release with another stated size of its download, or with none.</summary>
    private static ModVersionMetadata RoverOfSize(long? size, ModDependency[]? dependencies = null) => new(
        specVersion: 1,
        modId: Rover.ModId,
        version: Rover.Version,
        releaseStatus: Rover.ReleaseStatus,
        releaseDate: Rover.ReleaseDate,
        gameMin: Rover.GameMin,
        gameMinRevision: Rover.GameMinRevision,
        download: new DownloadInfo(Rover.Download.Url, Rover.Download.Sha256, size, Rover.Download.ContentType),
        installSizeBytes: null,
        dependencies: dependencies ?? []);

    private static readonly PreviewAssetRef TriangleFile = new("Core", "Triangle.glb");

    private static readonly IReadOnlyDictionary<string, byte[]> TriangleFiles = new Dictionary<string, byte[]> { [TriangleFile.Path] = TriangleGlb() };

    /// <summary>A vehicle of one triangle part, and the given boxes where parts did not resolve.</summary>
    private static ResolvedVehicle TriangleVehicle(params ResolvedPlaceholder[] boxes) => new(
        [new PreviewMeshRef(TriangleFile, 0, "Triangle")],
        [],
        [new ResolvedDraw(0, -1, Matrix4x4.Identity, "Body", PreviewModelKind.Model)],
        boxes,
        [],
        1 + boxes.Length,
        0);

    /// <summary>
    /// A GLB with one triangle and 16-bit indices, the smallest mesh that gives a scene a draw, and with UVs when
    /// <paramref name="uvs"/> is true, so its material can load a texture.
    /// </summary>
    private static byte[] TriangleGlb(bool uvs = false)
    {
        byte[] bin =
        [
            .. GlbTestWriter.Floats(0, 0, 0, 1, 0, 0, 0, 1, 0),
            .. GlbTestWriter.Indices(GlbTestWriter.UnsignedShortComponent, 0, 1, 2, 0),
            .. uvs ? GlbTestWriter.Floats(0, 0, 1, 0, 0, 1) : [],
        ];
        var attributes = new JsonObject { ["POSITION"] = 0 };
        var views = new JsonArray
        {
            new JsonObject { ["buffer"] = 0, ["byteOffset"] = 0, ["byteLength"] = 36 },
            new JsonObject { ["buffer"] = 0, ["byteOffset"] = 36, ["byteLength"] = 6 },
        };
        var accessors = new JsonArray
        {
            new JsonObject
            {
                ["bufferView"] = 0, ["componentType"] = GlbTestWriter.FloatComponent, ["count"] = 3, ["type"] = "VEC3",
                ["min"] = new JsonArray(0, 0, 0), ["max"] = new JsonArray(1, 1, 0),
            },
            new JsonObject { ["bufferView"] = 1, ["componentType"] = GlbTestWriter.UnsignedShortComponent, ["count"] = 3, ["type"] = "SCALAR" },
        };
        if (uvs)
        {
            // The UVs start after a padding index, because a float accessor must start on 4 bytes.
            views.Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = 44, ["byteLength"] = 24 });
            accessors.Add(new JsonObject { ["bufferView"] = 2, ["componentType"] = GlbTestWriter.FloatComponent, ["count"] = 3, ["type"] = "VEC2" });
            attributes["TEXCOORD_0"] = 2;
        }

        var json = new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0" },
            ["buffers"] = new JsonArray { new JsonObject { ["byteLength"] = bin.Length } },
            ["bufferViews"] = views,
            ["accessors"] = accessors,
            ["meshes"] = new JsonArray
            {
                new JsonObject { ["primitives"] = new JsonArray { new JsonObject { ["attributes"] = attributes, ["indices"] = 1 } } },
            },
        };
        return GlbTestWriter.Pack(json, bin);
    }

    /// <summary>Decodes any image to one gray pixel, so a texture loads without a real PNG decoder.</summary>
    private sealed class FlatDecoder : IRgbaImageDecoder
    {
        public bool TryReadSize(Stream stream, out int width, out int height)
        {
            (width, height) = (1, 1);
            return true;
        }

        public PreviewImage Decode(Stream stream, int width) => new(1, 1, [128, 128, 128, 255]);
    }
}

/// <summary>
/// Stills in memory, with the scene key of each, and an encoder that records the size and the covered pixels of the
/// frame instead of a PNG.
/// </summary>
internal sealed class FakeThumbnails : IPreviewThumbnails, IPngEncoder
{
    private readonly ConcurrentDictionary<(string, string), string> _keys = new();
    private readonly ConcurrentQueue<Still> _stores = new();
    private (int Width, int Height, int Covered) _lastFrame;

    public IReadOnlyList<Still> Stores => [.. _stores];

    public string? TryGetPath(string? vehicleSha256, string? gameBuild) => null;

    public bool NeedsUpdate(string? vehicleSha256, string? gameBuild, string? sceneKey) =>
        vehicleSha256 is not null && gameBuild is not null && (!_keys.TryGetValue((vehicleSha256, gameBuild), out var key) || key != sceneKey);

    public void Store(string vehicleSha256, string gameBuild, string sceneKey, byte[] pngBytes)
    {
        _keys[(vehicleSha256, gameBuild)] = sceneKey;
        _stores.Enqueue(new Still(vehicleSha256, gameBuild, sceneKey, _lastFrame.Width, _lastFrame.Height, _lastFrame.Covered));
    }

    public byte[] Encode(FrameBuffer frame)
    {
        var covered = 0;
        for (var i = 3; i < frame.Rgba.Length; i += 4)
        {
            if (frame.Rgba[i] != 0)
                covered++;
        }

        _lastFrame = (frame.Width, frame.Height, covered);
        return [1];
    }

    public sealed record Still(string Sha256, string GameBuild, string SceneKey, int Width, int Height, int CoveredPixels);
}

/// <summary>
/// A preview service that answers from its settings and records each call. A load gives a ready result with an empty
/// vehicle, so the scene builder runs for real without any file.
/// </summary>
internal sealed class FakeVehiclePreview : IVehiclePreview
{
    private readonly ConcurrentQueue<ModVersionMetadata> _prepares = new();
    private readonly ConcurrentQueue<Guid?> _targets = new();
    private readonly ConcurrentQueue<(bool FetchVehicle, bool FetchMods)> _loads = new();
    private readonly ConcurrentQueue<(ModVersion Release, CancellationToken Token)> _loadTokens = new();

    public VehiclePreviewPlanState PlanState { get; init; } = VehiclePreviewPlanState.CanBuildLocally;

    public Exception? PrepareFailure { get; init; }

    public IReadOnlyList<VehiclePreviewMissingMod> Missing { get; init; } = [];

    /// <summary>The vehicle of a ready result, which has no part unless a test gives one.</summary>
    public ResolvedVehicle Vehicle { get; init; } = new([], [], [], [], [], 0, 0);

    /// <summary>The asset files of a ready result, by path.</summary>
    public IReadOnlyDictionary<string, byte[]>? Files { get; init; }

    /// <summary>Gives a task for a load of the release that the test completes itself, or null to answer at once.</summary>
    public Func<ModVersionMetadata, Task<VehiclePreviewResult>?>? HoldLoad { get; init; }

    public IReadOnlyList<ModVersionMetadata> Prepares => [.. _prepares];

    public IReadOnlyList<Guid?> Targets => [.. _targets];

    public IReadOnlyList<(bool FetchVehicle, bool FetchMods)> Loads => [.. _loads];

    public IReadOnlyList<(ModVersion Release, CancellationToken Token)> LoadTokens => [.. _loadTokens];

    /// <summary>The progress of the last load, so a test can report a stage after the load ended.</summary>
    public IProgress<VehiclePreviewStage>? LastProgress { get; private set; }

    public Task<VehiclePreviewPlan> PrepareAsync(ModVersionMetadata release, Guid? targetInstanceId, CancellationToken cancellationToken = default)
    {
        _prepares.Enqueue(release);
        _targets.Enqueue(targetInstanceId);
        return PrepareFailure is { } failure
            ? Task.FromException<VehiclePreviewPlan>(failure)
            : Task.FromResult(new VehiclePreviewPlan(PlanState, release, targetInstanceId, release.Download.SizeBytes, Missing, "2026.10.19.5638"));
    }

    public Task<VehiclePreviewResult> LoadAsync(VehiclePreviewPlan plan, bool fetchVehicle, bool fetchMods, IProgress<VehiclePreviewStage>? progress = null, CancellationToken cancellationToken = default)
    {
        _loads.Enqueue((fetchVehicle, fetchMods));
        _loadTokens.Enqueue((plan.Release.Version, cancellationToken));
        LastProgress = progress;
        progress?.Report(VehiclePreviewStage.ReadingParts);
        return HoldLoad?.Invoke(plan.Release) ?? Task.FromResult(Ready(new FakeAssets(Files), fetchMods ? [] : Missing));
    }

    public VehiclePreviewResult Ready(IPreviewAssetSource assets, IReadOnlyList<VehiclePreviewMissingMod>? missing = null) => new(
        VehiclePreviewLoadState.Ready,
        Vehicle,
        assets,
        missing ?? Missing,
        [],
        "scene",
        new string('a', 64),
        "2026.10.19.5638");

    public Task StartMaintenance() => Task.CompletedTask;

    public void Dispose()
    {
    }
}

/// <summary>An asset source over files in memory, or without files, that records its dispose.</summary>
internal sealed class FakeAssets(IReadOnlyDictionary<string, byte[]>? files = null) : IPreviewAssetSource
{
    public bool Disposed { get; private set; }

    public Stream? TryOpenRead(PreviewAssetRef asset) => files?.GetValueOrDefault(asset.Path) is { } bytes ? new MemoryStream(bytes, writable: false) : null;

    public Stream? TryOpenHead(PreviewAssetRef asset) => TryOpenRead(asset);

    public string GetCacheKey(PreviewAssetRef asset) => asset.ToString();

    public void Dispose() => Disposed = true;
}
