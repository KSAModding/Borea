using System.Globalization;
using Borea.App.Localization;
using Borea.App.ViewModels;
using Borea.Core.Listings;
using Borea.Core.Stewardship;

namespace Borea.App.Tests.ViewModels;

public sealed class StewardReviewViewModelTests
{
    private const string Index = "KSAModding/content-index";
    private const string Releases = "KSAModding/content-index-releases";
    private const string Second = "89abcdef0123456789abcdef0123456789abcdef";
    private const string Forums = "https://forums.ahwoo.com/threads/my-mod.783/";

    private const string ListingText = """
        spec_version = 1
        id = "MyMod"
        type = "mod"
        name = "My Mod"
        authors = ["Alice", "Bob"]
        abstract = "Adds a thing."
        description = "It adds **a thing**."
        license = "MIT"
        tags = ["parts", "space-station"]

        [links]
        repository = "https://github.com/alice/MyMod"
        forums = "https://forums.ahwoo.com/threads/my-mod.783/"
        homepage = "file:///C:/Windows/System32/calc.exe"

        [compatibility]
        game_min = "2026.9"
        """;

    private const string PackText = """
        spec_version = 1
        id = "my-pack"
        type = "mod-pack"
        name = "My Pack"
        version = "1.2.0"
        mods = [{ id = "MyMod", version = "1.0.0" }, { id = "Other", version = "2.1.0" }]
        saves = [{ id = "SomeSave", version = "1.0.0" }]
        """;

    private readonly StewardSession _session = new();
    private readonly FakeStewardQueue _queue = new();
    private readonly FakePullRequestReviews _reviews = new();

    [Fact]
    public async Task ListingFromAFork_ShowsThePullRequest_ValidateOnItsCommit_TheVerdict_AndThePreview()
    {
        _queue.Items.Add(FakeStewardQueue.Item(5));
        using var harness = await CreateAsync();
        var pull = FakePullRequestReviews.Review(5, headRepository: "alice/content-index") with
        {
            Title = "Add My Mod",
            Labels = ["listing", "needs-steward"],
            Validate = new ValidateStatus(ValidateState.Success, "validated, a steward decides", new Uri($"https://github.com/{Index}/actions/runs/1")),
            Verdict = "Validated, and **ownership is not verified**.",
        };
        _reviews.Reviews[(Index, 5)] = pull with
        {
            Files = [FakePullRequestReviews.File(pull, "listings/MyMod.toml", "@@ -0,0 +1,3 @@\n+id = \"MyMod\"\n-name = \"Old\"\n context")],
            Documents = [Listing(harness, new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.NoProof, Repository: "alice/MyMod", ForumsThread: new Uri(Forums)))],
        };

        var review = await OpenReviewAsync(harness.ViewModel);

        Assert.True(harness.ViewModel.StewardPage.IsReviewOpen);
        Assert.Equal([(Index, 5)], _reviews.Reads);
        Assert.Equal(
            ("content-index #5", "Add My Mod", "by alice", "from alice/content-index", null, false),
            (review.NumberText, review.Title, review.ByText, review.FromText, review.StateText, review.IsDraft));
        Assert.Equal(["listing", "needs-steward"], review.Labels);
        Assert.Equal(("Head commit 0123456", "validate: passed", true, false), (review.CommitText, review.ValidateText, review.IsValidateSuccess, review.IsValidateBad));
        Assert.Equal("validated, a steward decides", review.ValidateDescription);
        Assert.Equal(("Validated, and **ownership is not verified**.", true, true), (review.Verdict, review.HasVerdict, review.CanRunChecks));

        var file = Assert.Single(review.Files);
        Assert.Equal(("Added", "+4 -1", true, "Changed files (1)"), (file.StatusText, file.ChangesText, file.HasPatch, review.FilesHeading));
        Assert.Equal([(false, false, true), (true, false, false), (false, true, false), (false, false, false)], file.Lines.Select(line => (line.IsAdded, line.IsRemoved, line.IsHunk)));

        var document = Assert.Single(review.Documents);
        Assert.Equal(("listings/MyMod.toml", "Listing", true, null, null), (document.Path, document.KindText, document.IsPreview, document.Problem, document.RawText));
        Assert.Equal(("My Mod", "by Alice, Bob", "Adds a thing.", "It adds **a thing**.", "MIT", ">= 2026.9", null), (document.Name, document.AuthorsText, document.Abstract, document.Description, document.License, document.GameVersionText, document.VersionText));
        Assert.Equal(["Parts", "Space Station"], document.Tags);
        Assert.Equal([("forums", Forums), ("repository", "https://github.com/alice/MyMod")], document.Links.Select(link => (link.Key, link.Url)));
        Assert.Empty(document.Members);
        Assert.Equal(
            (true, false, "alice has no ownership proof, so a steward decides.", "alice does not own alice/MyMod, and alice/MyMod has neither the topic ksa-index-alice nor a marker file that names alice."),
            (document.HasOwnership, document.IsOwnershipVerified, document.OwnershipText, document.OwnershipDetail));
        Assert.Equal([("repository", "https://github.com/alice/MyMod"), ("forums", Forums)], document.OwnershipLinks.Select(link => (link.Key, link.Url)));
    }

    [Fact]
    public async Task EveryLink_OpensOnGitHubOrTheWeb()
    {
        _queue.Items.Add(FakeStewardQueue.Item(5));
        using var harness = await CreateAsync();
        var pull = FakePullRequestReviews.Review(5) with { Validate = new ValidateStatus(ValidateState.Failure, "does not parse", new Uri($"https://github.com/{Index}/actions/runs/7")) };
        _reviews.Reviews[(Index, 5)] = pull with
        {
            Files = [FakePullRequestReviews.File(pull, "listings/MyMod.toml", "@@")],
            Documents = [Listing(harness, new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.Topic, Repository: "alice/MyMod"))],
        };
        var viewModel = harness.ViewModel;
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;
        var review = await OpenReviewAsync(viewModel);

        review.OpenCommand.Execute(null);
        review.RunChecksCommand.Execute(null);
        review.OpenValidateDetailsCommand.Execute(null);
        review.Files[0].OpenCommand.Execute(null);
        review.Documents[0].OpenLinkCommand.Execute(review.Documents[0].Links[0]);
        review.Documents[0].OpenLinkCommand.Execute(review.Documents[0].OwnershipLinks[0]);

        Assert.Equal(
            [
                $"https://github.com/{Index}/pull/5",
                $"https://github.com/{Index}/actions/workflows/checks.yml",
                $"https://github.com/{Index}/actions/runs/7",
                PullRequestFile.DiffUrlOf(pull.Url, "listings/MyMod.toml").AbsoluteUri,
                Forums,
                "https://github.com/alice/MyMod",
            ],
            opened);
        Assert.Null(review.Error);
        Assert.Equal(("validate: failed", true), (review.ValidateText, review.IsValidateBad));
    }

    [Fact]
    public async Task AmendmentWithThreeReleaseFiles_ShowsEachDiff_AndNoRunOfTheChecks()
    {
        _queue.Items.Add(FakeStewardQueue.Item(71, repository: Releases));
        using var harness = await CreateAsync();
        var pull = FakePullRequestReviews.Review(71, Releases) with { Verdict = "An amendment by a non-owner, so a steward decides." };
        _reviews.Reviews[(Releases, 71)] = pull with
        {
            Files =
            [
                FakePullRequestReviews.File(pull, "releases/MyMod/1.0.0.json", "@@ -3 +3 @@\n-  \"game_max\": null\n+  \"game_max\": \"2026.9\"", "modified"),
                FakePullRequestReviews.File(pull, "releases/MyMod/1.1.0.json", "@@ -3 +3 @@\n-  \"yanked\": false\n+  \"yanked\": true", "modified"),
                FakePullRequestReviews.File(pull, "releases/MyMod/1.2.0.json", "@@ -7 +7 @@\n-  \"os\": null\n+  \"os\": [\"windows\"]", "modified"),
            ],
        };

        var review = await OpenReviewAsync(harness.ViewModel);

        Assert.Equal("content-index-releases #71", review.NumberText);
        Assert.Equal(3, review.Files.Count);
        Assert.All(review.Files, file => Assert.Equal(("Changed", 3), (file.StatusText, file.Lines.Count)));
        Assert.Equal("+  \"yanked\": true", review.Files[1].Lines[2].Text);
        Assert.Equal("Changed files (3)", review.FilesHeading);
        Assert.Equal((false, false, null), (review.HasDocuments, review.CanRunChecks, review.FromText));
    }

    [Fact]
    public async Task FileWithoutPatch_SaysSo_AndOpensItOnGitHub()
    {
        _queue.Items.Add(FakeStewardQueue.Item(7));
        using var harness = await CreateAsync();
        var pull = FakePullRequestReviews.Review(7);
        _reviews.Reviews[(Index, 7)] = pull with
        {
            Files = [FakePullRequestReviews.File(pull, "packs/my-pack/icon.png", null), FakePullRequestReviews.File(pull, "tags.toml", "@@ -1 +1 @@", "renamed", "old/tags.toml")],
        };
        var viewModel = harness.ViewModel;
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;
        var review = await OpenReviewAsync(viewModel);

        var binary = review.Files[0];
        binary.OpenCommand.Execute(null);

        Assert.Equal((false, 0), (binary.HasPatch, binary.Lines.Count));
        Assert.Equal([PullRequestFile.DiffUrlOf(pull.Url, "packs/my-pack/icon.png").AbsoluteUri], opened);
        Assert.Equal("Renamed from old/tags.toml", review.Files[1].StatusText);
    }

    [Fact]
    public async Task DocumentThatDoesNotParse_ShowsTheErrorAndTheText_AndAPackShowsItsVersionAndMembers()
    {
        const string Broken = "id = \"MyMod\"\nname = ";
        _queue.Items.Add(FakeStewardQueue.Item(8));
        using var harness = await CreateAsync();
        _reviews.Reviews[(Index, 8)] = FakePullRequestReviews.Review(8) with
        {
            Documents =
            [
                new PullRequestDocument("listings/MyMod.toml", StewardQueueKind.Listing, Broken, null, "Expected a value at line 2.", null),
                new PullRequestDocument("listings/Binary.toml", StewardQueueKind.Listing, null, null, null, null),
                new PullRequestDocument("packs/my-pack/1.2.0.toml", StewardQueueKind.Pack, PackText, harness.Services.ListingFormat.Read(PackText), null, null),
            ],
        };

        var review = await OpenReviewAsync(harness.ViewModel);

        var broken = review.Documents[0];
        Assert.Equal((false, "The document does not parse: Expected a value at line 2.", Broken, false), (broken.IsPreview, broken.Problem, broken.RawText, broken.HasOwnership));
        var binary = review.Documents[1];
        Assert.Equal((false, "The file is no UTF-8 text.", null), (binary.IsPreview, binary.Problem, binary.RawText));
        var pack = review.Documents[2];
        Assert.Equal((true, null, "Pack", "My Pack", "Version 1.2.0", false), (pack.IsPreview, pack.Problem, pack.KindText, pack.Name, pack.VersionText, pack.HasOwnership));
        Assert.Equal(["MyMod 1.0.0", "Other 2.1.0", "SomeSave 1.0.0"], pack.Members);
    }

    [Fact]
    public async Task NewCommitWhileTheReviewIsOpen_RefreshShowsIt()
    {
        _queue.Items.Add(FakeStewardQueue.Item(5));
        using var harness = await CreateAsync();
        var before = FakePullRequestReviews.Review(5);
        _reviews.Reviews[(Index, 5)] = before with
        {
            Files = [FakePullRequestReviews.File(before, "listings/MyMod.toml", "@@ -0,0 +1 @@\n+id = \"MyMod\"")],
            Documents = [Listing(harness, ListingOwnership.Unknown)],
        };
        var review = await OpenReviewAsync(harness.ViewModel);
        Assert.Equal(["listings/MyMod.toml"], review.Files.Select(file => file.File.Path));
        Assert.Equal(["My Mod"], review.Documents.Select(document => document.Name));
        var after = FakePullRequestReviews.Review(5) with { HeadCommit = Second, Validate = new ValidateStatus(ValidateState.Pending, "the checks run") };
        _reviews.Reviews[(Index, 5)] = after with
        {
            Files = [FakePullRequestReviews.File(after, "packs/my-pack/1.2.0.toml", "@@ -0,0 +1 @@\n+id = \"my-pack\"")],
            Documents = [new PullRequestDocument("packs/my-pack/1.2.0.toml", StewardQueueKind.Pack, PackText, harness.Services.ListingFormat.Read(PackText), null, null)],
        };

        await harness.ViewModel.StewardPage.RefreshTabCommand.ExecuteAsync(null);

        Assert.Same(review, harness.ViewModel.StewardPage.Review);
        Assert.Equal([(Index, 5), (Index, 5)], _reviews.Reads);
        Assert.Equal(("Head commit 89abcde", "validate: running", false, false), (review.CommitText, review.ValidateText, review.IsValidateSuccess, review.IsValidateBad));
        Assert.Equal(["packs/my-pack/1.2.0.toml"], review.Files.Select(file => file.File.Path));
        Assert.Equal(["My Pack"], review.Documents.Select(document => document.Name));
        Assert.Empty(_queue.Filters.Skip(1));
    }

    [Theory]
    [InlineData(ValidateState.Success, "validate: passed")]
    [InlineData(ValidateState.Failure, "validate: failed")]
    [InlineData(ValidateState.Error, "validate: the checks could not decide")]
    [InlineData(ValidateState.Pending, "validate: running")]
    [InlineData(ValidateState.Missing, "validate: not set on this commit yet")]
    [InlineData(ValidateState.Unknown, "validate: a state that Borea does not know")]
    public async Task EveryValidateState_HasItsText(ValidateState state, string text)
    {
        _queue.Items.Add(FakeStewardQueue.Item(5));
        using var harness = await CreateAsync();
        _reviews.Reviews[(Index, 5)] = FakePullRequestReviews.Review(5) with { Validate = new ValidateStatus(state) };

        var review = await OpenReviewAsync(harness.ViewModel);

        Assert.Equal(text, review.ValidateText);
        Assert.Equal(state is ValidateState.Failure or ValidateState.Error, review.IsValidateBad);
    }

    [Fact]
    public async Task PullRequestWithOtherFiles_OpensOnGitHubAndNotInBorea()
    {
        _queue.Items.Add(FakeStewardQueue.Item(26) with { HasOtherFiles = true });
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;
        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.Queue.WhenLoadedAsync();
        viewModel.StewardPage.Queue.SelectScopeCommand.Execute(StewardQueueScope.Other);

        await viewModel.StewardPage.Queue.Items[0].OpenCommand.ExecuteAsync(null);

        Assert.Equal([$"https://github.com/{Index}/pull/26"], opened);
        Assert.False(viewModel.StewardPage.IsReviewOpen);
        Assert.Empty(_reviews.Reads);
    }

    [Fact]
    public async Task Back_ShowsTheQueueAgain_AndSettingsOpensThePageOnItsTabs()
    {
        _queue.Items.Add(FakeStewardQueue.Item(5));
        using var harness = await CreateAsync();
        _reviews.Reviews[(Index, 5)] = FakePullRequestReviews.Review(5);
        var viewModel = harness.ViewModel;
        var page = viewModel.StewardPage;

        await OpenReviewAsync(viewModel);
        page.CloseReviewCommand.Execute(null);
        var closed = (page.IsReviewOpen, page.IsQueueTab);
        await OpenReviewAsync(viewModel);
        viewModel.OpenStewardPageCommand.Execute(null);
        await page.Queue.WhenLoadedAsync();

        Assert.Equal((false, true), closed);
        Assert.False(page.IsReviewOpen);
        Assert.Null(page.Review);
    }

    [Theory]
    [InlineData(StewardFailure.NotFound, "GitHub did not find this pull request.")]
    [InlineData(StewardFailure.Forbidden, "GitHub refused access, maybe because the Borea App is not installed on this repository.")]
    [InlineData(StewardFailure.SignedOut, "GitHub signed you out. Sign in again in Settings.")]
    public async Task ReadFails_SaysWhy(StewardFailure failure, string text)
    {
        _queue.Items.Add(FakeStewardQueue.Item(5));
        using var harness = await CreateAsync();
        _reviews.Failure = new StewardException(failure);

        var review = await OpenReviewAsync(harness.ViewModel);

        Assert.Equal(text, review.Error);
        Assert.Null(review.CommitText);
        Assert.Equal("Pull 5", review.Title);
    }

    [Theory]
    [InlineData(StewardFailure.Forbidden, "Cannot read validate on this commit. GitHub refused access.")]
    [InlineData(StewardFailure.NetworkError, "Cannot read validate on this commit. Cannot reach GitHub. Try again.")]
    [InlineData(StewardFailure.NotFound, "Cannot read validate on this commit. GitHub did not find this commit.")]
    [InlineData(StewardFailure.Refused, "Cannot read validate on this commit. GitHub did not find this commit.")]
    public async Task ValidateCannotBeRead_SaysWhy_AndTheRestShows(StewardFailure failure, string text)
    {
        _queue.Items.Add(FakeStewardQueue.Item(5));
        using var harness = await CreateAsync();
        _reviews.Reviews[(Index, 5)] = FakePullRequestReviews.Review(5) with { Validate = null, ValidateFailure = new StewardException(failure), Verdict = "Validated." };

        var review = await OpenReviewAsync(harness.ViewModel);

        Assert.Equal((text, true, false), (review.ValidateText, review.IsValidateBad, review.IsValidateSuccess));
        Assert.Equal("Validated.", review.Verdict);
    }

    [Theory]
    [InlineData(PullRequestState.Closed, "Closed")]
    [InlineData(PullRequestState.Merged, "Merged")]
    public async Task ClosedOrMergedDraft_SaysSo(PullRequestState state, string text)
    {
        _queue.Items.Add(FakeStewardQueue.Item(5));
        using var harness = await CreateAsync();
        _reviews.Reviews[(Index, 5)] = FakePullRequestReviews.Review(5) with { State = state, IsDraft = true, HeadRepository = null, Author = null };

        var review = await OpenReviewAsync(harness.ViewModel);

        Assert.Equal((text, true, "from a repository that no longer exists"), (review.StateText, review.IsDraft, review.FromText));
        Assert.Null(review.ByText);
    }

    [Fact]
    public async Task LanguageSwitch_ShowsTheReviewInTheNewLanguage()
    {
        _queue.Items.Add(FakeStewardQueue.Item(5));
        using var harness = await CreateAsync();
        var pull = FakePullRequestReviews.Review(5, headRepository: "alice/content-index");
        _reviews.Reviews[(Index, 5)] = pull with
        {
            Files = [FakePullRequestReviews.File(pull, "listings/MyMod.toml", "@@")],
            Documents = [Listing(harness, new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.Owner, Repository: "alice/MyMod"))],
        };
        var review = await OpenReviewAsync(harness.ViewModel);
        var changed = new List<string?>();
        review.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        harness.Localization.TrySetCulture("de");

        Assert.Contains(changed, name => string.IsNullOrEmpty(name) || name == nameof(StewardReview.ValidateText));
        Assert.Equal(("validate: bestanden", "aus alice/content-index", "Head-Commit 0123456"), (review.ValidateText, review.FromText, review.CommitText));
        Assert.Equal("Neu", review.Files[0].StatusText);
        Assert.Equal(("alice hat einen Eigentumsnachweis.", "alice besitzt alice/MyMod."), (review.Documents[0].OwnershipText, review.Documents[0].OwnershipDetail));
    }

    [Theory]
    [InlineData(ListingOwnershipState.Verified, ListingOwnershipProof.Owner, null, "alice has an ownership proof.", "The source code link of SpaceDock mod 4256 names Studio/MyMod. alice owns Studio/MyMod.")]
    [InlineData(ListingOwnershipState.Verified, ListingOwnershipProof.Topic, null, "alice has an ownership proof.", "The source code link of SpaceDock mod 4256 names Studio/MyMod. Studio/MyMod has the topic ksa-index-alice.")]
    [InlineData(ListingOwnershipState.Verified, ListingOwnershipProof.MarkerFile, null, "alice has an ownership proof.", "The source code link of SpaceDock mod 4256 names Studio/MyMod. Studio/MyMod names alice in .github/ksa-content-index.toml.")]
    [InlineData(ListingOwnershipState.NotVerified, null, ListingOwnershipProblem.NoHost, "alice has no ownership proof, so a steward decides.", "The listing names no GitHub repository and no SpaceDock mod under releases.")]
    [InlineData(ListingOwnershipState.NotVerified, null, ListingOwnershipProblem.RepositoryMissing, "alice has no ownership proof, so a steward decides.", "The source code link of SpaceDock mod 4256 names Studio/MyMod. GitHub has no public repository Studio/MyMod.")]
    [InlineData(ListingOwnershipState.NotVerified, null, ListingOwnershipProblem.RepositoryFork, "alice has no ownership proof, so a steward decides.", "The source code link of SpaceDock mod 4256 names Studio/MyMod. alice does not own the fork Studio/MyMod, and Studio/MyMod does not have the topic ksa-index-alice. A marker file on a fork proves nothing, because a fork inherits it.")]
    [InlineData(ListingOwnershipState.NotVerified, null, ListingOwnershipProblem.NoProof, "alice has no ownership proof, so a steward decides.", "The source code link of SpaceDock mod 4256 names Studio/MyMod. alice does not own Studio/MyMod, and Studio/MyMod has neither the topic ksa-index-alice nor a marker file that names alice.")]
    [InlineData(ListingOwnershipState.NotVerified, null, ListingOwnershipProblem.RepositoryRenamed, "alice has no ownership proof, so a steward decides.", "The source code link of SpaceDock mod 4256 names Studio/MyMod. Studio/MyMod is now Studio/MyNewMod.")]
    [InlineData(ListingOwnershipState.NotVerified, null, ListingOwnershipProblem.SpaceDockModUnusable, "alice has no ownership proof, so a steward decides.", "SpaceDock mod 4256 is not a published Kitten Space Agency mod.")]
    [InlineData(ListingOwnershipState.NotVerified, null, ListingOwnershipProblem.SpaceDockNoSourceLink, "alice has no ownership proof, so a steward decides.", "The source code link of SpaceDock mod 4256 names no GitHub repository.")]
    [InlineData(ListingOwnershipState.CouldNotEvaluate, null, null, "Borea could not check the ownership proof of alice.", null)]
    public void Ownership_NamesTheProofOfTheAuthor_OrTheOneThatIsMissing(ListingOwnershipState state, ListingOwnershipProof? proof, ListingOwnershipProblem? problem, string text, string? detail)
    {
        var viewModel = new MainViewModel(new LocalizationService());
        var review = new StewardReview(viewModel, FakeStewardQueue.Item(5));
        var ownership = new ListingOwnership(state, proof, problem, "Studio/MyMod", "4256", "Studio/MyNewMod");

        var document = new StewardReviewDocument(viewModel, review, new PullRequestDocument("listings/MyMod.toml", StewardQueueKind.Listing, "id = \"MyMod\"", new AuthoredTable(), null, ownership), "alice");
        var gone = new StewardReviewDocument(viewModel, review, new PullRequestDocument("listings/MyMod.toml", StewardQueueKind.Listing, "id = \"MyMod\"", new AuthoredTable(), null, ownership), null);

        Assert.Equal((text, detail, state == ListingOwnershipState.Verified), (document.OwnershipText, document.OwnershipDetail, document.IsOwnershipVerified));
        Assert.Equal((viewModel.Localization.StewardReviewOwnershipNoAuthor, null), (gone.OwnershipText, gone.OwnershipDetail));
        Assert.Equal(
            ["repository", "spacedock"],
            document.OwnershipLinks.Select(link => link.Key));
    }

    [Theory]
    [InlineData("en", "alice does not own the fork alice/Fork, and alice/Fork does not have the topic ksa-index-alice. A marker file on a fork proves nothing, because a fork inherits it.")]
    [InlineData("de", "alice besitzt den Fork alice/Fork nicht, und alice/Fork hat das Topic ksa-index-alice nicht. Eine Markierungsdatei auf einem Fork weist nichts nach, weil ein Fork sie erbt.")]
    public void Ownership_ForkOnGitHub_NamesTheMissingOwnerAndTopicWithoutALinkSentence(string culture, string detail)
    {
        var viewModel = new MainViewModel(new LocalizationService(CultureInfo.GetCultureInfo(culture)));
        var review = new StewardReview(viewModel, FakeStewardQueue.Item(5));
        var ownership = new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.RepositoryFork, Repository: "alice/Fork");

        var document = new StewardReviewDocument(viewModel, review, new PullRequestDocument("listings/MyMod.toml", StewardQueueKind.Listing, "id = \"MyMod\"", new AuthoredTable(), null, ownership), "alice");

        Assert.Equal(detail, document.OwnershipDetail);
        Assert.Equal(["repository"], document.OwnershipLinks.Select(link => link.Key));
    }

    private static PullRequestDocument Listing(ViewModelHarness harness, ListingOwnership ownership) =>
        new("listings/MyMod.toml", StewardQueueKind.Listing, ListingText, harness.Services.ListingFormat.Read(ListingText), null, ownership);

    private static async Task<StewardReview> OpenReviewAsync(MainViewModel viewModel)
    {
        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.Queue.WhenLoadedAsync();
        await viewModel.StewardPage.Queue.Items[0].OpenCommand.ExecuteAsync(null);
        return viewModel.StewardPage.Review!;
    }

    private async Task<ViewModelHarness> CreateAsync()
    {
        _session.SignInDirectly();
        var harness = await ViewModelHarness.CreateAsync(gitHub: _session, indexStatusEditor: new FakeIndexStatusEditor(), stewardQueue: _queue, pullRequestReviews: _reviews);
        await harness.ViewModel.WhenStewardRoleCheckedAsync();
        return harness;
    }
}
