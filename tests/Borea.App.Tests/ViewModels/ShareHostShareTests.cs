using System.Collections.Concurrent;
using System.Net;
using Borea.App.ViewModels;
using Borea.Core.GitHub;
using Borea.Core.Index;
using Borea.Core.Listings;
using Borea.Core.Mods;
using Borea.Core.Sharing;

namespace Borea.App.Tests.ViewModels;

/// <summary>Share on a share host of RFC 0094 from the listing editor, against a fake host.</summary>
public sealed class ShareHostShareTests
{
    private static readonly string VehiclesUrl = ContentIndexTypeFiles.Vehicles.AddressFrom(new Uri("https://ksamodding.github.io/content-index-releases/v1/index.json")).AbsoluteUri;

    private const string Hosts = """
        { "ksamods": {
            "kind": "share", "name": "ksamods.gg", "api": "https://ksamods.gg/api/share/v1/",
            "types": ["vehicle", "save"], "max_bytes": { "vehicle": 10485760, "save": 104857600 }
        } }
        """;

    /// <summary>A host without GitHub storage (RFC 0104), which serves signed data files and has no pages.</summary>
    private const string DataHost = """
        { "ownhost": {
            "kind": "share", "name": "own.example", "api": "https://own.example/api/share/v1/", "types": ["vehicle"],
            "data": "https://data.own.example/share/", "downloads": ["https://cdn.own.example/"],
            "key": "MCowBQYDK2VwAyEAGb9ECWmEzf6FQbrBZ9w7lshQhqowtrbLDFw4rXAxZuE=", "retired_keys": []
        } }
        """;

    private static readonly ShareUpload Checked = new("u42", [new ShareCheck("meta.toml", true, null), new ShareCheck("folder", true, null)], "1.0.0", "id = \"ExampleRover\"\n");

    [Fact]
    public async Task FirstShare_SignsInUploadsShowsTheChecksAndPublishes()
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
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
        Assert.Equal([new ShareCheckRow(harness.Localization.FormatListingShareCheckPassed("meta.toml"), true), new ShareCheckRow(harness.Localization.FormatListingShareCheckPassed("folder"), true)], editor.ShareHostChecks);
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
    public async Task Publish_RemembersTheListing_AndTheNextShareStartsWithItsAuthorAndLicense()
    {
        var page = new Uri("https://ksamods.gg/vehicles/ExampleRover");
        var host = new FakeHost { Page = page };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await ShareAsync(harness);
        Assert.Equal((string.Empty, string.Empty), (editor.Authors, editor.License));
        var row = Assert.Single(harness.ViewModel.VehiclesSection.Items);
        Assert.False(row.ShowsListingLine);

        editor.Authors = "Maxi, Cairn";
        editor.License = "CC-BY-4.0";
        editor.Abstract = "A six-wheel rover for the Moon.";
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        Assert.Equal(
            new SharedContentEntry(ContentType.Vehicle, "ksamods", "ksamods.gg", "ExampleRover", "Example Rover", "1.0.0", page),
            Assert.Single(await harness.Services.SharedContent.ListAsync()));
        Assert.Equal(harness.Localization.FormatContentSharedOn("ksamods.gg"), row.SharedOnText);
        Assert.True(row.ShowsListingLine);

        await row.ShareCommand.ExecuteAsync(null);

        Assert.Equal(("Maxi, Cairn", "CC-BY-4.0"), (editor.Authors, editor.License));
    }

    [Fact]
    public async Task Next_WaitsForTheRequiredFieldsAndNamesWhatIsMissing_AndSendsNothing()
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var text = harness.Localization;
        var editor = await ShareAsync(harness);

        Assert.True(editor.IsShareStep);
        Assert.Equal(text.FormatShareItemIn("Example Rover", "Main"), editor.ShareItemText);
        Assert.Equal(text.FormatListingStillMissing(string.Join(", ", text.ShareMissingAuthor, text.ShareMissingSentence, text.ShareMissingLicense)), editor.ShareNextReason);
        Assert.False(editor.CanContinueShare);
        editor.ContinueShareCommand.Execute(null);
        Assert.Equal(ShareFlowStep.Describe, editor.ShareFlowStep);
        Assert.Equal([ListingStepState.Done, ListingStepState.Current, ListingStepState.ToDo, ListingStepState.ToDo], editor.ShareSteps.Select(step => step.State));

        editor.Authors = "Maxi";
        editor.Abstract = "A six-wheel rover for the Moon.";
        editor.License = "CC-BY-4.0";

        Assert.Null(editor.ShareNextReason);
        Assert.True(editor.CanContinueShare);
        editor.ContinueShareCommand.Execute(null);
        Assert.Equal(ShareFlowStep.Publish, editor.ShareFlowStep);
        Assert.Equal([ListingStepState.Done, ListingStepState.Done, ListingStepState.Current, ListingStepState.ToDo], editor.ShareSteps.Select(step => step.State));
        Assert.Equal((0, 0), (host.SignIns, host.Uploads.Count));
    }

    [Theory]
    [InlineData(ListingDraft.AbstractLimit, false)]
    [InlineData(ListingDraft.AbstractLimit + 1, true)]
    public async Task SentenceOverTheLimit_ShowsItsTextAndStopsNext(int length, bool isTooLong)
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost());
        var editor = await ShareAsync(harness);
        editor.Authors = "Maxi";
        editor.License = "CC-BY-4.0";

        // One rune outside the basic plane is two chars, so the limit has to count runes.
        editor.Abstract = string.Concat(Enumerable.Repeat("\U0001F680", length));

        var expected = isTooLong ? harness.Localization.FormatShareSentenceTooLong(length, ListingDraft.AbstractLimit) : null;
        Assert.Equal(expected, editor.ShareSentenceError);
        Assert.Equal(expected, editor.ShareNextReason);
        Assert.Equal(!isTooLong, editor.CanContinueShare);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not an id")]
    [InlineData("TakenRover")]
    public async Task IdError_ShowsItsOwnTextAndOpensMoreOptions(string id)
    {
        var taken = ListedRover.Replace("ExampleRover", "TakenRover", StringComparison.Ordinal).Replace("\"ksamods\": \"TakenRover\"", "\"github\": \"someone/rover\"", StringComparison.Ordinal);
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: taken), shareHostClient: new FakeHost());
        var editor = await ShareAsync(harness);
        editor.Authors = "Maxi";
        editor.License = "CC-BY-4.0";
        editor.Abstract = "A six-wheel rover for the Moon.";
        Assert.False(editor.IsShareMoreOptionsOpen);
        Assert.True(editor.CanContinueShare);

        editor.Id = id;

        var text = harness.Localization;
        Assert.Equal(id switch { "" => text.ShareIdEmpty, "TakenRover" => text.FormatShareIdHeld(id), _ => text.ShareIdInvalid }, editor.ShareIdError);
        Assert.True(editor.IsShareMoreOptionsOpen);
        Assert.False(editor.HasShareOtherFields);
        Assert.False(editor.CanContinueShare);
    }

    [Fact]
    public async Task ErrorOutsideTheStep_LeadsToAllFields_AndStopsTheUpload()
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await ShareAsync(harness);
        editor.Authors = "Maxi";
        editor.License = "CC-BY-4.0";
        editor.Abstract = "A six-wheel rover for the Moon.";

        editor.Homepage = "not a link";

        Assert.True(editor.HasShareOtherFields);
        Assert.Null(editor.ShareNextReason);
        Assert.False(editor.CanContinueShare);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        Assert.Equal(harness.Localization.ShareOtherFields, editor.ShareHostError);
        Assert.Equal((0, 0), (host.SignIns, host.Uploads.Count));

        editor.EditAllShareFieldsCommand.Execute(null);
        Assert.True(editor.IsFormStep);
        editor.Homepage = "https://example.test/rover";
        editor.BackToShareStepsCommand.Execute(null);

        Assert.True(editor.IsShareStep);
        Assert.Equal(("https://example.test/rover", "Maxi"), (editor.Homepage, editor.Authors));
        Assert.True(editor.CanContinueShare);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DependencyWithNoId_StopsNextAndTheUpload(bool isNewVersion)
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: isNewVersion ? ListedRover : null), shareHostClient: host);
        var editor = await ShareAsync(harness);
        editor.Authors = "Maxi";
        editor.License = "CC-BY-4.0";
        editor.Abstract = "A six-wheel rover for the Moon.";
        Assert.True(editor.CanContinueShare);

        editor.AddDependencyCommand.Execute(null);

        Assert.True(editor.HasShareOtherFields);
        Assert.False(editor.CanContinueShare);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        Assert.Equal(harness.Localization.ShareOtherFields, editor.ShareHostError);
        Assert.Empty(host.Uploads);
    }

    [Fact]
    public async Task ErrorAfterTheUpload_StopsThePublish()
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await ShareAsync(harness);
        editor.Authors = "Maxi";
        editor.License = "CC-BY-4.0";
        editor.Abstract = "A six-wheel rover for the Moon.";
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        editor.Homepage = "not a link";
        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.ShareOtherFields, editor.ShareHostError);
        Assert.Empty(host.Publishes);
    }

    [Fact]
    public async Task CancelAfterTheUpload_DropsIt()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost());
        var editor = await ShareAsync(harness);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        editor.CancelShareHostCommand.Execute(null);

        Assert.False(editor.HasShareUpload);
        Assert.Empty(editor.ShareHostChecks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LeavingThePublishStep_DropsTheUpload(bool toAllFields)
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await ShareAsync(harness);
        editor.Authors = "Maxi";
        editor.License = "CC-BY-4.0";
        editor.Abstract = "A six-wheel rover for the Moon.";
        editor.ContinueShareCommand.Execute(null);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        editor.OpenShareStep("choose");
        Assert.True(editor.HasShareUpload);

        if (toAllFields)
            editor.EditAllShareFieldsCommand.Execute(null);
        else
            editor.OpenShareStep("describe");

        Assert.True(toAllFields ? editor.IsFormStep : editor.ShareFlowStep == ShareFlowStep.Describe);
        Assert.False(editor.HasShareUpload);
        Assert.Empty(editor.ShareHostChecks);
        Assert.Equal("Maxi", editor.Authors);
    }

    [Fact]
    public async Task LicenseList_StartsEmpty_WritesTheValueOfTheChoice_AndAnotherLicenseShowsItsField()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost());
        var editor = await ShareAsync(harness);
        Assert.Null(editor.SelectedShareLicense);
        Assert.False(editor.IsOtherShareLicense);

        editor.SelectedShareLicense = editor.ShareLicenseChoices[0];
        Assert.Equal("CC-BY-4.0", editor.License);
        Assert.Equal(editor.ShareLicenseChoices[0], editor.SelectedShareLicense);

        editor.SelectedShareLicense = editor.ShareLicenseChoices[^1];
        Assert.Equal(string.Empty, editor.License);
        Assert.True(editor.IsOtherShareLicense);
        Assert.Equal(editor.ShareLicenseChoices[^1], editor.SelectedShareLicense);

        editor.License = "MIT";
        Assert.Equal(editor.ShareLicenseChoices[^1], editor.SelectedShareLicense);
    }

    [Fact]
    public async Task LicenseFromTheLastShareOffTheList_KeepsItsFieldWhileThePlayerEditsIt()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost());
        await harness.Services.SharedContent.RecordAsync(new SharedContentEntry(ContentType.Vehicle, "ksamods", "ksamods.gg", "OtherRover", "Other Rover", "1.0.0", null), new SharedContentAuthor(["Maxi"], "MIT"));
        var editor = await ShareAsync(harness);
        Assert.True(editor.IsOtherShareLicense);

        editor.License = string.Empty;
        Assert.True(editor.IsOtherShareLicense);

        editor.License = "CC0-1.0";
        Assert.True(editor.IsOtherShareLicense);
        Assert.Equal(editor.ShareLicenseChoices[^1], editor.SelectedShareLicense);
    }

    [Fact]
    public async Task LicenseThatBoreaDoesNotKnow_ShowsItsOwnTextAndStopsNext()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost());
        var editor = await ShareAsync(harness);
        editor.Authors = "Maxi";
        editor.Abstract = "A six-wheel rover for the Moon.";
        editor.SelectedShareLicense = editor.ShareLicenseChoices[^1];

        editor.License = "My own license";

        Assert.Equal(harness.Localization.ShareLicenseInvalid, editor.ShareLicenseError);
        Assert.Equal(harness.Localization.ShareLicenseInvalid, editor.ShareNextReason);
        Assert.False(editor.HasShareOtherFields);
        Assert.False(editor.CanContinueShare);
    }

    [Fact]
    public async Task NewVersion_ShowsNoIdErrorAndNoMissingLine_AndNextIsEnabled()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: ListedRover), shareHostClient: new FakeHost());

        var editor = await ShareAsync(harness);

        Assert.True(editor.IsNewVersionShare);
        Assert.Equal(harness.Localization.FormatShareNewVersionLine("Example Rover", "ksamods.gg"), editor.ShareNewVersionText);
        Assert.DoesNotContain(editor.Errors, issue => issue.Location == "id");
        Assert.Null(editor.ShareIdError);
        Assert.Null(editor.ShareNextReason);
        Assert.False(editor.HasShareOtherFields);
        Assert.False(editor.ShowsPreviewCard);
        Assert.True(editor.CanContinueShare);

        // The full form can open a pull request for a new file, so there the id counts as held.
        editor.EditAllShareFieldsCommand.Execute(null);
        editor.Authors = "Maxi";
        editor.License = "CC-BY-4.0";
        editor.Abstract = "A six-wheel rover for the Moon.";
        Assert.Contains(editor.Errors, issue => issue.Location == "id");
        Assert.False(editor.CanOpenPullRequest);

        editor.BackToShareStepsCommand.Execute(null);
        Assert.DoesNotContain(editor.Errors, issue => issue.Location == "id");
        Assert.True(editor.CanContinueShare);
    }

    [Theory]
    [InlineData("ExampleRover", "ExampleRover")]
    [InlineData("MyRover", null)]
    public async Task IdOnTheFullForm_DecidesWhetherTheShareStaysANewVersion(string id, string? listingId)
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: ListedRover), shareHostClient: host);
        var editor = await ShareAsync(harness);

        editor.EditAllShareFieldsCommand.Execute(null);
        editor.Id = id;
        editor.BackToShareStepsCommand.Execute(null);

        Assert.Equal(listingId is not null, editor.IsNewVersionShare);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        Assert.Equal(listingId, Assert.Single(host.Uploads).ListingId);
    }

    [Fact]
    public async Task ShareStartedWithoutTheIndex_FindsItsListingAndHostOnceTheIndexIsBack()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: ListedRover), shareHostClient: new FakeHost(), indexOffline: true);
        var editor = await ShareAsync(harness);
        Assert.False(editor.IsNewVersionShare);
        Assert.Empty(editor.ShareHostRows);

        harness.IndexOffline = false;
        await harness.ViewModel.OpenListingAsync();
        await editor.ShareHostsLoad;

        Assert.True(editor.IsNewVersionShare);
        Assert.Null(editor.ShareIdError);
        Assert.Single(editor.ShareHostRows);
    }

    [Fact]
    public async Task NewVersion_OffersOnlyTheHostOfItsListing()
    {
        const string twoHosts = """
            {
              "ksamods": { "kind": "share", "name": "ksamods.gg", "api": "https://ksamods.gg/api/share/v1/", "types": ["vehicle"] },
              "another": { "kind": "share", "name": "another.example", "api": "https://another.example/api/share/v1/", "types": ["vehicle"] }
            }
            """;
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(twoHosts), respond: Serve(vehicles: ListedRover), shareHostClient: new FakeHost());

        var editor = await ShareAsync(harness);

        Assert.Equal("ksamods", Assert.Single(editor.ShareHostRows).Host.Key);
    }

    [Fact]
    public async Task HostError_ShowsItsMessage()
    {
        var host = new FakeHost { UploadFailure = new ShareHostException(ShareHostFailure.Host, "ExampleRover belongs to another account.", "id_taken") };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await ShareAsync(harness);
        editor.Homepage = "https://example.test/rover";

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        Assert.Equal(harness.Localization.FormatListingShareHostSaid("ksamods.gg", "ExampleRover belongs to another account."), editor.ShareHostError);
        Assert.False(editor.HasShareUpload);
        Assert.Null(editor.ShareHostProgress);
        Assert.Equal("https://example.test/rover", editor.Homepage);
    }

    [Fact]
    public async Task ArchiveOverTheLimitOfTheHost_SendsNothing()
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts.Replace("\"vehicle\": 10485760", "\"vehicle\": 10", StringComparison.Ordinal)), respond: Serve(), shareHostClient: host);
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
        var host = new FakeHost { Upload = new ShareUpload("u42", [new ShareCheck("meta.toml", true, null), new ShareCheck("folder", false, "Taken by OtherRover.")], "1.0.0", null) };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await ShareAsync(harness);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        var failed = editor.ShareHostChecks[1];
        Assert.Equal((harness.Localization.FormatListingShareCheckFailed("folder", "Taken by OtherRover."), true), (failed.Text, failed.Failed));
        Assert.False(editor.ShareHostChecks[0].Failed);
        Assert.False(editor.CanPublishToShareHost);
        Assert.Equal(harness.Localization.ListingShareHostChecksFailed, editor.ShareHostError);
        Assert.False(editor.HasShareUpload);
    }

    [Fact]
    public async Task RefusedUpload_ShowsItsChecks()
    {
        var failure = new ShareHostException(ShareHostFailure.Host, "A check failed.", "invalid_archive") { Checks = [new ShareCheck("meta.toml", true, null), new ShareCheck("meta.toml", true, "Read twice."), new ShareCheck("folder", false, "Taken by OtherRover.")] };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost { UploadFailure = failure });
        var editor = await ShareAsync(harness);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        Assert.Equal([new ShareCheckRow(harness.Localization.FormatListingShareCheckPassed("meta.toml"), true), new ShareCheckRow(harness.Localization.FormatListingShareCheckFailed("folder", "Taken by OtherRover."), false)], editor.ShareHostChecks);
        Assert.Equal(harness.Localization.FormatListingShareHostSaid("ksamods.gg", "A check failed."), editor.ShareHostError);
        Assert.False(editor.HasShareUpload);
    }

    [Fact]
    public async Task RefusedToken_SignsInAgainAndSendsOnceMore()
    {
        var host = new FakeHost { TokenRefusals = 1 };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: ListedRover), shareHostClient: host);
        var editor = await ShareAsync(harness);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        Assert.Equal((2, 2), (host.SignIns, host.Uploads.Count));
        Assert.True(editor.CanPublishToShareHost);

        host.TokenRefusals = 1;
        host.DuringSignIn = () =>
        {
            editor.ShareHostVersion = "2.0.0";
            editor.ShareHostStatus = "dev";
            return Task.CompletedTask;
        };
        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        Assert.Equal((3, 2), (host.SignIns, host.Publishes.Count));
        Assert.All(host.Publishes, publish => Assert.Equal(("1.0.0", "stable"), (publish.Version, publish.Status)));
        Assert.Equal(harness.Localization.FormatListingShareHostPublished("ExampleRover", "1.0.0"), editor.ShareHostOutcome);
        Assert.Null(editor.ShareHostError);
        Assert.Equal(0, host.SignOuts);
    }

    [Fact]
    public async Task SignInThatFailsDuringAPublish_TakesTheCodeAwayAndKeepsTheUpload()
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: ListedRover), shareHostClient: host);
        var editor = await ShareAsync(harness);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        var codeShown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        editor.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ListingEditor.ShareHostUserCode) && editor.ShareHostUserCode is not null)
                codeShown.TrySetResult();
        };
        var signIn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.TokenRefusals = 1;
        host.DuringSignIn = () => signIn.Task;
        var publish = editor.PublishToShareHostCommand.ExecuteAsync(null);
        await codeShown.Task.WaitAsync(TimeSpan.FromSeconds(30));

        signIn.SetException(new ShareHostException(ShareHostFailure.SignInRefused, "The sign-in code expired.", "expired_token"));
        await publish;

        Assert.Null(editor.ShareHostUserCode);
        Assert.Null(editor.ShareHostCodeText);
        Assert.Equal(harness.Localization.ListingShareHostSignInRefused, editor.ShareHostError);
        Assert.True(editor.CanPublishToShareHost);
    }

    [Fact]
    public async Task CancelAtTheEndOfASignIn_SendsNothingAndLeavesNoProgress()
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await ShareAsync(harness);
        host.DuringSignIn = () =>
        {
            editor.CancelShareHostCommand.Execute(null);
            return Task.CompletedTask;
        };

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        Assert.Empty(host.Uploads);
        Assert.Null(editor.ShareHostProgress);
    }

    [Fact]
    public async Task TokenRefusedAfterANewSignIn_StopsWithTheSignedOutText()
    {
        var host = new FakeHost { TokenRefusals = 2 };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await ShareAsync(harness);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        Assert.Equal((2, 2), (host.SignIns, host.Uploads.Count));
        Assert.Equal(harness.Localization.ListingShareHostSignedOut, editor.ShareHostError);
        Assert.False(editor.HasShareUpload);
        Assert.Null(editor.ShareHostProgress);
        Assert.Equal(1, host.SignOuts);
    }

    [Fact]
    public async Task VehicleListedOnTheHost_UploadsAsANewVersionOfItsListing()
    {
        // The dependency list did not change, so the host publishes the release without a listing pull request.
        var host = new FakeHost { PullRequest = null };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: ListedRover), shareHostClient: host);
        var editor = await ShareAsync(harness);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        Assert.Equal("ExampleRover", Assert.Single(host.Uploads).ListingId);
        Assert.Null(Assert.Single(host.Publishes).Listing);
        Assert.Equal(harness.Localization.FormatListingShareHostReleased("ExampleRover", "1.0.0"), editor.ShareHostOutcome);
        Assert.Null(editor.SharePullRequest);
        Assert.Null(editor.ShareHostError);
        Assert.False(editor.HasShareUpload);
        Assert.Equal("ExampleRover", Assert.Single(await harness.Services.SharedContent.ListAsync()).ListingId);
        Assert.Null(await harness.Services.SharedContent.GetAuthorAsync());
    }

    [Fact]
    public async Task NewVersionAnswerWithoutId_ShowsTheIdOfTheListing()
    {
        var host = new FakeHost { PublishedId = null, PullRequest = null };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: ListedRover), shareHostClient: host);
        var editor = await ShareAsync(harness);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatListingShareHostReleased("ExampleRover", "1.0.0"), editor.ShareHostOutcome);
    }

    [Fact]
    public async Task NoHostInContentIndex_OffersNone()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve(), shareHostClient: new FakeHost());

        var editor = await ShareAsync(harness);

        Assert.False(editor.HasShareHosts);
        Assert.DoesNotContain(harness.Requests, uri => uri.AbsolutePath.EndsWith("/hosts.toml", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HostWithoutStorage_IsOfferedByItsName()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(DataHost), respond: Serve(), shareHostClient: new FakeHost());

        var editor = await ShareAsync(harness);

        Assert.Equal(harness.Localization.FormatListingShareOnHost("own.example"), Assert.Single(editor.ShareHostRows).Label);
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

    /// <summary>Serves the schema without its gate and the vehicles of the index.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage?> Serve(string? vehicles = null) => request => request.RequestUri?.AbsoluteUri switch
    {
        { } uri when uri == VehiclesUrl && vehicles is not null => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(vehicles) },
        _ => null,
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

        public string? PublishedId { get; init; } = "ExampleRover";

        public Uri? Page { get; init; }

        /// <summary>How many of the next requests the host answers with a refused token.</summary>
        public int TokenRefusals { get; set; }

        /// <summary>Runs after the code shows and before the sign-in ends, for what the player does in that time.</summary>
        public Func<Task>? DuringSignIn { get; set; }

        public int SignOuts { get; private set; }

        public bool IsSignedIn(ShareHost host) => _signedIn;

        public async Task SignInAsync(ShareHost host, IProgress<GitHubDeviceCode>? progress = null, bool keepSignedIn = false, CancellationToken cancellationToken = default)
        {
            SignIns++;
            progress?.Report(new GitHubDeviceCode("ROVR-1234", "https://ksamods.gg/device"));
            if (DuringSignIn is { } during)
                await during();

            _signedIn = true;
        }

        public void SignOut(ShareHost host)
        {
            SignOuts++;
            _signedIn = false;
        }

        public Task<ShareUpload> UploadAsync(ShareHost host, Stream archive, string fileName, IReadOnlyList<ListingDependency> dependencies, string? listingId, CancellationToken cancellationToken = default)
        {
            Uploads.Enqueue(new Upload(fileName, archive.Length, dependencies, listingId));

            // The real client disposes the stream with its request, so a second send needs a new one.
            archive.Dispose();
            if (RefuseToken() is { } refused)
                return Task.FromException<ShareUpload>(refused);

            return UploadFailure is { } failure ? Task.FromException<ShareUpload>(failure) : Task.FromResult(Upload);
        }

        public Task<SharePublished> PublishAsync(ShareHost host, string uploadId, string version, string status, ListingDraft? listing, CancellationToken cancellationToken = default)
        {
            Publishes.Enqueue(new Publish(uploadId, version, status, listing));
            return RefuseToken() is { } refused ? Task.FromException<SharePublished>(refused) : Task.FromResult(new SharePublished(PublishedId, version, PullRequest, Page));
        }

        private ShareHostException? RefuseToken()
        {
            if (TokenRefusals == 0)
                return null;

            TokenRefusals--;
            _signedIn = false;
            return new ShareHostException(ShareHostFailure.SignedOut, "ksamods.gg no longer takes the sign-in.");
        }
    }
}
