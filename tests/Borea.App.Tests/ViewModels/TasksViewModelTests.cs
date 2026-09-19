using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Borea.App.Formatting;
using Borea.App.ViewModels;
using Borea.Core.History;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Preferences;

namespace Borea.App.Tests.ViewModels;

public sealed class TasksViewModelTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly byte[] _archive = Archive();

    private readonly TaskCompletionSource _halfway = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task TasksDrawer_OpensOverThePage_AndClosesOnToggleCloseAndNavigation()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.SetMainWindowLibraryCommand.Execute(null);

        viewModel.ToggleTasksCommand.Execute(null);
        Assert.True(viewModel.IsTasksOpen);
        Assert.True(viewModel.CurrentWindowLibrary);

        viewModel.ToggleTasksCommand.Execute(null);
        Assert.False(viewModel.IsTasksOpen);

        viewModel.ToggleTasksCommand.Execute(null);
        viewModel.CloseTasksCommand.Execute(null);
        Assert.False(viewModel.IsTasksOpen);

        viewModel.ToggleTasksCommand.Execute(null);
        viewModel.SetMainWindowDiscoverCommand.Execute(null);
        Assert.False(viewModel.IsTasksOpen);
        Assert.True(viewModel.CurrentWindowDiscover);
    }

    [Fact]
    public async Task RunningInstall_ShowsItsProgress_AndMovesToTheHistoryWhenItEnds()
    {
        using var harness = await CreateAsync();
        var tasks = harness.ViewModel.Tasks;
        var (_, item) = await ConfirmingInstallAsync(harness);

        var install = item.ConfirmInstallCommand.ExecuteAsync(null);
        await _halfway.Task.WaitAsync(Timeout);
        var task = Assert.Single(tasks.Running);
        await WaitUntilAsync(() => task.HasProgress);

        Assert.Equal(harness.Localization.FormatTaskInstall(item.Name), task.Title);
        Assert.Equal("Main", task.InstanceName);
        Assert.Equal(TaskState.Running, task.State);
        Assert.True(task.Progress > 0);
        Assert.NotNull(task.Step);
        Assert.Equal(item.ProgressStatus, task.Step);

        _gate.SetResult();
        await install;

        Assert.Empty(tasks.Running);
        Assert.Same(task, tasks.History[0]);
        Assert.Equal(TaskState.Finished, task.State);
        Assert.Null(task.Step);
        Assert.False(task.CanRetry);
    }

    [Fact]
    public async Task FailedIndexRefresh_ShowsItsReason_AndTryAgainRefreshesAgain()
    {
        using var harness = await ViewModelHarness.CreateAsync(indexOffline: true);
        var tasks = harness.ViewModel.Tasks;

        var failed = Assert.Single(tasks.History);
        Assert.Equal(harness.Localization.TaskIndexRefresh, failed.Title);
        Assert.Equal(TaskState.Failed, failed.State);
        Assert.Equal(ViewModelHarness.OfflineMessage, failed.FailureReason);
        Assert.True(failed.CanRetry);

        harness.IndexOffline = false;
        await failed.RetryCommand.ExecuteAsync(null);

        Assert.Equal(2, tasks.History.Count);
        Assert.Equal(TaskState.Finished, tasks.History[0].State);
        Assert.False(harness.ViewModel.IsIndexUnreachable);
    }

    [Fact]
    public async Task FailedInstall_TryAgain_PlansTheSameInstallOnItsPage()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var (_, item) = await ConfirmingInstallAsync(harness);
        await item.ConfirmInstallCommand.ExecuteAsync(null);

        var failed = viewModel.Tasks.History[0];
        Assert.Equal(TaskState.Failed, failed.State);
        Assert.Equal(item.InstallError, failed.FailureReason);
        Assert.True(failed.CanRetry);

        viewModel.ToggleTasksCommand.Execute(null);
        await failed.RetryCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsTasksOpen);
        Assert.True(viewModel.CurrentWindowContent);
        Assert.Same(item, viewModel.SelectedContent);
        Assert.True(item.IsConfirmingInstall);
        Assert.Empty(viewModel.Tasks.Running);
    }

    [Fact]
    public async Task FailedVersionInstall_TryAgain_KeepsTheVersionThroughTheConfirmation()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        var afc = viewModel.DiscoverItems.Single(row => row.ModId == "AdvancedFlightComputer");
        await afc.OpenCommand.ExecuteAsync(null);
        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);
        var newest = viewModel.ContentVersions[0];
        var oldest = viewModel.ContentVersions.Last();
        await oldest.InstallCommand.ExecuteAsync(null);
        await oldest.ConfirmInstallCommand.ExecuteAsync(null);

        var failed = viewModel.Tasks.History[0];
        Assert.Equal(TaskState.Failed, failed.State);
        Assert.Equal(harness.Localization.FormatTaskInstall($"{afc.Name} {oldest.Version}"), failed.Title);

        await failed.RetryCommand.ExecuteAsync(null);
        Assert.True(afc.IsConfirmingInstall);
        await afc.ConfirmInstallCommand.ExecuteAsync(null);

        var again = viewModel.Tasks.History[0];
        Assert.NotSame(failed, again);
        Assert.Equal(TaskState.Failed, again.State);
        Assert.Equal(failed.Title, again.Title);
        Assert.Equal(oldest.Version, again.Version);
        Assert.DoesNotContain(harness.Requests, uri => uri.AbsolutePath.Contains($"/v{newest.Version}/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailedInstall_TryAgain_KeepsItsInstanceWhenAnotherIsActive()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var (instance, item) = await ConfirmingInstallAsync(harness);
        await item.ConfirmInstallCommand.ExecuteAsync(null);
        var failed = viewModel.Tasks.History[0];
        var second = (await harness.Services.Instances.CreateAsync("Second", InstanceSource.Custom.Value)).Instance;
        await viewModel.ActivateInstanceAsync(second.InstanceId);

        await failed.RetryCommand.ExecuteAsync(null);
        await item.ConfirmInstallCommand.ExecuteAsync(null);

        var again = viewModel.Tasks.History[0];
        Assert.NotSame(failed, again);
        Assert.Equal(instance.InstanceId, again.InstanceId);
        Assert.Equal("Main", again.InstanceName);
        Assert.Equal(second.InstanceId, viewModel.ActiveInstance?.InstanceId);
    }

    [Fact]
    public async Task TryAgain_IntoADeletedInstance_FailsAndSaysWhy()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var (instance, item) = await ConfirmingInstallAsync(harness);
        await item.ConfirmInstallCommand.ExecuteAsync(null);
        var failed = viewModel.Tasks.History[0];
        await harness.Services.Instances.DeleteAsync(instance.InstanceId);
        await viewModel.LoadAsync();
        viewModel.ToggleTasksCommand.Execute(null);

        await failed.RetryCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsTasksOpen);
        Assert.False(item.IsConfirmingInstall);
        var again = viewModel.Tasks.History[0];
        Assert.Equal(TaskState.Failed, again.State);
        Assert.Equal(harness.Localization.LaunchInstanceMissing, again.FailureReason);
        Assert.Equal("Main", again.InstanceName);
    }

    [Fact]
    public async Task FailedUpdate_ShowsInTheHistory_AndTryAgainPlansItOnTheInstancePage()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea, version: "0.7.4");
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.Single().Items.Single();
        await row.UpdateCommand.ExecuteAsync(null);
        await row.ConfirmUpdateCommand.ExecuteAsync(null);

        var failed = viewModel.Tasks.History[0];
        Assert.Equal(harness.Localization.FormatTaskUpdate(row.Name), failed.Title);
        Assert.Equal("Main", failed.InstanceName);
        Assert.Equal(TaskState.Failed, failed.State);
        Assert.Equal(viewModel.ContentGroups.Single().Items.Single().InstallError, failed.FailureReason);
        Assert.True(failed.CanRetry);

        viewModel.ToggleTasksCommand.Execute(null);
        await failed.RetryCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsTasksOpen);
        Assert.True(viewModel.CurrentWindowInstance);
        Assert.True(viewModel.ContentGroups.Single().Items.Single().IsConfirmingUpdate);
        Assert.Empty(viewModel.Tasks.Running);
    }

    [Fact]
    public async Task Removal_MovesToTheHistoryWhenItEnds()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        var afc = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");
        await afc.OpenCommand.ExecuteAsync(null);

        afc.BeginRemoveCommand.Execute(null);
        await afc.ConfirmRemoveCommand.ExecuteAsync(null);

        var task = viewModel.Tasks.History[0];
        Assert.Equal(TaskKind.ModRemoval, task.Kind);
        Assert.Equal(harness.Localization.FormatTaskRemove(afc.Name), task.Title);
        Assert.Equal("Main", task.InstanceName);
        Assert.Equal(TaskState.Finished, task.State);
        Assert.False(task.HasRetry);
        Assert.Empty(viewModel.Tasks.Running);
    }

    [Fact]
    public async Task StopOnThePage_StopsTheInstallLikeTheRow_AndShowsItAsStopped()
    {
        using var harness = await CreateAsync();
        var tasks = harness.ViewModel.Tasks;
        var (instance, item) = await ConfirmingInstallAsync(harness);

        var install = item.ConfirmInstallCommand.ExecuteAsync(null);
        await _halfway.Task.WaitAsync(Timeout);
        var task = Assert.Single(tasks.Running);
        Assert.True(task.CanStop);
        Assert.Same(item.Run, task.Run);
        task.Run!.StopCommand.Execute(null);
        await install;

        Assert.Equal(harness.Localization.InstallStopped, item.ProgressStatus);
        Assert.Empty(tasks.Running);
        Assert.Same(task, tasks.History[0]);
        Assert.Equal(TaskState.Stopped, task.State);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
    }

    [Fact]
    public async Task NavigationProgress_ShowsOnlyWhileATaskRuns()
    {
        using var harness = await CreateAsync();
        var tasks = harness.ViewModel.Tasks;
        var (_, item) = await ConfirmingInstallAsync(harness);
        Assert.False(tasks.IsBusy);

        var install = item.ConfirmInstallCommand.ExecuteAsync(null);
        Assert.True(tasks.IsBusy);

        await _halfway.Task.WaitAsync(Timeout);
        await WaitUntilAsync(() => tasks.HasProgress);
        Assert.True(tasks.Progress > 0);

        _gate.SetResult();
        await install;

        Assert.False(tasks.IsBusy);
        Assert.False(tasks.HasProgress);
    }

    [Fact]
    public async Task NavigationProgress_TaskWithoutAFraction_ShowsNoProgress()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var tasks = harness.ViewModel.Tasks;

        var task = tasks.Start(TaskKind.IndexRefresh, null, null, null, null, null, TaskState.Running);

        Assert.True(tasks.IsBusy);
        Assert.False(tasks.HasProgress);

        tasks.End(task, TaskState.Finished);

        Assert.False(tasks.IsBusy);
        Assert.False(tasks.HasProgress);
    }

    [Fact]
    public async Task History_SurvivesARestart()
    {
        using var harness = await ViewModelHarness.CreateAsync(indexOffline: true);
        await harness.ViewModel.Tasks.WhenSavedAsync();

        var restarted = new MainViewModel(harness.Localization, new RegionalFormatService(harness.Localization), appPreferencesRepository: null, AppPreferences.Empty, harness.Services);
        await restarted.Tasks.LoadAsync();

        var entry = Assert.Single(restarted.Tasks.History);
        Assert.Equal(TaskKind.IndexRefresh, entry.Kind);
        Assert.Equal(TaskState.Failed, entry.State);
        Assert.Equal(ViewModelHarness.OfflineMessage, entry.FailureReason);
        Assert.True(entry.CanRetry);
    }

    [Fact]
    public async Task BrokenHistoryFile_StartsWithAnEmptyHistory()
    {
        using var harness = await ViewModelHarness.CreateAsync(seed: services => File.WriteAllTextAsync(services.Paths.GetTaskHistoryPath(), "{ broken"));

        Assert.Equal(TaskKind.IndexRefresh, Assert.Single(harness.ViewModel.Tasks.History).Kind);
    }

    private Task<ViewModelHarness> CreateAsync()
        => ViewModelHarness.CreateAsync(
            respond: request => request.RequestUri?.AbsolutePath.EndsWith("/AdvancedFlightComputer.zip", StringComparison.Ordinal) == true
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new GatedBody(_archive, _halfway, _gate.Task)) { Headers = { ContentType = new MediaTypeHeaderValue("application/zip") } } }
                : null,
            editSnapshot: snapshot => snapshot
                .Replace("AD14E4FE8111F4DAE8406D50459B7E5C42D58F1E01F549636946922CF72AE9E6", Convert.ToHexString(SHA256.HashData(_archive)), StringComparison.Ordinal)
                .Replace("\"size\": 129696", $"\"size\": {_archive.Length}", StringComparison.Ordinal));

    /// <summary>The Discover row of AdvancedFlightComputer, waiting for the confirmation of an unknown compatibility.</summary>
    private static async Task<(Instance Instance, DiscoverItem Item)> ConfirmingInstallAsync(ViewModelHarness harness)
    {
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        var item = viewModel.DiscoverItems.Single(row => row.ModId == "AdvancedFlightComputer");
        await item.InstallCommand.ExecuteAsync(null);
        Assert.True(item.IsConfirmingInstall);
        Assert.Empty(viewModel.Tasks.Running);
        return (instance, item);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private static byte[] Archive()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("AdvancedFlightComputer/mod.toml").Open());
            writer.Write("name = \"AdvancedFlightComputer\"");
        }

        return stream.ToArray();
    }

    /// <summary>A response body that sends half of the archive, then waits for the gate before it sends the rest.</summary>
    private sealed class GatedBody(byte[] content, TaskCompletionSource halfway, Task gate) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var half = content.Length / 2;
            if (_position == half)
            {
                halfway.TrySetResult();
                await gate.WaitAsync(cancellationToken);
            }

            var count = Math.Min(buffer.Length, (_position < half ? half : content.Length) - _position);
            content.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
