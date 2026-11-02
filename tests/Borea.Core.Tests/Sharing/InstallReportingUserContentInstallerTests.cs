using Borea.Core.GitHub;
using Borea.Core.Instances;
using Borea.Core.Listings;
using Borea.Core.Logging;
using Borea.Core.Mods;
using Borea.Core.Sharing;
using Borea.Core.UserContent;

namespace Borea.Core.Tests.Sharing;

public sealed class InstallReportingUserContentInstallerTests
{
    private const string HostFiles = "https://ksamods.gg/share/files/";

    private static readonly ShareHost Host = new("ksamods", "ksamods.gg", new Uri("https://ksamods.gg/api/share/v1/"), ["vehicle", "save"], new Dictionary<string, long>())
    {
        Data = new Uri("https://data.ksamods.example/"),
        Downloads = [new Uri(HostFiles)],
    };

    private readonly FakeInner _inner = new();
    private readonly FakeClient _client = new();
    private readonly RecordingLog _log = new();

    [Theory]
    [InlineData(false, ContentType.Vehicle, "vehicle")]
    [InlineData(true, ContentType.Save, "save")]
    public async Task InstallOrUpdate_Finished_ReportsTheListingOnceToItsHost(bool update, ContentType type, string expectedType)
    {
        var installer = Installer();
        var release = Release(type, HostFiles + expectedType + "/Rover/1.0.0");

        var result = update ? await installer.UpdateAsync(Guid.Empty, Record(release), release, null, discardChanges: false) : await installer.InstallAsync(Guid.Empty, release, null);
        await installer.Reports;

        Assert.Equal(update ? UserContentOutcome.Updated : UserContentOutcome.Installed, result.Outcome);
        Assert.Equal([(Host, expectedType, "Rover")], _client.Reports);
        Assert.Equal(["Reported the install of Rover 1.0.0 to ksamods.gg."], _log.Messages);
    }

    [Theory]
    [InlineData(false, UserContentOutcome.GameRunning)]
    [InlineData(false, UserContentOutcome.NameTaken)]
    [InlineData(true, UserContentOutcome.Changed)]
    public async Task InstallOrUpdate_NotFinished_ReportsNothing(bool update, UserContentOutcome outcome)
    {
        _inner.Outcome = outcome;
        var installer = Installer();
        var release = Release(ContentType.Vehicle, HostFiles + "vehicle/Rover/1.0.0");

        var result = update ? await installer.UpdateAsync(Guid.Empty, Record(release), release, null, discardChanges: false) : await installer.InstallAsync(Guid.Empty, release, null);
        await installer.Reports;

        Assert.Equal(outcome, result.Outcome);
        Assert.Empty(_client.Reports);
    }

    [Fact]
    public async Task Install_ArchiveOfNoShareHost_ReportsNothing()
    {
        var installer = Installer();

        await installer.InstallAsync(Guid.Empty, Release(ContentType.Vehicle, "https://github.com/author/rover/releases/download/1.0.0/rover.zip"), null);
        await installer.Reports;

        Assert.Empty(_client.Reports);
        Assert.Empty(_log.Messages);
    }

    [Fact]
    public async Task Install_ReportWaitsAndThenFails_EndsFirstAndWritesOneLine()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _client.Gate = gate.Task;
        _client.Failure = new ShareHostException(ShareHostFailure.Host, "ksamods.gg answered the install report with 429.");
        var installer = Installer();

        var result = await installer.InstallAsync(Guid.Empty, Release(ContentType.Vehicle, HostFiles + "vehicle/Rover/1.0.0"), null);
        var reportedBeforeTheEnd = installer.Reports.IsCompleted;
        gate.SetResult();
        await installer.Reports;

        Assert.Equal(UserContentOutcome.Installed, result.Outcome);
        Assert.False(reportedBeforeTheEnd);
        Assert.Equal(["Could not report the install of Rover 1.0.0 to ksamods.gg: ksamods.gg answered the install report with 429."], _log.Messages);
    }

    [Fact]
    public async Task WaitForReports_ReportStillOnItsWay_ReturnsAtOnceAndEndsWithTheReport()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _client.Gate = gate.Task;
        var installer = Installer();
        await installer.InstallAsync(Guid.Empty, Release(ContentType.Vehicle, HostFiles + "vehicle/Rover/1.0.0"), null);

        var wait = installer.WaitForReportsAsync(TimeSpan.FromMinutes(1));
        var endedBeforeTheReport = wait.IsCompleted;
        gate.SetResult();
        await wait;

        Assert.False(endedBeforeTheReport);
        Assert.Single(_client.Reports);
    }

    private InstallReportingUserContentInstaller Installer() => new(_inner, new FixedHosts([Host]), _client, _log);

    private static ModVersionMetadata Release(ContentType type, string url) =>
        new(1, "Rover", ModVersion.Parse("1.0.0"), ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, "2026.10.19.5638", 5638, new DownloadInfo(url, new string('A', 64), 1, "application/zip"), 1, [], type, install: new InstallInfo(null, derived: false, folder: "Rover"));

    private static InstalledContentRecord Record(ModVersionMetadata release) => new(release, "Rover", DateTimeOffset.UnixEpoch, new Dictionary<string, string>());

    private sealed class FakeInner : IUserContentInstaller
    {
        public UserContentOutcome? Outcome { get; set; }

        public Task<UserContentResult> CheckInstallAsync(Guid instanceId, ModVersionMetadata release, string? name, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<UserContentResult> CheckUpdateAsync(Guid instanceId, InstalledContentRecord installed, ModVersionMetadata release, string? name, bool discardChanges, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<UserContentResult> InstallAsync(Guid instanceId, ModVersionMetadata release, string? name, UserContentStep? beforeMove = null, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Answer(UserContentOutcome.Installed);

        public Task<UserContentResult> UpdateAsync(Guid instanceId, InstalledContentRecord installed, ModVersionMetadata release, string? name, bool discardChanges, UserContentStep? beforeMove = null, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Answer(UserContentOutcome.Updated);

        public Task<UserContentResult> RemoveAsync(Guid instanceId, InstalledContentRecord installed, bool discardChanges, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private Task<UserContentResult> Answer(UserContentOutcome finished) => Task.FromResult(new UserContentResult(Outcome ?? finished, "Rover"));
    }

    private sealed class FixedHosts(IReadOnlyList<ShareHost> hosts) : IShareHostSource
    {
        public Task<IReadOnlyList<ShareHost>> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(hosts);
    }

    private sealed class FakeClient : IShareHostClient
    {
        public List<(ShareHost Host, string Type, string Id)> Reports { get; } = [];

        public Task? Gate { get; set; }

        public ShareHostException? Failure { get; set; }

        public async Task ReportInstallAsync(ShareHost host, string type, string id, CancellationToken cancellationToken = default)
        {
            if (Gate is { } gate)
                await gate;

            lock (Reports)
                Reports.Add((host, type, id));
            if (Failure is { } failure)
                throw failure;
        }

        public bool IsSignedIn(ShareHost host) => throw new NotSupportedException();

        public Task SignInAsync(ShareHost host, IProgress<GitHubDeviceCode>? progress = null, bool keepSignedIn = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void SignOut(ShareHost host) => throw new NotSupportedException();

        public Task<ShareUpload> UploadAsync(ShareHost host, Stream archive, string fileName, IReadOnlyList<ListingDependency> dependencies, string? listingId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SharePublished> PublishAsync(ShareHost host, string uploadId, string version, string status, ListingDraft? listing, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task UploadImageAsync(ShareHost host, string listingType, string listingId, ReadOnlyMemory<byte> image, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ShareListingChanged> ChangeListingAsync(ShareHost host, string type, string id, ShareListingChange change, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task TakeDownReleaseAsync(ShareHost host, string type, string id, string version, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingLog : IBoreaLog
    {
        public List<string> Messages { get; } = [];

        public string CurrentFilePath => "borea.log";

        public void Write(string message)
        {
            lock (Messages)
                Messages.Add(message);
        }

        public void Write(string message, Exception exception) => Write(message);

        public IReadOnlyList<string> ReadRecentLines(int maxLines) => [];
    }
}
