using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Borea.App.Localization;
using Borea.App.ViewModels;
using Borea.Core.History;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

public sealed class InstallStopTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly TaskCompletionSource _downloading = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public void StopText_AfterTheDownload_SaysThatTheModFinishesFirst()
    {
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("en"));
        var task = new TaskRegistry(localization, () => null, () => null, _ => Task.CompletedTask).Start(TaskKind.ModInstall, null, null, null, null, null, TaskState.Running);
        var run = new InstallRun(localization, task);
        Assert.Equal(localization.InstallStop, run.StopText);

        run.StopCommand.Execute(null);
        Assert.Equal(localization.InstallStopping, run.StopText);
        Assert.False(run.StopCommand.CanExecute(null));

        run.IsFinishingMod = true;
        Assert.Equal(localization.InstallStoppingAfterMod, run.StopText);
        Assert.True(run.InstallStop.IsRequested);
    }

    [Fact]
    public void ShowReport_AfterTheRunEnded_DoesNothing()
    {
        var run = NewRun();
        run.End();

        var shown = false;
        run.ShowReport(() => shown = true);

        Assert.False(shown);
    }

    [Fact]
    public async Task End_WhileAReportRuns_WaitsUntilTheReportIsDone()
    {
        var run = NewRun();
        using var reporting = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var status = "Downloading";
        var report = Task.Run(() => run.ShowReport(() =>
        {
            reporting.Set();
            release.Wait(Timeout);
            status = "Downloading AdvancedFlightComputer 0.7.5";
        }));
        Assert.True(reporting.Wait(Timeout));

        var end = Task.Run(() =>
        {
            run.End();
            status = "Install stopped.";
        });
        await Task.WhenAny(end, Task.Delay(TimeSpan.FromMilliseconds(200)));
        Assert.False(end.IsCompleted);

        release.Set();
        await Task.WhenAll(report, end).WaitAsync(Timeout);
        Assert.Equal("Install stopped.", status);
    }

    private static InstallRun NewRun()
    {
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("en"));
        var task = new TaskRegistry(localization, () => null, () => null, _ => Task.CompletedTask).Start(TaskKind.ModInstall, null, null, null, null, null, TaskState.Running);
        return new InstallRun(localization, task);
    }

    [Fact]
    public void PauseButton_WorksOnlyWhileTheModDownloads()
    {
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("en"));
        var task = new TaskRegistry(localization, () => null, () => null, _ => Task.CompletedTask).Start(TaskKind.ModInstall, null, null, null, null, null, TaskState.Running);
        var run = new InstallRun(localization, task);
        Assert.False(run.TogglePauseCommand.CanExecute(null));

        run.Report(InstallPhase.Downloading);
        Assert.True(run.TogglePauseCommand.CanExecute(null));
        Assert.Equal(localization.InstallPause, run.PauseText);

        run.Report(InstallPhase.Extracting);
        Assert.False(run.TogglePauseCommand.CanExecute(null));
    }

    [Fact]
    public async Task Pause_DuringTheDownload_ShowsThePausedTaskAndResumeFinishesTheInstall()
    {
        var archive = Archive("AdvancedFlightComputer");
        var half = archive.Length / 2;
        var ranges = new List<RangeHeaderValue?>();
        using var harness = await ViewModelHarness.CreateAsync(
            respond: request =>
            {
                if (request.RequestUri?.AbsolutePath.EndsWith("/AdvancedFlightComputer.zip", StringComparison.Ordinal) != true)
                    return null;

                ranges.Add(request.Headers.Range);
                return request.Headers.Range is null ? FirstHalf(archive) : SecondHalf(archive);
            },
            editSnapshot: snapshot => snapshot
                .Replace("AD14E4FE8111F4DAE8406D50459B7E5C42D58F1E01F549636946922CF72AE9E6", Convert.ToHexString(SHA256.HashData(archive)), StringComparison.Ordinal)
                .Replace("\"size\": 129696", $"\"size\": {archive.Length}", StringComparison.Ordinal));
        var viewModel = harness.ViewModel;
        var (instance, item) = await ConfirmingInstallAsync(harness);

        var install = item.ConfirmInstallCommand.ExecuteAsync(null);
        await _downloading.Task.WaitAsync(Timeout);
        var run = item.Run!;
        var task = Assert.Single(viewModel.Tasks.Running);
        await WaitUntilAsync(() => run.IsDownloading && task.Progress > 0);
        run.TogglePauseCommand.Execute(null);

        Assert.True(run.IsPaused);
        Assert.Equal(harness.Localization.InstallResume, run.PauseText);
        await WaitUntilAsync(() => task.State == TaskState.Paused);
        Assert.Equal(harness.Localization.TaskPaused, task.StateText);
        Assert.Equal(harness.Localization.FormatInstallPaused("AdvancedFlightComputer 0.7.5"), item.ProgressStatus);
        Assert.Equal(item.ProgressStatus, task.Step);
        Assert.Equal(harness.Localization.FormatInstallSize(InstallProgressText.Number(half), InstallProgressText.Number(archive.Length) + " MB"), item.ProgressDetail);
        Assert.True(viewModel.Tasks.HasProgress);
        Assert.Equal(100.0 * half / archive.Length, viewModel.Tasks.Progress, 3);
        Assert.Empty(viewModel.Toasts.Items);

        harness.Localization.TrySetCulture("de");
        Assert.Equal(harness.Localization.FormatInstallPaused("AdvancedFlightComputer 0.7.5"), item.ProgressStatus);
        Assert.Equal(item.ProgressStatus, task.Step);

        run.TogglePauseCommand.Execute(null);
        await install;

        Assert.Equal(2, ranges.Count);
        Assert.Equal(half, Assert.Single(ranges[1]!.Ranges).From);
        Assert.Null(item.InstallError);
        Assert.Equal("AdvancedFlightComputer", Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods).ModId);
        Assert.Equal(TaskState.Finished, task.State);
        Assert.Single(viewModel.Toasts.Items);
    }

    [Fact]
    public async Task Stop_WhilePaused_StopsTheInstall()
    {
        var archive = Archive("AdvancedFlightComputer");
        using var harness = await ViewModelHarness.CreateAsync(
            respond: request => request.RequestUri?.AbsolutePath.EndsWith("/AdvancedFlightComputer.zip", StringComparison.Ordinal) == true ? FirstHalf(archive) : null);
        var (instance, item) = await ConfirmingInstallAsync(harness);

        var install = item.ConfirmInstallCommand.ExecuteAsync(null);
        await _downloading.Task.WaitAsync(Timeout);
        var run = item.Run!;
        await WaitUntilAsync(() => run.IsDownloading);
        run.TogglePauseCommand.Execute(null);
        Assert.True(run.IsPaused);
        run.StopCommand.Execute(null);
        await install;

        Assert.False(run.IsPaused);
        Assert.Equal(harness.Localization.InstallStopped, item.ProgressStatus);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
        Assert.Equal(TaskState.Stopped, harness.ViewModel.Tasks.History[0].State);
    }

    [Fact]
    public async Task Stop_DuringTheDownload_SaysSoAndInstallsNothing()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: StallArchive);
        var (instance, item) = await ConfirmingInstallAsync(harness);

        var install = item.ConfirmInstallCommand.ExecuteAsync(null);
        await _downloading.Task.WaitAsync(Timeout);
        item.Run!.StopCommand.Execute(null);
        await install;

        Assert.False(item.IsInstalling);
        Assert.Null(item.Run);
        Assert.Null(item.InstallError);
        Assert.Equal(harness.Localization.InstallStopped, item.ProgressStatus);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
        var toast = Assert.Single(harness.ViewModel.Toasts.Items);
        Assert.Equal(harness.Localization.FormatToastInstallStopped(item.Name), toast.Message);
        Assert.False(toast.HasDetail);
    }

    [Fact]
    public async Task StopUpdate_DuringTheDownload_KeepsTheOldVersion()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: StallArchive);
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea, version: "0.7.4");
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();
        var row = viewModel.ContentGroups.Single().Items.Single();
        await row.UpdateCommand.ExecuteAsync(null);
        Assert.True(row.IsConfirmingUpdate);

        var update = row.ConfirmUpdateCommand.ExecuteAsync(null);
        await _downloading.Task.WaitAsync(Timeout);
        row.Run!.StopCommand.Execute(null);
        await update;

        var shown = viewModel.ContentGroups.Single().Items.Single();
        Assert.Equal("0.7.4", shown.Version);
        Assert.Equal(harness.Localization.UpdateStopped, shown.ProgressStatus);
        Assert.Null(shown.InstallError);
        Assert.Equal(ModVersion.Parse("0.7.4"), Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods).Version);
    }

    [Fact]
    public async Task StopPack_DuringTheSecondMember_KeepsTheFirstAndSaysHowFarItGot()
    {
        var archive = Archive("AdvancedFlightComputer");
        using var harness = await ViewModelHarness.CreateAsync(
            respond: request => request.RequestUri?.AbsolutePath.EndsWith("/AdvancedFlightComputer.zip", StringComparison.Ordinal) == true
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) { Headers = { ContentType = new MediaTypeHeaderValue("application/zip") } } }
                : request.RequestUri?.AbsolutePath.EndsWith("/MeasureTools.zip", StringComparison.Ordinal) == true
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledBody(_downloading)) }
                    : null,
            editSnapshot: snapshot => snapshot
                .Replace("AD14E4FE8111F4DAE8406D50459B7E5C42D58F1E01F549636946922CF72AE9E6", Convert.ToHexString(SHA256.HashData(archive)), StringComparison.Ordinal)
                .Replace("\"size\": 129696", $"\"size\": {archive.Length}", StringComparison.Ordinal)
                .Replace("\"packs\": []", StarterPack, StringComparison.Ordinal));
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);
        await pack.InstallCommand.ExecuteAsync(null);
        Assert.True(pack.IsConfirmingInstall);

        var install = pack.ConfirmInstallCommand.ExecuteAsync(null);
        await _downloading.Task.WaitAsync(Timeout);
        pack.Run!.StopCommand.Execute(null);
        await install;

        Assert.False(pack.IsInstalling);
        Assert.Null(pack.InstallError);
        Assert.Equal(harness.Localization.FormatInstallStoppedAfter(1, 2), pack.ProgressStatus);
        Assert.Equal("AdvancedFlightComputer", Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods).ModId);
        var toast = Assert.Single(viewModel.Toasts.Items);
        Assert.Equal(harness.Localization.FormatToastInstallStopped(pack.Name), toast.Message);
        Assert.Equal(harness.Localization.FormatToastStoppedInstalled(1, 2), toast.Detail);
    }

    [Fact]
    public async Task Install_WhileTheWindowCloses_StopsBeforeTheDownload()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: StallArchive);
        var viewModel = harness.ViewModel;
        var (instance, item) = await ConfirmingInstallAsync(harness);
        await viewModel.StopInstallsAsync().WaitAsync(Timeout);

        await item.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.False(_downloading.Task.IsCompleted);
        Assert.False(viewModel.HasRunningInstalls);
        Assert.Equal(harness.Localization.InstallStopped, item.ProgressStatus);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
    }

    [Fact]
    public async Task StopInstalls_ReturnsOnceTheRunningInstallStopped()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: StallArchive);
        var viewModel = harness.ViewModel;
        var (_, item) = await ConfirmingInstallAsync(harness);
        var install = item.ConfirmInstallCommand.ExecuteAsync(null);
        await _downloading.Task.WaitAsync(Timeout);
        Assert.True(viewModel.HasRunningInstalls);

        await viewModel.StopInstallsAsync().WaitAsync(Timeout);

        Assert.True(viewModel.IsClosing);
        Assert.False(viewModel.HasRunningInstalls);
        await install;
        Assert.Equal(harness.Localization.InstallStopped, item.ProgressStatus);
    }

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
        return (instance, item);
    }

    private const string StarterPack = """ "packs": [{ "id": "starter-pack", "versions": [{ "authored": { "spec_version": 1, "id": "starter-pack", "type": "modpack", "name": "Starter Pack", "authors": ["Maxi"], "abstract": "Starter Pack abstract.", "description": "## Starter Pack", "license": "MIT", "tags": ["starter"], "version": "1.0.0", "released_at": "2026-09-01T12:00:00Z", "links": { "forums": "https://forums.example.com/starter-pack" }, "compatibility": { "game_min": "2026.8.19.5261" }, "mods": [{ "id": "AdvancedFlightComputer", "version": "0.7.5" }, { "id": "MeasureTools", "version": "1.1.10" }] } }] }] """;

    private static byte[] Archive(string modId)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry($"{modId}/mod.toml").Open());
            writer.Write($"name = \"{modId}\"");
        }

        return stream.ToArray();
    }

    /// <summary>The first answer, which announces the whole archive and sends its first half.</summary>
    private HttpResponseMessage FirstHalf(byte[] archive)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new HalfThenStalledBody(archive, _downloading)) };
        response.Content.Headers.ContentLength = archive.Length;
        response.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
        return response;
    }

    private static HttpResponseMessage SecondHalf(byte[] archive)
    {
        var half = archive.Length / 2;
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(archive[half..]) };
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(half, archive.Length - 1, archive.Length);
        return response;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private HttpResponseMessage? StallArchive(HttpRequestMessage request)
        => request.RequestUri?.AbsolutePath.EndsWith("/AdvancedFlightComputer.zip", StringComparison.Ordinal) == true
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledBody(_downloading)) }
            : null;

    /// <summary>A response body that sends the first half and then nothing until its read is canceled.</summary>
    private sealed class HalfThenStalledBody(byte[] content, TaskCompletionSource halfway) : Stream
    {
        private bool _sent;

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
            if (!_sent)
            {
                _sent = true;
                var half = content.Length / 2;
                content.AsMemory(0, half).CopyTo(buffer);
                return half;
            }

            halfway.TrySetResult();
            await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
            return 0;
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

    /// <summary>A response body that sends nothing until its read is cancelled.</summary>
    private sealed class StalledBody(TaskCompletionSource reading) : Stream
    {
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
            reading.TrySetResult();
            await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
            return 0;
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
