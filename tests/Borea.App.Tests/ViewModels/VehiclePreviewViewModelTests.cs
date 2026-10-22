using System.Collections.Concurrent;
using System.Globalization;
using Borea.App.Localization;
using Borea.App.ViewModels;
using Borea.Core.Mods;
using Borea.Core.Preview;
using Borea.Preview.Loading;
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

    [Fact]
    public async Task NeedsVehicleFile_LoadsNothingBeforeTheClick_AndTheClickFetchesOnlyTheVehicle()
    {
        var service = new FakeVehiclePreview { PlanState = VehiclePreviewPlanState.NeedsVehicleFile };
        using var preview = Start(service);
        await preview.Loading;

        Assert.Equal(VehiclePreviewState.Idle, preview.State);
        Assert.Empty(service.Loads);
        Assert.True(preview.ShowCommand.CanExecute(null));
        Assert.Equal(Localization.FormatPreviewShowSize(MainViewModel.SizeText(2_500_000)), preview.ShowText);

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

    internal static async Task OpenRoverAsync(MainViewModel viewModel)
    {
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);
        await Assert.Single(viewModel.DiscoverItems).OpenCommand.ExecuteAsync(null);
        await viewModel.WhenVehiclePreviewLoadedAsync();
    }

    private static VehiclePreviewViewModel Start(FakeVehiclePreview service, Action? openSettings = null)
    {
        var preview = new VehiclePreviewViewModel(service, Rover, null, Localization, openSettings ?? (() => { }), new PreviewSceneBuilder(new PreviewTextureLoader(null)));
        preview.Start();
        return preview;
    }
}

/// <summary>
/// A preview service that answers from its settings and records each call. A load gives a ready result with an empty
/// vehicle, so the scene builder runs for real without any file.
/// </summary>
internal sealed class FakeVehiclePreview : IVehiclePreview
{
    private readonly ConcurrentQueue<ModVersionMetadata> _prepares = new();
    private readonly ConcurrentQueue<(bool FetchVehicle, bool FetchMods)> _loads = new();
    private readonly ConcurrentQueue<(ModVersion Release, CancellationToken Token)> _loadTokens = new();

    public VehiclePreviewPlanState PlanState { get; init; } = VehiclePreviewPlanState.CanBuildLocally;

    public Exception? PrepareFailure { get; init; }

    public IReadOnlyList<VehiclePreviewMissingMod> Missing { get; init; } = [];

    /// <summary>Gives a task for a load of the release that the test completes itself, or null to answer at once.</summary>
    public Func<ModVersionMetadata, Task<VehiclePreviewResult>?>? HoldLoad { get; init; }

    public IReadOnlyList<ModVersionMetadata> Prepares => [.. _prepares];

    public IReadOnlyList<(bool FetchVehicle, bool FetchMods)> Loads => [.. _loads];

    public IReadOnlyList<(ModVersion Release, CancellationToken Token)> LoadTokens => [.. _loadTokens];

    /// <summary>The progress of the last load, so a test can report a stage after the load ended.</summary>
    public IProgress<VehiclePreviewStage>? LastProgress { get; private set; }

    public Task<VehiclePreviewPlan> PrepareAsync(ModVersionMetadata release, Guid? targetInstanceId, CancellationToken cancellationToken = default)
    {
        _prepares.Enqueue(release);
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
        return HoldLoad?.Invoke(plan.Release) ?? Task.FromResult(Ready(new FakeAssets(), fetchMods ? [] : Missing));
    }

    public VehiclePreviewResult Ready(IPreviewAssetSource assets, IReadOnlyList<VehiclePreviewMissingMod>? missing = null) => new(
        VehiclePreviewLoadState.Ready,
        new ResolvedVehicle([], [], [], [], [], 0, 0),
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

/// <summary>An asset source without files that records its dispose.</summary>
internal sealed class FakeAssets : IPreviewAssetSource
{
    public bool Disposed { get; private set; }

    public Stream? TryOpenRead(PreviewAssetRef asset) => null;

    public Stream? TryOpenHead(PreviewAssetRef asset) => null;

    public string GetCacheKey(PreviewAssetRef asset) => asset.ToString();

    public void Dispose() => Disposed = true;
}
