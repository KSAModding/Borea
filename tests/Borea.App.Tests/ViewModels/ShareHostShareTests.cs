using System.Collections.Concurrent;
using System.Net;
using Borea.App.ViewModels;
using Borea.Core.GitHub;
using Borea.Core.Index;
using Borea.Core.Listings;
using Borea.Core.Sharing;

namespace Borea.App.Tests.ViewModels;

/// <summary>Share on a share host of RFC 0094 from the listing editor, against a fake host.</summary>
public sealed class ShareHostShareTests
{
    private const string HostsUrl = "https://raw.githubusercontent.com/KSAModding/content-index/main/hosts.toml";

    private static readonly string VehiclesUrl = ContentIndexTypeFiles.Vehicles.AddressFrom(new Uri("https://ksamodding.github.io/content-index-releases/v1/index.json")).AbsoluteUri;

    private const string Hosts = """
        [ksamods]
        kind = "share"
        name = "ksamods.gg"
        api = "https://ksamods.gg/api/share/v1/"
        types = ["vehicle", "save"]
        max_bytes = { vehicle = 10485760, save = 104857600 }
        """;

    private static readonly ShareUpload Checked = new("u42", [new ShareCheck("meta.toml", true, null), new ShareCheck("install.folder", true, null)], "1.0.0", "id = \"ExampleRover\"\n");

    [Fact]
    public async Task FirstShare_SignsInUploadsShowsTheChecksAndPublishes()
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve(Hosts), shareHostClient: host);
        var editor = await ShareAsync(harness);
        editor.Authors = "Maxi";
        editor.License = "CC-BY-4.0";
        editor.Abstract = "A six-wheel rover for the Moon.";

        var row = Assert.Single(editor.ShareHostRows);
        Assert.Equal(harness.Localization.FormatListingShareOnHost("ksamods.gg"), row.Label);
        await editor.UploadToShareHostCommand.ExecuteAsync(row);

        Assert.Equal(1, host.SignIns);
        var upload = Assert.Single(host.Uploads);
        Assert.Equal(("Example Rover.zip", null), (upload.FileName, upload.ListingId));
        Assert.True(upload.Size > 0);
        Assert.Equal(("ExamplePartsMod", "required"), (upload.Dependencies[0].Id, upload.Dependencies[0].Kind));
        Assert.Equal([new ShareCheckRow(harness.Localization.FormatListingShareCheckPassed("meta.toml"), true), new ShareCheckRow(harness.Localization.FormatListingShareCheckPassed("install.folder"), true)], editor.ShareHostChecks);
        Assert.Equal(("1.0.0", "id = \"ExampleRover\"\n"), (editor.ShareHostVersion, editor.ShareHostListingText));
        Assert.True(editor.CanPublishToShareHost);

        Assert.Equal([harness.Localization.ReleaseStable, harness.Localization.ReleaseTesting, harness.Localization.ReleaseDev], editor.ShareStatusChoices.Select(choice => choice.Label));
        editor.SelectedShareStatus = editor.ShareStatusChoices[1];
        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        var publish = Assert.Single(host.Publishes);
        Assert.Equal(("u42", "1.0.0", "testing"), (publish.UploadId, publish.Version, publish.Status));
        var listing = Assert.IsType<ListingDraft>(publish.Listing);
        Assert.Equal(("ExampleRover", "Example Rover", "A six-wheel rover for the Moon.", "CC-BY-4.0"), (listing.Id, listing.Name, listing.Abstract, listing.License));
        Assert.Equal(["Maxi"], listing.Authors);
        Assert.Equal(new Uri("https://github.com/KSAModding/content-index/pull/250"), editor.SharePullRequest);
        Assert.Equal(harness.Localization.FormatListingShareHostPublished("ExampleRover", "1.0.0"), editor.ShareHostOutcome);
        Assert.Null(editor.ShareHostError);
    }

    [Fact]
    public async Task HostError_ShowsItsMessage()
    {
        var host = new FakeHost { UploadFailure = new ShareHostException(ShareHostFailure.Host, "ExampleRover belongs to another account.", "id_taken") };
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve(Hosts), shareHostClient: host);
        var editor = await ShareAsync(harness);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        Assert.Equal(harness.Localization.FormatListingShareHostSaid("ksamods.gg", "ExampleRover belongs to another account."), editor.ShareHostError);
        Assert.False(editor.HasShareUpload);
        Assert.Null(editor.ShareHostProgress);
    }

    [Fact]
    public async Task ArchiveOverTheLimitOfTheHost_SendsNothing()
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve(Hosts.Replace("vehicle = 10485760", "vehicle = 10", StringComparison.Ordinal)), shareHostClient: host);
        var editor = await ShareAsync(harness);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        var parts = harness.Localization.FormatListingShareHostTooLarge("<size>", "ksamods.gg", MainViewModel.SizeText(10)).Split("<size>");
        Assert.StartsWith(parts[0], editor.ShareHostError, StringComparison.Ordinal);
        Assert.EndsWith(parts[1], editor.ShareHostError, StringComparison.Ordinal);
        Assert.Equal((0, 0), (host.SignIns, host.Uploads.Count));
    }

    [Fact]
    public async Task FailedCheck_IsMarkedAndBlocksThePublish()
    {
        var host = new FakeHost { Upload = new ShareUpload("u42", [new ShareCheck("meta.toml", true, null), new ShareCheck("install.folder", false, "Taken by OtherRover.")], "1.0.0", null) };
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve(Hosts), shareHostClient: host);
        var editor = await ShareAsync(harness);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        var failed = editor.ShareHostChecks[1];
        Assert.Equal((harness.Localization.FormatListingShareCheckFailed("install.folder", "Taken by OtherRover."), true), (failed.Text, failed.Failed));
        Assert.False(editor.ShareHostChecks[0].Failed);
        Assert.False(editor.CanPublishToShareHost);
        Assert.Equal(harness.Localization.ListingShareHostChecksFailed, editor.ShareHostError);
    }

    [Fact]
    public async Task VehicleListedOnTheHost_UploadsAsANewVersionOfItsListing()
    {
        // The dependency list did not change, so the host publishes the release without a listing pull request.
        var host = new FakeHost { PullRequest = null };
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve(Hosts, vehicles: ListedRover), shareHostClient: host);
        var editor = await ShareAsync(harness);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        Assert.Equal("ExampleRover", Assert.Single(host.Uploads).ListingId);
        Assert.Null(Assert.Single(host.Publishes).Listing);
        Assert.Equal(harness.Localization.FormatListingShareHostReleased("ExampleRover", "1.0.0"), editor.ShareHostOutcome);
        Assert.Null(editor.SharePullRequest);
        Assert.Null(editor.ShareHostError);
        Assert.False(editor.HasShareUpload);
    }

    [Fact]
    public async Task NoHostInContentIndex_OffersNone()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve(null), shareHostClient: new FakeHost());

        var editor = await ShareAsync(harness);

        Assert.False(editor.HasShareHosts);
    }

    private static async Task<ListingEditor> ShareAsync(ViewModelHarness harness)
    {
        await GameSaveShareTests.CreateInstanceAsync(harness, "Example Rover");
        await GameSaveShareTests.OpenAsync(harness);
        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);
        var editor = harness.ViewModel.ListingEditor;
        await editor.ShareHostsLoad;
        return editor;
    }

    /// <summary>Serves hosts.toml, or a 404 when it is null, the schema without its gate, and the vehicles of the index.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage?> Serve(string? hosts, string? vehicles = null) => request => request.RequestUri?.AbsoluteUri switch
    {
        HostsUrl => hosts is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(hosts) },
        { } uri when uri == VehiclesUrl && vehicles is not null => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(vehicles) },
        _ => GameSaveShareTests.Ungated(request),
    };

    private static readonly string ListedRover = $$"""
        { "snapshot_version": 1, "{{ContentIndexTypeFiles.Vehicles.ArrayKey}}": [{
            "id": "ExampleRover",
            "authored": { "spec_version": 1, "id": "ExampleRover", "type": "vehicle", "name": "Example Rover", "authors": ["Maxi"], "abstract": "A small rover.", "license": "CC0-1.0", "releases": { "ksamods": "ExampleRover" } },
            "releases": [{
                "spec_version": 1, "id": "ExampleRover", "type": "vehicle", "version": "1.0.0", "version_scheme": "semver", "release_status": "stable",
                "release_date": "2026-10-20T12:00:00Z", "game_min": "2026.10.19.5638", "game_min_revision": 5638,
                "download": { "url": "https://example.test/rover.zip", "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "size": 1024, "content_type": "application/zip" },
                "install": { "derived": true, "folder": "Example Rover" }, "install_size": 2048, "dependencies": [],
                "listing": { "name": "Example Rover", "authors": ["Maxi"], "abstract": "A small rover.", "license": "CC0-1.0", "tags": [], "links": {} }
            }]
        }] }
        """;

    private sealed record Upload(string FileName, long Size, IReadOnlyList<ListingDependency> Dependencies, string? ListingId);

    private sealed record Publish(string UploadId, string Version, string Status, ListingDraft? Listing);

    private sealed class FakeHost : IShareHostClient
    {
        private bool _signedIn;

        public int SignIns { get; private set; }

        public ConcurrentQueue<Upload> Uploads { get; } = new();

        public ConcurrentQueue<Publish> Publishes { get; } = new();

        public ShareHostException? UploadFailure { get; init; }

        public ShareUpload Upload { get; init; } = Checked;

        public Uri? PullRequest { get; init; } = new("https://github.com/KSAModding/content-index/pull/250");

        public bool IsSignedIn(ShareHost host) => _signedIn;

        public Task SignInAsync(ShareHost host, IProgress<GitHubDeviceCode>? progress = null, bool keepSignedIn = false, CancellationToken cancellationToken = default)
        {
            SignIns++;
            progress?.Report(new GitHubDeviceCode("ROVR-1234", "https://ksamods.gg/device"));
            _signedIn = true;
            return Task.CompletedTask;
        }

        public void SignOut(ShareHost host) => _signedIn = false;

        public Task<ShareUpload> UploadAsync(ShareHost host, Stream archive, string fileName, IReadOnlyList<ListingDependency> dependencies, string? listingId, CancellationToken cancellationToken = default)
        {
            Uploads.Enqueue(new Upload(fileName, archive.Length, dependencies, listingId));
            return UploadFailure is { } failure ? Task.FromException<ShareUpload>(failure) : Task.FromResult(Upload);
        }

        public Task<SharePublished> PublishAsync(ShareHost host, string uploadId, string version, string status, ListingDraft? listing, CancellationToken cancellationToken = default)
        {
            Publishes.Enqueue(new Publish(uploadId, version, status, listing));
            return Task.FromResult(new SharePublished("ExampleRover", version, PullRequest));
        }
    }
}
