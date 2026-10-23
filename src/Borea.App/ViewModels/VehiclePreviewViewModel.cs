using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Core.Mods;
using Borea.Core.Preview;
using Borea.Preview.Loading;
using Borea.Preview.Rendering;
using Borea.Preview.Scene;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>What the 3D preview panel of a vehicle page shows.</summary>
public enum VehiclePreviewState
{
    /// <summary>The panel waits for the player, because the vehicle file is not on this computer.</summary>
    Idle,

    Fetching,

    Building,

    /// <summary>The scene shows, and every part came from a mod on this computer.</summary>
    Ready,

    /// <summary>The scene shows, but some parts are boxes because a mod the vehicle needs is not on this computer.</summary>
    NeedsMods,

    /// <summary>No game folder is set, or the folder is not a KSA install, so Borea has no parts to draw with.</summary>
    NoGameFolder,

    VehicleDamaged,

    Failed,

    Cancelled,
}

/// <summary>
/// The 3D preview of one vehicle release on its page. It asks <see cref="IVehiclePreview"/> what the preview needs,
/// builds the scene at once when every file is on this computer, and fetches a vehicle file of at most
/// <see cref="AutoFetchVehicleBytes"/> after a short delay without a click. A larger vehicle file or the missing mods
/// it fetches only after a click. The meshes come first with flat colors, so the player can turn the vehicle early,
/// and the textures follow. The page makes a new instance for each release and disposes the old one.
/// </summary>
public sealed partial class VehiclePreviewViewModel : ObservableObject, IDisposable
{
    /// <summary>A missing mod above this size gets a warning line, because its download takes a while.</summary>
    internal const long LargeModBytes = 100L * 1024 * 1024;

    /// <summary>
    /// A vehicle file of at most this stated size downloads when its page opens, because a player who opens a vehicle
    /// page wants to see the vehicle. Every fetch raises the public download count of the host (RFC 0052), so a larger
    /// or unknown size still waits for the Show click, and a player who leaves the page during the settle delay
    /// downloads nothing. The value is decimal, as the size on the button is.
    /// </summary>
    internal const long AutoFetchVehicleBytes = 5_000_000;

    /// <summary>The default wait between the open of a page and its automatic fetch, so a quick pass over a page does not download.</summary>
    internal static readonly TimeSpan DefaultAutoFetchDelay = TimeSpan.FromMilliseconds(750);

    /// <summary>The size of the still that Discover shows, 16 by 9 like the panel of the page.</summary>
    internal const int StillWidth = 384;

    internal const int StillHeight = 216;

    /// <summary>Goes after the scene key of a still with a texture that failed, so a complete preview of the same scene replaces it.</summary>
    internal const string PartialKeySuffix = "p";

    private readonly IVehiclePreview _service;
    private readonly ModVersionMetadata _release;
    private readonly Guid? _targetInstanceId;
    private readonly LocalizationService _localization;
    private readonly Action _openSettings;
    private readonly PreviewSceneBuilder _builder;
    private readonly PreviewLoadOptions _options;
    private readonly IPreviewThumbnails? _thumbnails;
    private readonly IPngEncoder? _encoder;
    private readonly Action<string>? _stillStored;
    private readonly TimeSpan _autoFetchDelay;
    private readonly bool _autoFetch;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _run;
    private VehiclePreviewPlan? _plan;
    private VehiclePreviewResult? _result;
    private IReadOnlyList<VehiclePreviewMissingMod> _missingMods = [];
    private IReadOnlyList<PreviewNotice> _notices = [];
    private PreviewStep? _step;
    private string? _message;
    private bool _gameFolderInvalid;
    private bool _disposed;

    /// <param name="targetInstanceId">The instance the player would install into, or null when there is none.</param>
    /// <param name="openSettings">Opens the settings, where the player sets the game folder.</param>
    /// <param name="builder">Turns the resolved vehicle into a scene. This instance clears its mesh cache when it is disposed.</param>
    /// <param name="thumbnails">Where a complete preview stores its still for Discover, or null to store none.</param>
    /// <param name="encoder">Encodes the still as a PNG, or null to store none.</param>
    /// <param name="stillStored">Gets the vehicle hash after a new still was stored, on the thread that started the load.</param>
    /// <param name="autoFetchDelay">The wait before a small vehicle file downloads on its own, or null for <see cref="DefaultAutoFetchDelay"/>.</param>
    /// <param name="autoFetch">False when a small vehicle file must also wait for the click, such as after the player cancelled this release.</param>
    public VehiclePreviewViewModel(
        IVehiclePreview service,
        ModVersionMetadata release,
        Guid? targetInstanceId,
        LocalizationService localization,
        Action openSettings,
        PreviewSceneBuilder builder,
        IPreviewThumbnails? thumbnails = null,
        IPngEncoder? encoder = null,
        Action<string>? stillStored = null,
        TimeSpan? autoFetchDelay = null,
        bool autoFetch = true)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _release = release ?? throw new ArgumentNullException(nameof(release));
        _targetInstanceId = targetInstanceId;
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _openSettings = openSettings ?? throw new ArgumentNullException(nameof(openSettings));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _options = new PreviewLoadOptions();
        _thumbnails = thumbnails;
        _encoder = encoder;
        _stillStored = stillStored;
        _autoFetchDelay = autoFetchDelay ?? DefaultAutoFetchDelay;
        _autoFetch = autoFetch;
    }

    internal ModVersionMetadata Release => _release;

    /// <summary>True while the automatic fetch waits out its delay, so a replacement of this preview sends no request yet.</summary>
    internal bool IsWaitingToFetch { get; private set; }

    /// <summary>True once this preview asked the service to download the vehicle file, so a failure can have cost a request to the host.</summary>
    internal bool HasFetchedVehicle { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(IsNoGameFolder))]
    [NotifyPropertyChangedFor(nameof(CanShow))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(ShowText))]
    [NotifyCanExecuteChangedFor(nameof(ShowCommand))]
    [NotifyCanExecuteChangedFor(nameof(FetchModsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private VehiclePreviewState _state;

    /// <summary>The scene the view draws, or null while there is none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScene))]
    [NotifyCanExecuteChangedFor(nameof(ResetViewCommand))]
    private PreviewScene? _scene;

    /// <summary>Goes up by one for each Reset view click. The view frames the vehicle again when it changes.</summary>
    [ObservableProperty]
    private int _resetCount;

    [ObservableProperty]
    private bool _isNoticesExpanded;

    public bool HasScene => Scene is not null;

    public bool IsBusy => State is VehiclePreviewState.Fetching or VehiclePreviewState.Building;

    public bool IsNoGameFolder => State == VehiclePreviewState.NoGameFolder;

    /// <summary>True when a click can start the preview, which is after the preparation found that the vehicle file must be downloaded, or after a stop or a failure.</summary>
    public bool CanShow => State is VehiclePreviewState.Cancelled or VehiclePreviewState.Failed || (State == VehiclePreviewState.Idle && _plan is not null);

    /// <summary>"Show 3D preview (2.4 MB)" with the size of the vehicle file when it must be downloaded.</summary>
    public string ShowText => _plan is { State: VehiclePreviewPlanState.NeedsVehicleFile, VehicleSizeBytes: { } size }
        ? _localization.FormatPreviewShowSize(MainViewModel.SizeText(size))
        : _localization.PreviewShow;

    /// <summary>What the panel says now, or null when the scene speaks for itself.</summary>
    public string? StatusText => State switch
    {
        VehiclePreviewState.Idle when _plan is null => null,
        VehiclePreviewState.Idle => _localization.PreviewIdle,
        VehiclePreviewState.Fetching or VehiclePreviewState.Building => StepText(_step),
        VehiclePreviewState.NoGameFolder => _gameFolderInvalid ? _localization.PreviewGameFolderInvalid : _localization.PreviewNoGameFolder,
        VehiclePreviewState.VehicleDamaged => _localization.PreviewVehicleDamaged,
        VehiclePreviewState.Failed => _localization.FormatPreviewFailed(_message ?? string.Empty),
        VehiclePreviewState.Cancelled => _localization.PreviewCancelled,
        _ => null,
    };

    /// <summary>One line for each mod that the vehicle needs and that is not on this computer.</summary>
    public ObservableCollection<PreviewMissingModLine> MissingModLines { get; } = [];

    public bool HasMissingMods => MissingModLines.Count > 0;

    public bool CanFetchMods => _missingMods.Any(mod => mod.CanFetch);

    /// <summary>
    /// "Load parts (12 MB)" with the summed size of the mods that a click downloads, or "Load parts" when one of
    /// them has no stated size, because a sum of only the known sizes would understate the download.
    /// </summary>
    public string FetchModsText
    {
        get
        {
            var fetched = _missingMods.Where(mod => mod.CanFetch).ToList();
            return fetched.Count > 0 && fetched.All(mod => mod.SizeBytes is not null)
                ? _localization.FormatPreviewLoadPartsSize(MainViewModel.SizeText(fetched.Sum(mod => mod.SizeBytes!.Value)))
                : _localization.PreviewLoadParts;
        }
    }

    /// <summary>What the player should know about the scene, such as a part that shows as a box.</summary>
    public ObservableCollection<string> Notices { get; } = [];

    public bool HasNotices => Notices.Count > 0;

    public string NoticesText => _localization.FormatPreviewNotices(Notices.Count);

    /// <summary>The preparation and the load that run now, so a test can wait for them. It never fails.</summary>
    internal Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>Asks the service what the preview needs, and builds the scene at once when everything is on this computer.</summary>
    internal void Start() => Loading = RunAsync(PrepareAsync);

    [RelayCommand(CanExecute = nameof(CanShow))]
    private Task ShowAsync() => Loading = RunAsync(async token =>
    {
        if (_plan is null)
        {
            await PrepareAsync(token);
            if (State != VehiclePreviewState.Idle)
                return;
        }

        await LoadAsync(fetchVehicle: true, fetchMods: false, token);
    });

    [RelayCommand(CanExecute = nameof(CanFetchModsNow))]
    private Task FetchModsAsync() => Loading = RunAsync(token => LoadAsync(fetchVehicle: false, fetchMods: true, token));

    private bool CanFetchModsNow() => CanFetchMods && !IsBusy;

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _run?.Cancel();

    [RelayCommand(CanExecute = nameof(HasScene))]
    private void ResetView() => ResetCount++;

    [RelayCommand]
    private void OpenSettings() => _openSettings();

    [RelayCommand]
    private void ToggleNotices() => IsNoticesExpanded = !IsNoticesExpanded;

    /// <summary>Stops the running work, drops a result that arrives later, closes the archives and clears the mesh cache.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _result?.Dispose();
        _result = null;
        _builder.MeshCache.Clear();
        Scene = null;
    }

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ShowText));
        ShowMissingMods(_missingMods);
        ShowNotices();
    }

    /// <summary>
    /// Runs one step of the preview with its own cancellation, which a Cancel click or the dispose fires. Every
    /// exception ends as a state, so the page stays usable.
    /// </summary>
    private async Task RunAsync(Func<CancellationToken, Task> body)
    {
        if (_disposed)
            return;

        _run?.Cancel();
        var run = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _run = run;
        try
        {
            await body(run.Token);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
            if (!_disposed && ReferenceEquals(_run, run))
                State = VehiclePreviewState.Cancelled;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!_disposed && ReferenceEquals(_run, run))
            {
                _message = exception.Message;
                State = VehiclePreviewState.Failed;
            }
        }
        finally
        {
            if (ReferenceEquals(_run, run))
                _run = null;

            run.Dispose();
        }
    }

    private async Task PrepareAsync(CancellationToken token)
    {
        var plan = await _service.PrepareAsync(_release, _targetInstanceId, token);
        ThrowWhenStale(token);
        _plan = plan;
        _message = plan.Message;
        _gameFolderInvalid = plan.State == VehiclePreviewPlanState.GameFolderInvalid;
        switch (plan.State)
        {
            case VehiclePreviewPlanState.CanBuildLocally:
                await LoadAsync(fetchVehicle: false, fetchMods: false, token);
                break;
            case VehiclePreviewPlanState.NeedsVehicleFile when _autoFetch && plan.VehicleSizeBytes is { } size && size <= AutoFetchVehicleBytes:
                // The Fetching step shows the Cancel button during the delay, and a cancel or a dispose ends the delay before any download.
                Step(PreviewStep.FetchingVehicle);
                IsWaitingToFetch = true;
                try
                {
                    await Task.Delay(_autoFetchDelay, token);
                }
                finally
                {
                    IsWaitingToFetch = false;
                }

                ThrowWhenStale(token);
                await LoadAsync(fetchVehicle: true, fetchMods: false, token);
                break;
            case VehiclePreviewPlanState.NeedsVehicleFile:
                SetState(VehiclePreviewState.Idle);
                break;
            case VehiclePreviewPlanState.NoGameFolder or VehiclePreviewPlanState.GameFolderInvalid:
                SetState(VehiclePreviewState.NoGameFolder);
                break;
            case VehiclePreviewPlanState.VehicleDamaged:
                SetState(VehiclePreviewState.VehicleDamaged);
                break;
            default:
                SetState(VehiclePreviewState.Failed);
                break;
        }
    }

    private async Task LoadAsync(bool fetchVehicle, bool fetchMods, CancellationToken token)
    {
        var plan = _plan ?? throw new InvalidOperationException("The preview loads only after a preparation.");
        Step(fetchVehicle ? PreviewStep.FetchingVehicle : fetchMods ? PreviewStep.FetchingMods : PreviewStep.ReadingParts);
        HasFetchedVehicle |= fetchVehicle;
        var progress = new StageProgress(stage =>
        {
            if (!token.IsCancellationRequested && stage != VehiclePreviewStage.Done)
                Step(StepOf(stage));
        });

        VehiclePreviewResult result;
        try
        {
            result = await _service.LoadAsync(plan, fetchVehicle, fetchMods, progress, token);
        }
        finally
        {
            progress.Close();
        }

        if (_disposed || token.IsCancellationRequested)
        {
            // A result that arrives after a cancel or a dispose holds archives open, which nobody else closes.
            result.Dispose();
            ThrowWhenStale(token);
        }

        if (!result.IsReady)
        {
            _message = result.Message;
            _gameFolderInvalid = result.State == VehiclePreviewLoadState.GameFolderInvalid;
            result.Dispose();
            SetState(result.State switch
            {
                VehiclePreviewLoadState.NoGameFolder or VehiclePreviewLoadState.GameFolderInvalid => VehiclePreviewState.NoGameFolder,
                VehiclePreviewLoadState.NeedsVehicleFile => VehiclePreviewState.Idle,
                VehiclePreviewLoadState.VehicleDamaged => VehiclePreviewState.VehicleDamaged,
                _ => VehiclePreviewState.Failed,
            });
            return;
        }

        var previous = _result;
        _result = result;
        previous?.Dispose();
        _notices = result.Notices;

        Step(PreviewStep.BuildingModel);
        var (geometry, geometryNotices) = await _builder.BuildGeometryAsync(result.Vehicle, result.Assets, _options, token);
        ThrowWhenStale(token);
        _notices = [.. _notices, .. geometryNotices];
        Scene = geometry;
        ShowMissingMods(result.MissingMods);
        ShowNotices();

        Step(PreviewStep.LoadingTextures);
        var (textured, textureNotices) = await _builder.AddTexturesAsync(geometry, result.Vehicle, result.Assets, _options, token);
        ThrowWhenStale(token);
        _notices = [.. _notices, .. textureNotices];
        Scene = textured;
        ShowNotices();
        SetState(result.MissingMods.Count > 0 ? VehiclePreviewState.NeedsMods : VehiclePreviewState.Ready);
        await StoreStillAsync(textured, result, textureNotices.Any(notice => notice.Kind == PreviewNoticeKind.AssetFailed), token);
        await AddNormalsAsync(textured, result.Vehicle, result.Assets, token);
    }

    /// <summary>
    /// Gives the scene its normal maps. Only the High frame of a view at rest reads them, so the Standard frames and
    /// the still do not wait for them. A failure or a cancel keeps the scene without normal maps and never changes
    /// the panel.
    /// </summary>
    private async Task AddNormalsAsync(PreviewScene textured, ResolvedVehicle vehicle, IPreviewAssetSource assets, CancellationToken token)
    {
        try
        {
            var scene = await _builder.AddNormalsAsync(textured, vehicle, assets, _options, token);
            if (!_disposed && !token.IsCancellationRequested && ReferenceEquals(Scene, textured) && !ReferenceEquals(scene, textured))
                Scene = scene;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A Standard frame does not read a normal map, and a High frame without one shows the same pixels.
        }
    }

    /// <summary>
    /// Stores a still of the scene for Discover when the scene shows the whole vehicle and the stored still, if any,
    /// was made from another scene key. A scene with a box, a missing mod or no draw gets no still, because Discover
    /// would show it as the vehicle. A failure only means that Discover keeps the icon, so it never changes the panel.
    /// </summary>
    /// <param name="partial">
    /// True when a texture failed to read or decode and keeps its flat color. That can be a failure of this one read,
    /// so the still goes under the scene key with <see cref="PartialKeySuffix"/>. It never replaces a complete still of
    /// the same scene, and the next complete preview replaces it, while a vehicle whose texture always fails still
    /// gets a still.
    /// </param>
    private async Task StoreStillAsync(PreviewScene scene, VehiclePreviewResult result, bool partial, CancellationToken token)
    {
        if (_thumbnails is not { } thumbnails
            || _encoder is not { } encoder
            || State != VehiclePreviewState.Ready
            || scene.Draws.Count == 0
            || scene.Placeholders.Count > 0
            || result is not { VehicleSha256: { } sha256, GameBuild: { } gameBuild, SceneKey: { } sceneKey })
            return;

        try
        {
            var stored = await Task.Run(
                () =>
                {
                    var key = partial ? sceneKey + PartialKeySuffix : sceneKey;
                    if (!thumbnails.NeedsUpdate(sha256, gameBuild, key) || (partial && !thumbnails.NeedsUpdate(sha256, gameBuild, sceneKey)))
                        return false;

                    thumbnails.Store(sha256, gameBuild, key, encoder.Encode(RenderStill(scene, token)));
                    return true;
                },
                token);
            if (stored && !_disposed)
                _stillStored?.Invoke(sha256);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A cancel, a dispose or a failed encode leaves the old still or none, and the next complete preview tries again.
        }
    }

    /// <summary>Renders the scene from the default camera angles, framed as the page frames it, on a renderer of its own.</summary>
    internal static FrameBuffer RenderStill(PreviewScene scene, CancellationToken token)
    {
        var camera = new OrbitCamera();
        camera.Frame(scene.Bounds, (float)StillWidth / StillHeight);
        var frame = new FrameBuffer(StillWidth, StillHeight);

        // The renderer is dropped after this one frame, so it keeps no vertex cache and no work buffers.
        new SoftwareRenderer().Render(scene, camera, frame, new RenderOptions { WorldCacheBytes = 0, WorkBufferBytes = 0 }, token);
        return frame;
    }

    private void ThrowWhenStale(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_disposed)
            throw new OperationCanceledException(token);
    }

    private void Step(PreviewStep step)
    {
        _step = step;
        SetState(step is PreviewStep.FetchingVehicle or PreviewStep.FetchingMods ? VehiclePreviewState.Fetching : VehiclePreviewState.Building);
    }

    private void SetState(VehiclePreviewState state)
    {
        State = state;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ShowText));
        OnPropertyChanged(nameof(CanShow));
        ShowCommand.NotifyCanExecuteChanged();
        FetchModsCommand.NotifyCanExecuteChanged();
    }

    private void ShowMissingMods(IReadOnlyList<VehiclePreviewMissingMod> missing)
    {
        _missingMods = missing;
        MissingModLines.Clear();
        foreach (var mod in missing)
        {
            var name = mod.Version is { } version ? $"{mod.ModId} {version}" : mod.ModId;
            var text = mod.PartCount is { } count
                ? _localization.FormatPreviewMissingModParts(count, name)
                : _localization.FormatPreviewMissingMod(name);
            var warning = mod.SizeBytes is { } size && size > LargeModBytes
                ? _localization.FormatPreviewLargeMod(name, MainViewModel.SizeText(size))
                : null;
            MissingModLines.Add(new PreviewMissingModLine(text, warning));
        }

        OnPropertyChanged(nameof(HasMissingMods));
        OnPropertyChanged(nameof(CanFetchMods));
        OnPropertyChanged(nameof(FetchModsText));
        FetchModsCommand.NotifyCanExecuteChanged();
    }

    private void ShowNotices()
    {
        Notices.Clear();
        foreach (var notice in _notices)
            Notices.Add(_localization.FormatPreviewNotice(NoticeKindText(notice.Kind), notice.Subject));

        OnPropertyChanged(nameof(HasNotices));
        OnPropertyChanged(nameof(NoticesText));
    }

    private string NoticeKindText(PreviewNoticeKind kind) => kind switch
    {
        PreviewNoticeKind.DuplicateId => _localization.PreviewNoticeDuplicateId,
        PreviewNoticeKind.PatchMod => _localization.PreviewNoticePatchMod,
        PreviewNoticeKind.UnsupportedTexture => _localization.PreviewNoticeUnsupportedTexture,
        PreviewNoticeKind.Budget => _localization.PreviewNoticeBudget,
        PreviewNoticeKind.Cycle => _localization.PreviewNoticeCycle,
        PreviewNoticeKind.MissingSource => _localization.PreviewNoticeMissingSource,
        PreviewNoticeKind.PlannerFallback => _localization.PreviewNoticePlannerFallback,
        _ => _localization.PreviewNoticeAssetFailed,
    };

    private string? StepText(PreviewStep? step) => step switch
    {
        PreviewStep.FetchingVehicle => _localization.PreviewStageFetchingVehicle,
        PreviewStep.ReadingParts => _localization.PreviewStageReadingParts,
        PreviewStep.FetchingMods => _localization.PreviewStageFetchingMods,
        PreviewStep.BuildingModel => _localization.PreviewStageBuildingModel,
        PreviewStep.LoadingTextures => _localization.PreviewStageLoadingTextures,
        _ => null,
    };

    private static PreviewStep StepOf(VehiclePreviewStage stage) => stage switch
    {
        VehiclePreviewStage.FetchingVehicle => PreviewStep.FetchingVehicle,
        VehiclePreviewStage.FetchingMods => PreviewStep.FetchingMods,
        _ => PreviewStep.ReadingParts,
    };

    private enum PreviewStep
    {
        FetchingVehicle,
        ReadingParts,
        FetchingMods,
        BuildingModel,
        LoadingTextures,
    }

    /// <summary>
    /// Reports a stage on the thread that started the load, which is the UI thread in the App. Without a
    /// synchronization context, as in a test, it reports at once. A context that does not keep the order of its posts,
    /// such as the one of the test runner, can deliver a stage after the load ended, so a report after
    /// <see cref="Close"/> is dropped, and the lock keeps a report that is under way from ending after it.
    /// </summary>
    private sealed class StageProgress(Action<VehiclePreviewStage> report) : IProgress<VehiclePreviewStage>
    {
        private readonly SynchronizationContext? _context = SynchronizationContext.Current;
        private readonly Lock _gate = new();
        private bool _closed;

        public void Report(VehiclePreviewStage value)
        {
            if (_context is null)
                Deliver(value);
            else
                _context.Post(_ => Deliver(value), null);
        }

        public void Close()
        {
            lock (_gate)
                _closed = true;
        }

        private void Deliver(VehiclePreviewStage value)
        {
            lock (_gate)
            {
                if (!_closed)
                    report(value);
            }
        }
    }
}

/// <summary>One mod that the vehicle needs and that is not on this computer, with a warning when its download is large.</summary>
public sealed record PreviewMissingModLine(string Text, string? Warning);
