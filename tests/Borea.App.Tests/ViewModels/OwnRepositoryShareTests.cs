using System.Collections.Concurrent;
using System.Net;
using Borea.App.ViewModels;
using Borea.Core.Index;
using Borea.Core.Sharing;

namespace Borea.App.Tests.ViewModels;

/// <summary>Share from the player's own GitHub repository, which needs no share host (RFC 0094).</summary>
public sealed class OwnRepositoryShareTests
{
    private static readonly string VehiclesUrl = ContentIndexTypeFiles.Vehicles.AddressFrom(new Uri("https://ksamodding.github.io/content-index-releases/v1/index.json")).AbsoluteUri;

    private readonly ListingPullRequestViewModelTests.FakeSession _session = new();
    private readonly ListingPullRequestViewModelTests.FakePublisher _publisher = new();
    private readonly FakeRepository _repository = new();

    [Fact]
    public async Task FirstShare_MakesTheReleaseAndOpensThePullRequestThatNamesTheRepositoryAndThePrefix()
    {
        using var harness = await CreateAsync();
        var editor = await ShareAsync(harness);

        await editor.PublishFromOwnRepositoryCommand.ExecuteAsync(null);

        Assert.Null(editor.OwnRepositoryError);
        Assert.Equal(("ExampleRover/v", "1.0.0", "ExampleRover-1.0.0.zip"), Assert.Single(_repository.Releases));
        Assert.Equal(("octocat/ksa-shares", "ExampleRover/v"), (editor.Draft.Releases?.GitHub, editor.Draft.Releases?.TagPrefix));
        var document = Assert.Single(_publisher.Submissions).Document;
        Assert.Equal("vehicles/ExampleRover.toml", document.Path);
        Assert.Contains("[releases]\ngithub = \"octocat/ksa-shares\"\ntag_prefix = \"ExampleRover/v\"\n", document.Text, StringComparison.Ordinal);
        Assert.Equal(harness.Localization.FormatListingShareOwnReleased("ExampleRover/v1.0.0", "octocat/ksa-shares"), editor.OwnRepositoryOutcome);
        Assert.False(editor.CanPublishFromOwnRepository);
    }

    [Fact]
    public async Task MissingPermission_ShowsTheStepsAndCreatesNothing()
    {
        _repository.EnsureFailure = new ShareRepositoryException(ShareRepositoryFailure.MayNotCreateRepository, "Resource not accessible by integration");
        using var harness = await CreateAsync();
        var editor = await ShareAsync(harness);

        await editor.PublishFromOwnRepositoryCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.ListingShareOwnMayNotCreate, editor.OwnRepositoryError);
        Assert.True(editor.NeedsShareRepository);
        Assert.Empty(_repository.Releases);
        Assert.Empty(_publisher.Submissions);
        Assert.Null(editor.Draft.Releases);
        Assert.True(editor.CanPublishFromOwnRepository);
    }

    [Fact]
    public async Task InvalidDraft_MakesNoRelease()
    {
        using var harness = await CreateAsync();
        var editor = await ShareAsync(harness);
        editor.License = string.Empty;

        await editor.PublishFromOwnRepositoryCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.ListingFixErrors, editor.OwnRepositoryError);
        Assert.Empty(_repository.Releases);
        Assert.Null(editor.Draft.Releases);
    }

    [Fact]
    public async Task DraftNamingAnotherRepository_IsNotOffered()
    {
        using var harness = await CreateAsync();
        var editor = await ShareAsync(harness);

        editor.ReleasesGitHub = "octocat/rovers";

        Assert.False(editor.OffersOwnRepository);
    }

    [Fact]
    public async Task VehicleListedFromAnotherHost_IsNotOffered()
    {
        using var harness = await CreateAsync(Listed("""{ "github": "octocat/rovers" }""", "[]"));

        var editor = await ShareAsync(harness, offered: false);

        Assert.False(editor.OffersOwnRepository);
    }

    [Fact]
    public async Task LaterVersionWithTheSameDependencies_MakesTheReleaseWithoutAPullRequest()
    {
        using var harness = await CreateAsync(Listed(OwnReleases, """[{ "id": "ExamplePartsMod", "kind": "required", "min": "1.4.0" }]"""));
        var editor = await ShareAsync(harness);

        await editor.PublishFromOwnRepositoryCommand.ExecuteAsync(null);

        Assert.Null(editor.OwnRepositoryError);
        Assert.Equal(("ExampleRover/v", "1.0.0", "ExampleRover-1.0.0.zip"), Assert.Single(_repository.Releases));
        Assert.Empty(_publisher.Submissions);
        Assert.Equal(harness.Localization.FormatListingShareOwnNextVersion("ExampleRover/v1.0.0", "octocat/ksa-shares"), editor.OwnRepositoryOutcome);
        Assert.False(editor.OwnListingNeedsEdit);
    }

    [Fact]
    public async Task LaterVersionWithOtherDependencies_SaysTheListingNeedsAnEdit()
    {
        using var harness = await CreateAsync(Listed(OwnReleases, "[]"));
        var editor = await ShareAsync(harness);

        await editor.PublishFromOwnRepositoryCommand.ExecuteAsync(null);

        Assert.Single(_repository.Releases);
        Assert.Empty(_publisher.Submissions);
        Assert.True(editor.OwnListingNeedsEdit);
    }

    [Fact]
    public async Task WhileTheReleaseRuns_TheShareHostWaitsAndNoSecondReleaseStarts()
    {
        _repository.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = await CreateAsync();
        var editor = await ShareAsync(harness);

        var run = editor.PublishFromOwnRepositoryCommand.ExecuteAsync(null);
        await _repository.Entered.Task;

        Assert.True(editor.IsSharing);
        Assert.False(editor.CanUploadToShareHost);
        Assert.False(editor.CanPublishFromOwnRepository);

        _repository.Gate.SetResult();
        await run;

        Assert.True(editor.CanUploadToShareHost);
        Assert.Single(_repository.Releases);
    }

    private const string OwnReleases = """{ "github": "octocat/ksa-shares", "tag_prefix": "ExampleRover/v" }""";

    /// <summary>The vehicles of the index with ExampleRover, listed with these [releases] and [[dependencies]].</summary>
    private static string Listed(string releases, string dependencies) => $$"""
        { "snapshot_version": 1, "{{ContentIndexTypeFiles.Vehicles.ArrayKey}}": [{
            "id": "ExampleRover",
            "authored": { "spec_version": 1, "id": "ExampleRover", "type": "vehicle", "name": "Example Rover", "authors": ["Maxi"], "abstract": "A small rover.", "license": "CC0-1.0", "releases": {{releases}}, "dependencies": {{dependencies}} },
            "releases": [{
                "spec_version": 1, "id": "ExampleRover", "type": "vehicle", "version": "1.0.0", "version_scheme": "semver", "release_status": "stable",
                "release_date": "2026-10-20T12:00:00Z", "game_min": "2026.10.19.5638", "game_min_revision": 5638,
                "download": { "url": "https://example.test/rover.zip", "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "size": 1024, "content_type": "application/zip" },
                "install": { "derived": true, "folder": "Example Rover" }, "install_size": 2048, "dependencies": [],
                "listing": { "name": "Example Rover", "authors": ["Maxi"], "abstract": "A small rover.", "license": "CC0-1.0", "tags": [], "links": {} }
            }]
        }] }
        """;

    private async Task<ViewModelHarness> CreateAsync(string? vehicles = null)
    {
        _session.SignIn();
        return await ViewModelHarness.CreateAsync(
            respond: request => vehicles is not null && request.RequestUri?.AbsoluteUri == VehiclesUrl
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(vehicles) }
                : GameSaveShareTests.Ungated(request),
            gitHub: _session,
            listingPublisher: _publisher,
            shareRepository: _repository);
    }

    private static async Task<ListingEditor> ShareAsync(ViewModelHarness harness, bool offered = true)
    {
        await GameSaveShareTests.CreateInstanceAsync(harness, "Example Rover");
        await GameSaveShareTests.OpenAsync(harness);
        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);
        var editor = harness.ViewModel.ListingEditor;
        editor.Authors = "Maxi";
        editor.License = "CC-BY-4.0";
        editor.Abstract = "A six-wheel rover for the Moon.";
        Assert.Equal(offered, editor.OffersOwnRepository);
        return editor;
    }

    private sealed class FakeRepository : IShareRepository
    {
        public ShareRepositoryException? EnsureFailure { get; set; }

        /// <summary>Holds the release until it is set, after <see cref="Entered"/>.</summary>
        public TaskCompletionSource? Gate { get; set; }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<(string Prefix, string Version, string Asset)> Releases { get; } = new();

        public Task<ShareRepository> EnsureAsync(CancellationToken cancellationToken = default) =>
            EnsureFailure is { } failure ? Task.FromException<ShareRepository>(failure) : Task.FromResult(new ShareRepository("octocat/ksa-shares", 77));

        public Task<string> NextVersionAsync(ShareRepository repository, string tagPrefix, CancellationToken cancellationToken = default) => Task.FromResult("1.0.0");

        public async Task<Uri> PublishAsync(ShareRepository repository, string tagPrefix, string version, string assetName, Stream archive, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            if (Gate is { } gate)
                await gate.Task;

            Releases.Enqueue((tagPrefix, version, assetName));
            return new Uri($"https://github.com/{repository.FullName}/releases/tag/{tagPrefix}{version}");
        }
    }
}
