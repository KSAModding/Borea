using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Borea.App.Localization;
using Borea.App.ViewModels;
using Borea.Core.History;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

public sealed class ToastViewModelTests
{
    private const string OwnId = ViewModelHarness.FakeSpaceDock.OwnId;
    private const string ArchiveHost = "archives.test";
    private const string StarMapSha256 = "BC9510994DAF56FD826B734EF0F2704C8E4D1B91BCF010C76733E168CC23604A";

    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

    private readonly LocalizationService _localization = new(CultureInfo.GetCultureInfo("en"));

    private readonly ManualTimeProvider _time = new();

    [Theory]
    [InlineData(TaskKind.ModInstall, "AdvancedFlightComputer", null, "AdvancedFlightComputer added to Main")]
    [InlineData(TaskKind.PackInstall, "Starter Pack", null, "Starter Pack added to Main")]
    [InlineData(TaskKind.ModRemoval, "MeasureTools", null, "MeasureTools removed from Main")]
    [InlineData(TaskKind.LoaderInstall, "StarMap", "0.4.6", "StarMap 0.4.6 installed")]
    [InlineData(TaskKind.ManualReplace, "OldHud", null, "OldHud replaced in Main")]
    [InlineData(TaskKind.ModListImport, "Copy of Main", null, "Instance Copy of Main created")]
    public void FinishedTask_SaysWhatChangedAndWhere(TaskKind kind, string subject, string? version, string message)
    {
        var viewModel = ViewModel();

        var toast = End(viewModel, kind, TaskState.Finished, subject, version);

        Assert.Equal(message, toast.Message);
        Assert.True(toast.IsFinished);
        Assert.False(toast.HasDetail);
        Assert.False(toast.CanShowDetails);
    }

    [Theory]
    [InlineData(TaskKind.IndexRefresh, null, "Could not refresh the content index")]
    [InlineData(TaskKind.ModInstall, "AdvancedFlightComputer", "Could not install AdvancedFlightComputer")]
    [InlineData(TaskKind.Update, "MeasureTools", "Could not update MeasureTools")]
    [InlineData(TaskKind.UpdateAll, null, "Could not update the mods in Main")]
    [InlineData(TaskKind.ModRemoval, "MeasureTools", "Could not remove MeasureTools")]
    public void FailedTask_ShowsItsReasonAndShowDetails(TaskKind kind, string? subject, string message)
    {
        var viewModel = ViewModel();

        var toast = End(viewModel, kind, TaskState.Failed, subject, failureReason: "The host is offline.");

        Assert.Equal(message, toast.Message);
        Assert.Equal("The host is offline.", toast.Detail);
        Assert.True(toast.IsFailed);
        Assert.True(toast.CanShowDetails);
        Assert.Equal($"{message}{Environment.NewLine}The host is offline.", viewModel.Toasts.Announcement);
    }

    [Fact]
    public void StoppedTask_ShowsTheCountOnlyWhenModsWereChanged()
    {
        var viewModel = ViewModel();

        var partial = End(viewModel, TaskKind.ModInstall, TaskState.Stopped, stoppedAfter: (2, 3));
        var updates = End(viewModel, TaskKind.UpdateAll, TaskState.Stopped, subject: null, stoppedAfter: (1, 2));
        var nothing = End(viewModel, TaskKind.UpdateAll, TaskState.Stopped, subject: null);

        Assert.Equal("Install of AdvancedFlightComputer stopped", partial.Message);
        Assert.Equal("2 of 3 mods installed", partial.Detail);
        Assert.True(partial.CanShowDetails);
        Assert.Equal("1 of 2 mods updated", updates.Detail);
        Assert.Equal("Update of the mods in Main stopped", nothing.Message);
        Assert.False(nothing.HasDetail);
    }

    [Theory]
    [InlineData(TaskKind.ModListImport, TaskState.Stopped)]
    [InlineData(TaskKind.ManualReplace, TaskState.Stopped)]
    [InlineData(TaskKind.Update, TaskState.Finished)]
    [InlineData(TaskKind.UpdateAll, TaskState.Finished)]
    public void TaskWithNothingToSay_ShowsNoToast(TaskKind kind, TaskState state)
    {
        var viewModel = ViewModel();

        viewModel.Tasks.End(viewModel.Tasks.Start(kind, "Main", Guid.NewGuid(), "Main", null, null, TaskState.Running), state);

        Assert.Empty(viewModel.Toasts.Items);
    }

    [Fact]
    public async Task LanguageSwitch_TranslatesTheShownToasts()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        harness.ViewModel.Toasts.Clock = _time;
        var toast = End(harness.ViewModel, TaskKind.PackInstall, TaskState.Stopped, "Starter Pack", stoppedAfter: (1, 2));

        harness.Localization.TrySetCulture("de");

        Assert.Equal("Installation von Starter Pack gestoppt", toast.Message);
        Assert.Equal("1 von 2 Mods installiert", toast.Detail);
    }

    [Fact]
    public async Task FinishedIndexRefresh_ShowsNoToast()
    {
        using var harness = await ViewModelHarness.CreateAsync();

        Assert.Equal(TaskState.Finished, Assert.Single(harness.ViewModel.Tasks.History).State);
        Assert.Empty(harness.ViewModel.Toasts.Items);
    }

    [Fact]
    public async Task FailedIndexRefresh_ShowsItsReason()
    {
        using var harness = await ViewModelHarness.CreateAsync(indexOffline: true);

        var toast = Assert.Single(harness.ViewModel.Toasts.Items);
        Assert.Equal(harness.Localization.ToastIndexRefreshFailed, toast.Message);
        Assert.Equal(ViewModelHarness.OfflineMessage, toast.Detail);
        Assert.True(toast.CanShowDetails);
        Assert.False(toast.CanOpenInstance);
    }

    [Theory]
    [InlineData(TaskState.Finished, 5)]
    [InlineData(TaskState.Stopped, 10)]
    [InlineData(TaskState.Failed, 10)]
    public async Task Toast_ClosesAfterItsTime(TaskState state, int seconds)
    {
        var viewModel = ViewModel();
        var toast = End(viewModel, TaskKind.ModInstall, state, failureReason: "The host is offline.");

        _time.Advance(TimeSpan.FromSeconds(seconds) - TimeSpan.FromMilliseconds(1));
        Assert.Contains(toast, viewModel.Toasts.Items);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        await WaitUntilAsync(() => !viewModel.Toasts.Items.Contains(toast));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Toast_WhilePointedAtOrFocused_StaysAndStartsItsTimeAgainWhenLeft(bool byFocus)
    {
        var viewModel = ViewModel();
        var toast = End(viewModel, TaskKind.ModRemoval, TaskState.Finished);
        _time.Advance(TimeSpan.FromSeconds(4));

        Hold(toast, byFocus, held: true);
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Contains(toast, viewModel.Toasts.Items);

        Hold(toast, byFocus, held: false);
        _time.Advance(TimeSpan.FromSeconds(4));
        Assert.Contains(toast, viewModel.Toasts.Items);

        _time.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => !viewModel.Toasts.Items.Contains(toast));
    }

    [Fact]
    public void FourthToast_ClosesTheOldestAtOnce()
    {
        var viewModel = ViewModel();

        var toasts = Enumerable.Range(1, 4).Select(index => End(viewModel, TaskKind.ModRemoval, TaskState.Finished, $"Mod{index}")).ToList();

        Assert.Equal(toasts.Skip(1), viewModel.Toasts.Items);
        Assert.Equal("Mod4 removed from Main", viewModel.Toasts.Items[^1].Message);
    }

    [Fact]
    public void Close_RemovesTheToastAndItsAnnouncement()
    {
        var viewModel = ViewModel();
        var older = End(viewModel, TaskKind.ModInstall, TaskState.Finished, "HudCore");
        var newest = End(viewModel, TaskKind.ModInstall, TaskState.Finished);

        older.CloseCommand.Execute(null);
        Assert.Equal(newest.Message, viewModel.Toasts.Announcement);

        newest.CloseCommand.Execute(null);
        Assert.Empty(viewModel.Toasts.Items);
        Assert.Null(viewModel.Toasts.Announcement);
    }

    [Fact]
    public async Task FinishedUpdate_NamesTheNewVersion_AndOpenInstanceOpensItsPage()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("1.1.0")]);
        var viewModel = harness.ViewModel;
        viewModel.Toasts.Clock = _time;
        var instance = await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.Single().Items.Single();
        await row.UpdateCommand.ExecuteAsync(null);
        await row.ConfirmUpdateCommand.ExecuteAsync(null);
        viewModel.SetMainWindowHome();

        var toast = Assert.Single(viewModel.Toasts.Items);
        Assert.Equal(harness.Localization.FormatToastUpdated(row.Name, "1.1.0", "Main"), toast.Message);
        Assert.True(toast.CanOpenInstance);
        Assert.False(toast.CanShowDetails);

        await toast.OpenInstanceCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowInstance);
        Assert.Equal(instance.InstanceId, viewModel.SelectedInstance?.InstanceId);
        Assert.Empty(viewModel.Toasts.Items);
    }

    [Fact]
    public async Task FinishedUpdateAll_CountsTheUpdatedModsInOneToast()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        harness.SpaceDock.Releases.AddRange([Release("1.0.0"), Release("1.1.0")]);
        var viewModel = harness.ViewModel;
        viewModel.Toasts.Clock = _time;
        await InstalledContent.AddAsync(harness, OwnId, activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.UpdateAll!.UpdateCommand.ExecuteAsync(null);
        await viewModel.UpdateAll.ConfirmUpdateCommand.ExecuteAsync(null);

        Assert.Equal("1 mod updated in Main", Assert.Single(viewModel.Toasts.Items).Message);
        Assert.Equal("2 mods updated in Main", harness.Localization.FormatToastUpdatedAll(2, "Main"));
    }

    [Fact]
    public async Task Removal_ShowsTheModAndTheInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.Toasts.Clock = _time;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        var afc = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");

        afc.BeginRemoveCommand.Execute(null);
        await afc.ConfirmRemoveCommand.ExecuteAsync(null);

        var toast = Assert.Single(viewModel.Toasts.Items);
        Assert.Equal(harness.Localization.FormatToastRemoved(afc.Name, "Main"), toast.Message);
        Assert.True(toast.CanOpenInstance);

        await harness.Services.Instances.DeleteAsync(viewModel.Instances.Single().InstanceId);
        await viewModel.LoadAsync();

        Assert.False(toast.CanOpenInstance);
        Assert.False(toast.HasActions);
    }

    [Fact]
    public async Task FinishedDuplicate_OpenInstanceOpensTheNewInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.Toasts.Clock = _time;
        await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        await viewModel.Instances.Single().DuplicateCommand.ExecuteAsync(null);

        await viewModel.ModListImport!.ConfirmCommand.ExecuteAsync(null);

        var toast = Assert.Single(viewModel.Toasts.Items);
        Assert.Equal("Instance Main (copy) created", toast.Message);
        Assert.True(toast.CanOpenInstance);

        await toast.OpenInstanceCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowInstance);
        Assert.Equal("Main (copy)", viewModel.SelectedInstance?.Name);
    }

    [Fact]
    public async Task FinishedLoaderInstall_NamesTheVersion()
    {
        var archive = LoaderArchive();
        using var harness = await ViewModelHarness.CreateAsync(
            services =>
            {
                var game = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Game")).FullName;
                return services.SettingsRepository.SaveAsync(services.Settings.WithGameDirectory(game));
            },
            respond: request => request.RequestUri?.AbsolutePath.EndsWith("/StarMap-0.4.6.zip", StringComparison.Ordinal) == true
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) }
                : null,
            editSnapshot: snapshot => snapshot
                .Replace(StarMapSha256, Convert.ToHexString(SHA256.HashData(archive)), StringComparison.Ordinal)
                .Replace("\"size\": 891020", $"\"size\": {archive.Length}", StringComparison.Ordinal));
        var viewModel = harness.ViewModel;
        viewModel.Toasts.Clock = _time;
        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);

        await viewModel.InstallLoaderCommand.ExecuteAsync(null);

        Assert.Null(viewModel.SetupError);
        var toast = Assert.Single(viewModel.Toasts.Items);
        Assert.Equal("StarMap 0.4.6 installed", toast.Message);
        Assert.False(toast.HasActions);
    }

    [Fact]
    public async Task FailedInstall_ShowDetailsOpensTheTasksPageWithThatTask()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.Toasts.Clock = _time;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        var item = viewModel.DiscoverItems.Single(row => row.ModId == "AdvancedFlightComputer");
        await item.InstallCommand.ExecuteAsync(null);
        await item.ConfirmInstallCommand.ExecuteAsync(null);

        var failed = viewModel.Tasks.History[0];
        var toast = Assert.Single(viewModel.Toasts.Items);
        Assert.Same(failed, toast.TaskItem);
        Assert.Equal(harness.Localization.FormatToastInstallFailed(item.Name), toast.Message);
        Assert.Equal(item.InstallError, toast.Detail);
        Assert.True(toast.CanShowDetails);

        toast.ShowDetailsCommand.Execute(null);

        Assert.True(viewModel.IsTasksOpen);
        Assert.Same(failed, viewModel.TaskInView);
        Assert.Empty(viewModel.Toasts.Items);
    }

    private MainViewModel ViewModel()
    {
        var viewModel = new MainViewModel(_localization);
        viewModel.Toasts.Clock = _time;
        return viewModel;
    }

    private static ToastItem End(MainViewModel viewModel, TaskKind kind, TaskState state, string? subject = "AdvancedFlightComputer", string? version = null, string? failureReason = null, (int Completed, int Total) stoppedAfter = default)
    {
        var task = viewModel.Tasks.Start(kind, subject, Guid.NewGuid(), "Main", subject, null, TaskState.Running);
        task.NewVersion = version;
        task.StoppedAfter = stoppedAfter;
        viewModel.Tasks.End(task, state, failureReason);
        return viewModel.Toasts.Items[^1];
    }

    private static void Hold(ToastItem toast, bool byFocus, bool held)
    {
        if (byFocus)
            toast.SetFocusWithin(held);
        else
            toast.SetPointerOver(held);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(WaitLimit);
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private static ModVersionMetadata Release(string version) => new(
        specVersion: 1,
        modId: OwnId,
        version: ModVersion.Parse(version),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: DateTimeOffset.UnixEpoch,
        gameMin: "2026.1.1.1",
        gameMinRevision: 1,
        download: new DownloadInfo($"https://{ArchiveHost}/{OwnId}/{version}.zip", sha256: null, sizeBytes: null, contentType: "application/zip"),
        installSizeBytes: null,
        dependencies: []);

    private static byte[] LoaderArchive()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("StarMap.exe").Open());
            writer.Write("MZ");
        }

        return buffer.ToArray();
    }

    private static HttpResponseMessage? ServeArchive(HttpRequestMessage request)
    {
        if (request.RequestUri?.Host != ArchiveHost)
            return null;

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("mod.toml").Open(), Encoding.UTF8);
            writer.Write($"name = \"{OwnId}\"");
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(buffer.ToArray()) };
    }

    /// <summary>A clock that moves only when the test moves it, and then runs the timers that came due.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            lock (_timers)
                return _now;
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            List<ManualTimer> due;
            lock (_timers)
            {
                _now += by;
                due = _timers.Where(timer => timer.DueAt <= _now).ToList();
                _timers.RemoveAll(due.Contains);
            }

            foreach (var timer in due)
                timer.Fire();
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            public DateTimeOffset DueAt { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._timers)
                {
                    owner._timers.Remove(this);
                    if (dueTime == Timeout.InfiniteTimeSpan)
                        return true;

                    DueAt = owner._now + dueTime;
                    owner._timers.Add(this);
                }

                return true;
            }

            public void Fire() => callback(state);

            public void Dispose()
            {
                lock (owner._timers)
                    owner._timers.Remove(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
