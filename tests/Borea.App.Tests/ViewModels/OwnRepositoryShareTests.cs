using System.Collections.Concurrent;
using Borea.App.ViewModels;
using Borea.Core.Sharing;

namespace Borea.App.Tests.ViewModels;

/// <summary>Share from the player's own GitHub repository, which needs no share host (RFC 0094).</summary>
public sealed class OwnRepositoryShareTests
{
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
    }

    private async Task<ViewModelHarness> CreateAsync()
    {
        _session.SignIn();
        return await ViewModelHarness.CreateAsync(respond: GameSaveShareTests.Ungated, gitHub: _session, listingPublisher: _publisher, shareRepository: _repository);
    }

    private static async Task<ListingEditor> ShareAsync(ViewModelHarness harness)
    {
        await GameSaveShareTests.CreateInstanceAsync(harness, "Example Rover");
        await GameSaveShareTests.OpenAsync(harness);
        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);
        var editor = harness.ViewModel.ListingEditor;
        editor.Authors = "Maxi";
        editor.License = "CC-BY-4.0";
        editor.Abstract = "A six-wheel rover for the Moon.";
        Assert.True(editor.OffersOwnRepository);
        return editor;
    }

    private sealed class FakeRepository : IShareRepository
    {
        public ShareRepositoryException? EnsureFailure { get; set; }

        public ConcurrentQueue<(string Prefix, string Version, string Asset)> Releases { get; } = new();

        public Task<ShareRepository> EnsureAsync(CancellationToken cancellationToken = default) =>
            EnsureFailure is { } failure ? Task.FromException<ShareRepository>(failure) : Task.FromResult(new ShareRepository("octocat/ksa-shares", 77));

        public Task<string> NextVersionAsync(ShareRepository repository, string tagPrefix, CancellationToken cancellationToken = default) => Task.FromResult("1.0.0");

        public Task<Uri> PublishAsync(ShareRepository repository, string tagPrefix, string version, string assetName, Stream archive, CancellationToken cancellationToken = default)
        {
            Releases.Enqueue((tagPrefix, version, assetName));
            return Task.FromResult(new Uri($"https://github.com/{repository.FullName}/releases/tag/{tagPrefix}{version}"));
        }
    }
}
