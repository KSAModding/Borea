using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using Borea.App.ViewModels;
using Borea.Core.GitHub;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Listings;
using Borea.Core.Mods;
using Borea.Core.Preview;
using Borea.Core.Sharing;
using Borea.Preview.Rendering;

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

    private const string TwoHosts = """
        {
          "ksamods": { "kind": "share", "name": "ksamods.gg", "api": "https://ksamods.gg/api/share/v1/", "types": ["vehicle"] },
          "another": { "kind": "share", "name": "another.example", "api": "https://another.example/api/share/v1/", "types": ["vehicle"] }
        }
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
        Assert.Equal(harness.Localization.FormatShareCheckOnHost("ksamods.gg"), editor.ShareCheckOnHostText);
        Assert.Equal(harness.Localization.FormatShareCheckHint("ksamods.gg"), editor.ShareCheckHintText);
        await editor.UploadToShareHostCommand.ExecuteAsync(row);

        Assert.Equal(1, host.SignIns);
        var upload = Assert.Single(host.Uploads);
        Assert.Equal(("Example Rover.zip", null), (upload.FileName, upload.ListingId));
        Assert.True(upload.Size > 0);
        Assert.Equal(("ExamplePartsMod", "required"), (upload.Dependencies[0].Id, upload.Dependencies[0].Kind));
        Assert.Equal([harness.Localization.FormatListingShareCheckPassed("meta.toml"), harness.Localization.FormatListingShareCheckPassed("folder")], editor.ShareHostPassedChecks);
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
        Assert.Equal(ShareFlowStep.Done, editor.ShareFlowStep);
        Assert.All(editor.ShareSteps, step => Assert.Equal(ListingStepState.Done, step.State));
        Assert.Null(editor.ShareHostError);
    }

    [Fact]
    public async Task Pictures_GoUpInOrderRightAfterThePublish_AndTheDoneStepSaysSo()
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await SharePicturesAsync(harness, 2);
        Assert.Equal([true, false], editor.SharePictures.Select(row => row.IsCover));
        Assert.Equal(harness.Localization.FormatShareSummaryPictures(2), editor.ShareSummaryPicturesText);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        Assert.Empty(host.ImageUploads);
        var thumbnailReleased = false;
        editor.SharePictures[0].PropertyChanged += (_, args) => thumbnailReleased |= args.PropertyName == nameof(SharePictureRow.Bytes);
        bool? keptOnTheDoneStep = null;
        editor.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ListingEditor.ShareFlowStep) && editor.ShareFlowStep == ShareFlowStep.Done)
                keptOnTheDoneStep = editor.HasShareOnTheWay && editor.IsSendingShare && !editor.CanCancelShareHost;
        };

        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        Assert.Equal([new ImageUpload("vehicle", "ExampleRover", Picture(1).Length), new ImageUpload("vehicle", "ExampleRover", Picture(2).Length)], host.ImageUploads);
        Assert.Equal(ShareFlowStep.Done, editor.ShareFlowStep);
        Assert.All(editor.SharePictures, row => Assert.True(row.IsSent));
        Assert.Equal(harness.Localization.SharePicturesSent, editor.SharePicturesResultText);
        Assert.False(editor.CanRetrySharePictures);
        Assert.False(editor.IsSharing);
        Assert.True(thumbnailReleased);
        Assert.True(keptOnTheDoneStep);
    }

    [Fact]
    public async Task NoPictures_SendsNoneAndSaysNothingAboutThem()
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);

        var editor = await PublishPicturesAsync(harness, 0);

        Assert.Empty(host.ImageUploads);
        Assert.Null(editor.SharePicturesResultText);
        Assert.False(editor.CanRetrySharePictures);
        Assert.False(editor.IsSharing);
    }

    [Fact]
    public async Task Cover_ShowsOnThePublishStep_AndGoesUpFirstOnlyAfterThePublish()
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var (service, encoder) = CoverParts();
        var editor = await ShareWithCoverAsync(harness, service, encoder);

        Assert.True(editor.ShowsShareCoverCard);
        Assert.True(editor.ShowsShareCoverPicture);
        Assert.False(editor.ShowsShareCoverDrawing);
        Assert.EndsWith("Example Rover", Assert.Single(service.FolderLoads));
        Assert.Equal((VehicleCoverPicture.Size, VehicleCoverPicture.Size, true), (encoder.Width, encoder.Height, encoder.IsOpaque));
        Assert.Null(editor.ShareSummaryPicturesText);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        Assert.Empty(host.ImageUploads);
        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        Assert.Equal(new ImageUpload("vehicle", "ExampleRover", CoverEncoder.Png.Length), Assert.Single(host.ImageUploads));
        var row = Assert.Single(editor.SharePictures);
        Assert.Equal((harness.Localization.ShareCoverRowName, true, true), (row.Name, row.IsCover, row.IsSent));
        Assert.False(editor.ShowsShareCoverCard);
    }

    [Fact]
    public async Task Cover_LeftOut_SendsNoPicture_AndTheStepCanTakeItBack()
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var (service, encoder) = CoverParts();
        var editor = await ShareWithCoverAsync(harness, service, encoder);

        editor.LeaveOutShareCoverCommand.Execute(null);
        Assert.Equal((false, true, true), (editor.ShowsShareCoverPicture, editor.ShowsShareCoverLeftOut, editor.ShowsShareCoverCard));
        editor.UseShareCoverCommand.Execute(null);
        Assert.Equal((true, false), (editor.ShowsShareCoverPicture, editor.ShowsShareCoverLeftOut));
        editor.LeaveOutShareCoverCommand.Execute(null);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        Assert.Empty(host.ImageUploads);
        Assert.Empty(editor.SharePictures);
    }

    [Fact]
    public async Task Cover_LeftOutWhileThePublishRuns_StaysOut()
    {
        ListingEditor? editor = null;
        var publishing = false;
        var host = new FakeHost
        {
            DuringSend = () =>
            {
                if (publishing)
                    editor!.LeaveOutShareCoverCommand.Execute(null);
                return Task.CompletedTask;
            },
        };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var (service, encoder) = CoverParts();
        editor = await ShareWithCoverAsync(harness, service, encoder);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        publishing = true;
        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        Assert.Single(host.Publishes);
        Assert.Empty(host.ImageUploads);
    }

    [Fact]
    public async Task OwnPicture_WinsOverTheCover_AndRemovingItOffersTheCoverAgain()
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var (service, encoder) = CoverParts();
        var editor = await ShareWithCoverAsync(harness, service, encoder);
        harness.ViewModel.WindowServices = new PictureWindow { File = new PickedBinaryFile("shot.png", Picture(1)) };

        await editor.AddSharePictureCommand.ExecuteAsync(null);
        Assert.False(editor.ShowsShareCoverCard);

        editor.BackToShareDescribeCommand.Execute(null);
        editor.ContinueShareCommand.Execute(null);
        await editor.ShareCoverDraw;
        Assert.Single(service.FolderLoads);
        Assert.False(editor.ShowsShareCoverCard);

        editor.BackToShareDescribeCommand.Execute(null);
        editor.SharePictures[0].RemoveCommand.Execute(null);
        editor.ContinueShareCommand.Execute(null);
        await editor.ShareCoverDraw;
        Assert.True(editor.ShowsShareCoverPicture);

        await editor.AddSharePictureCommand.ExecuteAsync(null);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        Assert.Equal(new ImageUpload("vehicle", "ExampleRover", Picture(1).Length), Assert.Single(host.ImageUploads));
        Assert.Equal("shot.png", Assert.Single(editor.SharePictures).Name);
    }

    [Fact]
    public async Task Cover_PublishWaitsForTheDrawing_SoThePlayerSeesThePictureFirst()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var service = new FakeVehiclePreview { Vehicle = VehiclePreviewViewModelTests.TriangleVehicle(), Files = VehiclePreviewViewModelTests.TriangleFiles, FolderGate = gate.Task };
        harness.ViewModel.VehiclePreviewServiceOf = _ => service;
        harness.ViewModel.VehiclePreviewEncoder = new CoverEncoder();
        var editor = await ShareWithFieldsAsync(harness);
        Assert.True(editor.ShowsShareCoverDrawing);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        Assert.False(editor.CanPublishToShareHost);
        await editor.PublishToShareHostCommand.ExecuteAsync(null);
        Assert.Empty(host.Publishes);

        var raised = new List<string?>();
        editor.PropertyChanged += (_, change) => raised.Add(change.PropertyName);
        gate.TrySetResult();
        await editor.ShareCoverDraw;
        Assert.Contains(nameof(ListingEditor.CanPublishToShareHost), raised);
        Assert.True(editor.CanPublishToShareHost);

        await editor.PublishToShareHostCommand.ExecuteAsync(null);
        Assert.Single(host.ImageUploads);
    }

    [Fact]
    public async Task Cover_ADrawingThatTheStepStopped_DoesNotComeBackWhenItEnds()
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost());
        var (service, _) = CoverParts();
        harness.ViewModel.VehiclePreviewServiceOf = _ => service;
        harness.ViewModel.VehiclePreviewEncoder = new CoverEncoder { Reached = reached, Gate = gate.Task };
        var editor = await ShareWithFieldsAsync(harness);
        var draw = editor.ShareCoverDraw;

        // The stop comes after the last check of the token, so the drawing still gives a picture.
        await reached.Task;
        editor.BackToShareDescribeCommand.Execute(null);
        gate.TrySetResult();
        await draw;

        Assert.Null(editor.ShareCoverBytes);
    }

    [Fact]
    public async Task Cover_IsDrawnAgainAtACheck_OnlyWhenTheFilesOfTheVehicleChanged()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost());
        var (service, encoder) = CoverParts();
        var editor = await ShareWithCoverAsync(harness, service, encoder);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        Assert.Single(service.FolderLoads);

        var file = Directory.EnumerateFiles(service.FolderLoads[0], "*", SearchOption.AllDirectories).First();
        File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file).AddMinutes(5));
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await editor.ShareCoverDraw;

        Assert.Equal(2, service.FolderLoads.Count);
        Assert.True(editor.ShowsShareCoverPicture);
    }

    [Fact]
    public async Task Cover_StaysOutWhenThePublishEndsWhileThePickerIsOpen_AndThePickedPictureGoesFirst()
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var (service, encoder) = CoverParts();
        var editor = await ShareWithCoverAsync(harness, service, encoder);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.ViewModel.WindowServices = new PictureWindow { File = new PickedBinaryFile("shot.png", Picture(1)), Gate = answer.Task };

        var pick = editor.AddSharePictureCommand.ExecuteAsync(null);
        await editor.PublishToShareHostCommand.ExecuteAsync(null);
        Assert.Equal(ShareFlowStep.Done, editor.ShareFlowStep);
        Assert.Empty(host.ImageUploads);

        answer.TrySetResult();
        await pick;
        await editor.RetrySharePicturesCommand.ExecuteAsync(null);

        Assert.Equal(new ImageUpload("vehicle", "ExampleRover", Picture(1).Length), Assert.Single(host.ImageUploads));
        Assert.Equal("shot.png", Assert.Single(editor.SharePictures).Name);
    }

    [Fact]
    public async Task Cover_AFailedDrawing_LeavesTheStepUsable_AndSaysThatNoCoverComes()
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var service = new FakeVehiclePreview { FolderFailure = new OutOfMemoryException() };
        harness.ViewModel.VehiclePreviewServiceOf = _ => service;
        harness.ViewModel.VehiclePreviewEncoder = new CoverEncoder();
        var editor = await ShareWithFieldsAsync(harness);
        await Assert.ThrowsAsync<OutOfMemoryException>(() => editor.ShareCoverDraw);
        Assert.Equal((false, true), (editor.ShowsShareCoverDrawing, editor.ShowsShareCoverMissing));

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        Assert.Equal(ShareFlowStep.Done, editor.ShareFlowStep);
        Assert.Empty(host.ImageUploads);
    }

    [Fact]
    public async Task PictureError_OfTheDescribeStep_DoesNotFollowToThePublishStep()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost());
        var (service, encoder) = CoverParts();
        var editor = await ShareWithCoverAsync(harness, service, encoder);
        editor.BackToShareDescribeCommand.Execute(null);
        harness.ViewModel.WindowServices = new PictureWindow { File = new PickedBinaryFile("bad.png", [1, 2, 3]) };
        await editor.AddSharePictureCommand.ExecuteAsync(null);
        Assert.NotNull(editor.SharePictureError);

        editor.ContinueShareCommand.Execute(null);

        Assert.Null(editor.SharePictureError);
    }

    [Fact]
    public async Task Cover_IsNotOfferedWhenAPartIsABox()
    {
        var box = new ResolvedPlaceholder(System.Numerics.Matrix4x4.Identity, System.Numerics.Vector3.One, "Wing", PlaceholderReason.UnknownPart, null);
        foreach (var vehicle in new[] { VehiclePreviewViewModelTests.TriangleVehicle(box), new ResolvedVehicle([], [], [], [], [], 0, 0) })
        {
            var host = new FakeHost();
            using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
            var service = new FakeVehiclePreview { Vehicle = vehicle, Files = VehiclePreviewViewModelTests.TriangleFiles };
            var editor = await ShareWithCoverAsync(harness, service, new CoverEncoder());

            Assert.Single(service.FolderLoads);
            Assert.Equal((false, true), (editor.ShowsShareCoverPicture, editor.ShowsShareCoverMissing));

            await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
            await editor.PublishToShareHostCommand.ExecuteAsync(null);
            Assert.Empty(host.ImageUploads);
        }
    }

    [Fact]
    public async Task Cover_IsNotDrawnForASaveOrANewVersion()
    {
        var (forSave, encoder) = CoverParts();
        using (var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost()))
        {
            harness.ViewModel.VehiclePreviewServiceOf = _ => forSave;
            harness.ViewModel.VehiclePreviewEncoder = encoder;
            var instance = await GameSaveShareTests.CreateInstanceAsync(harness, "Example Rover");
            GameSaveShareTests.WriteItem(harness.Services.Paths.GetInstanceSavesFolder(instance), "Moon Base", "universe.xml", "<UniverseData><System><Id Id=\"Sol\" /></System></UniverseData>");
            await GameSaveShareTests.OpenAsync(harness);
            await Assert.Single(harness.ViewModel.SavesSection.Items).ShareCommand.ExecuteAsync(null);
            var editor = harness.ViewModel.ListingEditor;
            await editor.ShareHostsLoad;
            FillRequiredFields(editor);

            Assert.Equal(ShareFlowStep.Publish, editor.ShareFlowStep);
            Assert.Empty(forSave.FolderLoads);
            Assert.False(editor.ShowsShareCoverCard);
        }

        var (forVersion, versionEncoder) = CoverParts();
        using (var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: ListedRover), shareHostClient: new FakeHost { PublishedId = null }))
        {
            harness.ViewModel.VehiclePreviewServiceOf = _ => forVersion;
            harness.ViewModel.VehiclePreviewEncoder = versionEncoder;
            var editor = await ShareAsync(harness);
            editor.ShareAsNewVersionCommand.Execute(null);
            editor.ContinueShareCommand.Execute(null);

            Assert.Equal(ShareFlowStep.Publish, editor.ShareFlowStep);
            Assert.Empty(forVersion.FolderLoads);
            Assert.False(editor.ShowsShareCoverCard);
        }
    }

    [Fact]
    public async Task NewVersion_SendsPicturesToItsListing_WhichKeepsItsCover()
    {
        var host = new FakeHost { PublishedId = null };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: ListedRover), shareHostClient: host);
        var editor = await ShareAsync(harness);
        var window = new PictureWindow { File = new PickedBinaryFile("shot.png", Picture(1)) };
        harness.ViewModel.WindowServices = window;
        Assert.False(editor.ShowsSharePictures);

        editor.ShareAsNewVersionCommand.Execute(null);
        await editor.AddSharePictureCommand.ExecuteAsync(null);

        Assert.True(editor.ShowsSharePictures);
        Assert.False(Assert.Single(editor.SharePictures).IsCover);
        Assert.Equal(harness.Localization.FormatSharePicturesNewVersionHint(6, "10 MB"), editor.SharePicturesHint);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await editor.PublishToShareHostCommand.ExecuteAsync(null);
        Assert.Equal(new ImageUpload("vehicle", "ExampleRover", Picture(1).Length), Assert.Single(host.ImageUploads));
    }

    [Fact]
    public async Task AddPicture_TakesSixAndRefusesWhatTheHostWouldRefuse()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost());
        var localization = harness.Localization;
        var editor = await ShareAsync(harness);
        var window = new PictureWindow();
        harness.ViewModel.WindowServices = window;
        var still = ListingEditorTests.Png(16, 9);
        (byte[] Bytes, string Problem)[] refused =
        [
            (new byte[ShareImageLimits.MaxBytes + 1], localization.FormatSharePictureTooLarge("bad.png", "10 MB")),
            ([1, 2, 3], localization.FormatSharePictureNotImage("bad.png")),
            ([.. still[..33], 0, 0, 0, 8, .. "acTL"u8, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, .. still[33..]], localization.FormatSharePictureAnimated("bad.png")),
        ];
        foreach (var (bytes, problem) in refused)
        {
            window.File = new PickedBinaryFile("bad.png", bytes);
            await editor.AddSharePictureCommand.ExecuteAsync(null);
            Assert.Equal(problem, editor.SharePictureError);
            Assert.Empty(editor.SharePictures);
        }

        window.File = new PickedBinaryFile("shot.png", [.. still, .. new byte[ShareImageLimits.MaxBytes - still.Length]]);
        for (var count = 0; count < ShareImageLimits.MaxCount + 1; count++)
            await editor.AddSharePictureCommand.ExecuteAsync(null);

        Assert.Null(editor.SharePictureError);
        Assert.Equal(ShareImageLimits.MaxCount, editor.SharePictures.Count);
        Assert.False(editor.CanAddSharePicture);

        editor.SharePictures[0].RemoveCommand.Execute(null);

        Assert.True(editor.CanAddSharePicture);
        Assert.True(editor.SharePictures[0].IsCover);
    }

    [Fact]
    public async Task AddPicture_FileThatCannotBeRead_SaysSo()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost());
        var editor = await ShareAsync(harness);
        harness.ViewModel.WindowServices = new PictureWindow { Failure = new IOException("Locked.") };

        await editor.AddSharePictureCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.SharePictureUnreadable, editor.SharePictureError);
    }

    [Theory]
    [InlineData("invalid_request", "A listing has at most 6 screenshots.")]
    [InlineData("too_large", "Too large.")]
    public async Task RefusedPicture_KeepsThePublish_TheOthersGoOn_AndTryAgainSendsOnlyWhatIsMissing(string code, string message)
    {
        var host = new FakeHost();
        host.ImageFailures.Enqueue(new ShareHostException(ShareHostFailure.Host, message, code));
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);

        var editor = await PublishPicturesAsync(harness, 2);

        var expected = code == "too_large" ? harness.Localization.FormatSharePictureTooLargeOnHost("ksamods.gg") : harness.Localization.FormatListingShareHostSaid("ksamods.gg", message);
        Assert.Equal(ShareFlowStep.Done, editor.ShareFlowStep);
        Assert.Equal(harness.Localization.FormatShareDoneHeading("Example Rover"), editor.ShareDoneHeadingText);
        Assert.Single(await harness.Services.SharedContent.ListAsync());
        Assert.Equal(expected, editor.SharePictures[0].Error);
        Assert.True(editor.SharePictures[1].IsSent);
        Assert.Equal(harness.Localization.FormatSharePicturesMissing(1, 2), editor.SharePicturesResultText);
        Assert.True(editor.CanRetrySharePictures);

        await editor.RetrySharePicturesCommand.ExecuteAsync(null);

        Assert.Equal([Picture(1).Length, Picture(2).Length, Picture(1).Length], host.ImageUploads.Select(upload => upload.Size));
        Assert.Null(editor.SharePictures[0].Error);
        Assert.Equal(harness.Localization.SharePicturesSent, editor.SharePicturesResultText);
        Assert.Single(host.Publishes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PictureThatGetsNoAnswer_StopsTheRest_SaysThatItMayBeThere_AndAnEntryKeepsTheTry(bool serverError)
    {
        var host = new FakeHost();
        host.ImageFailures.Enqueue(serverError
            ? new ShareHostException(ShareHostFailure.Host, "Try later.", "busy") { IsServerError = true }
            : new ShareHostException(ShareHostFailure.NetworkError, "The share host could not be reached."));
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);

        var editor = await PublishPicturesAsync(harness, 2);

        var lost = harness.Localization.FormatSharePictureLostAnswer("ksamods.gg");
        Assert.Single(host.ImageUploads);
        Assert.Equal(serverError ? $"{lost}\nTry later." : lost, editor.SharePictures[0].Error);
        Assert.False(editor.SharePictures[1].IsSent);
        Assert.Equal(harness.Localization.FormatSharePicturesMissing(2, 2), editor.SharePicturesResultText);

        await harness.ViewModel.OpenShareFlowAsync(GameSaveKind.Vehicle);
        Assert.Equal((ShareFlowStep.Done, 2), (editor.ShareFlowStep, editor.SharePictures.Count));

        await editor.RetrySharePicturesCommand.ExecuteAsync(null);

        Assert.Equal([Picture(1).Length, Picture(1).Length, Picture(2).Length], host.ImageUploads.Select(upload => upload.Size));
        Assert.Equal(harness.Localization.SharePicturesSent, editor.SharePicturesResultText);
    }

    [Fact]
    public async Task HostWithoutThePictureRequest_OffersNoPictures_NowAndAtTheNextShare()
    {
        var host = new FakeHost();
        host.ImageFailures.Enqueue(new ShareHostException(ShareHostFailure.UnexpectedResponse, "ksamods.gg answered with 404.") { IsNotFound = true });
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);

        var editor = await PublishPicturesAsync(harness, 2);

        Assert.Single(host.ImageUploads);
        Assert.Empty(editor.SharePictures);
        Assert.Equal(ShareFlowStep.Done, editor.ShareFlowStep);
        Assert.Equal(harness.Localization.FormatSharePicturesNotOffered("ksamods.gg"), editor.SharePicturesResultText);
        Assert.False(editor.CanRetrySharePictures);

        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);
        await editor.ShareHostsLoad;
        await editor.ShareIsDifferentCommand.ExecuteAsync(null);

        Assert.False(editor.IsAskingShareListing);
        Assert.False(editor.ShowsSharePictures);

        FillRequiredFields(editor);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        Assert.Equal(ShareFlowStep.Done, editor.ShareFlowStep);
        Assert.Null(editor.SharePicturesResultText);
    }

    [Fact]
    public async Task PictureOfANotFoundListing_IsAFailure_AndTheHostStillOffersPictures()
    {
        var host = new FakeHost();
        host.ImageFailures.Enqueue(new ShareHostException(ShareHostFailure.Host, "No such listing.", "not_owner") { IsNotFound = true });
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);

        var editor = await PublishPicturesAsync(harness, 1);

        Assert.Equal(harness.Localization.FormatSharePictureNotOwner("ksamods.gg", "Example Rover"), Assert.Single(editor.SharePictures).Error);
        Assert.True(editor.CanRetrySharePictures);
        Assert.True(editor.ShowsSharePictures);
    }

    [Fact]
    public async Task PictureAfterTheSignInEnded_SignsInAgainAndGoesOn()
    {
        var host = new FakeHost();
        host.ImageFailures.Enqueue(new ShareHostException(ShareHostFailure.SignedOut, "ksamods.gg no longer takes the sign-in."));
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);

        var editor = await PublishPicturesAsync(harness, 1);

        Assert.Equal(2, host.SignIns);
        Assert.True(Assert.Single(editor.SharePictures).IsSent);
    }

    [Fact]
    public async Task WhilePicturesGoUp_AnEntryKeepsThem_AndCancelLeavesThemForAnotherTry()
    {
        var host = new FakeHost { ImageGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await SharePicturesAsync(harness, 2);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        var publish = editor.PublishToShareHostCommand.ExecuteAsync(null);
        await host.ImageStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(editor.IsSendingSharePictures);
        Assert.Equal(harness.Localization.FormatSharePicturesSending(1, 2), editor.ShareHostProgress);
        Assert.True(editor.CanCancelShareHost);
        Assert.False(editor.CanRetrySharePictures);
        await harness.ViewModel.OpenShareFlowAsync(GameSaveKind.Vehicle);
        Assert.Equal((ShareFlowStep.Done, 2), (editor.ShareFlowStep, editor.SharePictures.Count));

        editor.CancelShareHostCommand.Execute(null);
        await publish;

        Assert.Single(host.ImageUploads);
        Assert.False(editor.IsSharing);
        Assert.Equal(ShareFlowStep.Done, editor.ShareFlowStep);
        Assert.Equal(harness.Localization.FormatSharePicturesMissing(2, 2), editor.SharePicturesResultText);
        Assert.Equal(harness.Localization.FormatSharePictureLostAnswer("ksamods.gg"), editor.SharePictures[0].Error);
        Assert.True(editor.CanRetrySharePictures);

        host.ImageGate = null;
        await editor.RetrySharePicturesCommand.ExecuteAsync(null);

        Assert.Null(editor.SharePictures[0].Error);
        Assert.Equal(harness.Localization.SharePicturesSent, editor.SharePicturesResultText);
    }

    [Fact]
    public async Task ShareAnother_ForgetsThePictures()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost());
        var editor = await PublishPicturesAsync(harness, 1);

        editor.ShareAnotherCommand.Execute(null);
        await editor.ShareChooseLoad;

        Assert.Empty(editor.SharePictures);
        Assert.Null(editor.SharePicturesResultText);
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
        Assert.Equal(ShareFlowStep.Describe, editor.ShareFlowStep);
        Assert.Null(editor.ShareDoneHeadingText);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DoneStep_SaysWhenDiscoverShowsIt(bool isNewVersion, bool hasRequest)
    {
        var host = new FakeHost { PullRequest = hasRequest ? new Uri("https://github.com/KSAModding/content-index/pull/250") : null };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: isNewVersion ? ListedRover : null), shareHostClient: host);
        var editor = await ShareAsync(harness);

        if (isNewVersion)
            editor.ShareAsNewVersionCommand.Execute(null);

        FillRequiredFields(editor);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        var text = harness.Localization;
        Assert.Equal(text.FormatShareDoneHeading("Example Rover"), editor.ShareDoneHeadingText);
        var expected = isNewVersion ? text.ShareDoneNewVersion : hasRequest ? text.ShareDoneNewListing : text.FormatShareDoneNewListingLater("ksamods.gg");
        Assert.Equal(expected, editor.ShareDoneDiscoverText);
    }

    [Theory]
    [InlineData("https://ksamods.gg/vehicles/ExampleRover", "https://ksamods.gg/vehicles/ExampleRover", true)]
    [InlineData(null, "https://ksamods.gg/", false)]
    public async Task DoneStep_LinksThePageInTheAnswer_ElseTheSiteOfTheHost(string? page, string link, bool isPage)
    {
        var host = new FakeHost { Page = page is null ? null : new Uri(page) };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await PublishAsync(harness);
        var opened = new List<string>();
        harness.ViewModel.OpenWithSystem = opened.Add;
        var window = new UnexpectedErrorTests.FakeWindowServices();
        harness.ViewModel.WindowServices = window;

        editor.OpenShareDonePageCommand.Execute(null);
        await editor.CopyShareDoneLinkCommand.ExecuteAsync(null);

        Assert.Equal(isPage ? harness.Localization.ShareOpenPage : harness.Localization.FormatShareOpenHost("ksamods.gg"), editor.ShareDoneOpenText);
        Assert.Equal(link, Assert.Single(opened));
        Assert.Equal("https://ksamodding.github.io/Borea/vehicle/ExampleRover/", window.CopiedText);
    }

    [Fact]
    public async Task DoneStep_IdThatCannotBeAPage_CopiesTheLinkOfTheHost()
    {
        var host = new FakeHost { Page = new Uri("https://ksamods.gg/vehicles/ExampleRover"), PublishedId = "../ExampleRover" };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await PublishAsync(harness);

        Assert.Equal("https://ksamods.gg/vehicles/ExampleRover", editor.ShareDoneCopyLink);
    }

    [Fact]
    public async Task DoneStep_KeepsTheLink_WhenTheIndexComesBackAtTheNextOpen()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost(), indexOffline: true);
        var editor = await ShareAsync(harness);

        // Only the read of the page fails, so the hosts arrive while the page still has no index.
        var reads = 0;
        harness.IndexRequest = () =>
        {
            harness.IndexOffline = reads++ == 0;
            return Task.CompletedTask;
        };
        await editor.RetryShareHostsCommand.ExecuteAsync(null);
        FillRequiredFields(editor);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        await harness.ViewModel.OpenListingAsync();

        Assert.Equal(ShareFlowStep.Done, editor.ShareFlowStep);
        Assert.Equal(harness.Localization.FormatShareDoneHeading("Example Rover"), editor.ShareDoneHeadingText);
        Assert.NotNull(editor.ShareDoneLink);
        Assert.NotNull(editor.SharePullRequest);
    }

    [Fact]
    public async Task DoneStep_PageThatDoesNotOpen_SaysWhyUntilTheNextOneOpens()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost());
        var editor = await PublishAsync(harness);

        harness.ViewModel.OpenWithSystem = _ => throw new Win32Exception("No browser.");
        editor.OpenShareDonePageCommand.Execute(null);
        Assert.Equal(harness.Localization.FormatListingShareHostOpenFailed("No browser."), editor.ShareHostError);

        var opened = new List<string>();
        harness.ViewModel.OpenWithSystem = opened.Add;
        editor.OpenSharePullRequestCommand.Execute(null);
        Assert.Null(editor.ShareHostError);
        Assert.Equal("https://github.com/KSAModding/content-index/pull/250", Assert.Single(opened));
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
        var taken = Listed("TakenRover", "Taken Rover", "Taken Rover", "\"github\": \"someone/rover\"");
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
        Assert.Equal(harness.Localization.FormatShareFieldsToFix(harness.Localization.LinkHomepage), editor.ShareOtherFieldsText);
        Assert.True(editor.IsShareMoreOptionsOpen);
        Assert.Equal(editor.ShareOtherFieldsText, editor.ShareHostError);
        Assert.Equal((0, 0), (host.SignIns, host.Uploads.Count));

        editor.EditAllShareFieldsCommand.Execute(null);
        Assert.True(editor.IsFormStep);
        editor.Homepage = "https://example.test/rover";
        editor.BackToShareStepsCommand.Execute(null);

        Assert.True(editor.IsShareStep);
        Assert.Equal(("https://example.test/rover", "Maxi"), (editor.Homepage, editor.Authors));
        Assert.True(editor.CanContinueShare);
    }

    [Fact]
    public async Task ErrorWithNoLabel_NamesItsSection_AndNeverItsPath()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost());
        var editor = await ShareAsync(harness);

        editor.ShowShareOtherFields([new ListingIssue(ListingIssueSeverity.Error, "provides.launch", "Is odd.")]);
        Assert.Equal(harness.Localization.FormatShareFieldsToFix(harness.Localization.ListingAbout), editor.ShareOtherFieldsText);

        // A share has no status section, so this issue has neither a label nor a section to name.
        editor.ShowShareOtherFields([new ListingIssue(ListingIssueSeverity.Error, "status.reason", "Is odd.")]);
        Assert.Equal(harness.Localization.ShareOtherFields, editor.ShareOtherFieldsText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DependencyWithNoId_StopsNextAndTheUpload(bool isNewVersion)
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: isNewVersion ? ListedRover : null), shareHostClient: host);
        var editor = await ShareAsync(harness);
        editor.ShareAsNewVersionCommand.Execute(null);
        editor.Authors = "Maxi";
        editor.License = "CC-BY-4.0";
        editor.Abstract = "A six-wheel rover for the Moon.";
        Assert.True(editor.CanContinueShare);

        editor.AddDependencyCommand.Execute(null);

        Assert.True(editor.HasShareOtherFields);
        Assert.False(editor.CanContinueShare);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        Assert.Equal(harness.Localization.FormatShareFieldsToFix(harness.Localization.FormatListingDependencyNumber(2)), editor.ShareHostError);
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

        Assert.Equal(harness.Localization.FormatShareFieldsToFix(harness.Localization.LinkHomepage), editor.ShareHostError);
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
        Assert.Empty(editor.ShareHostPassedChecks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GameStartedOutsideBorea_StopsTheFileBeforeItIsWritten(bool saveToZip)
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await ShareWithFieldsAsync(harness);
        var window = new GameSaveShareTests.ArchiveWindow();
        harness.ViewModel.WindowServices = window;
        harness.IsGameProcessRunning = () => true;

        if (saveToZip)
            await editor.SaveShareArchiveCommand.ExecuteAsync(null);
        else
            await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        Assert.True(editor.ShowsShareGameRunning);
        Assert.Null(editor.ShareHostError);
        Assert.Empty(harness.ViewModel.Toasts.Items);
        Assert.Empty(host.Uploads);
        Assert.Null(window.SuggestedFileName);
        Assert.True(editor.ShowsShareCheckButton);

        // The game gives no signal when it ends outside Borea, so the same action goes on from the step.
        harness.IsGameProcessRunning = () => false;
        if (saveToZip)
            await editor.SaveShareArchiveCommand.ExecuteAsync(null);
        else
            await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        Assert.False(editor.ShowsShareGameRunning);
        Assert.Null(editor.ShareHostError);
        Assert.Equal(saveToZip ? 0 : 1, host.Uploads.Count);
        Assert.Equal(saveToZip, window.SuggestedFileName is not null);
    }

    [Fact]
    public async Task Publish_DropsTheLineAboutTheGame()
    {
        var host = new FakeHost { PublishFailure = new ShareHostException(ShareHostFailure.Host, "Refused.", "too_large") };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await ShareWithFieldsAsync(harness);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        harness.IsGameProcessRunning = () => true;
        await editor.SaveShareArchiveCommand.ExecuteAsync(null);
        Assert.True(editor.ShowsShareGameRunning);

        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        Assert.False(editor.ShowsShareGameRunning);
    }

    [Theory]
    [InlineData("id_taken")]
    [InlineData("version_taken")]
    public async Task ZipWhileTheGameRuns_KeepsTheRefusalThatStands(string code)
    {
        var host = new FakeHost { PublishFailure = new ShareHostException(ShareHostFailure.Host, "Refused.", code) };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await ShareWithFieldsAsync(harness);
        harness.ViewModel.WindowServices = new GameSaveShareTests.ArchiveWindow();
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await editor.PublishToShareHostCommand.ExecuteAsync(null);
        var refusal = (editor.ShareHostError, editor.ShareErrorActionText);
        Assert.NotNull(refusal.ShareHostError);

        harness.IsGameProcessRunning = () => true;
        await editor.SaveShareArchiveCommand.ExecuteAsync(null);

        Assert.True(editor.ShowsShareGameRunning);
        Assert.Equal(refusal, (editor.ShareHostError, editor.ShareErrorActionText));
        Assert.False(editor.ShowsShareCheckButton);
        Assert.False(editor.CanPublishToShareHost);

        harness.IsGameProcessRunning = () => false;
        await editor.SaveShareArchiveCommand.ExecuteAsync(null);

        Assert.False(editor.ShowsShareGameRunning);
        Assert.Equal(refusal, (editor.ShareHostError, editor.ShareErrorActionText));
        Assert.False(editor.CanPublishToShareHost);
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
        editor.OpenShareStep("done");
        Assert.True(editor.HasShareUpload);

        if (toAllFields)
            editor.EditAllShareFieldsCommand.Execute(null);
        else
            editor.OpenShareStep("describe");

        Assert.True(toAllFields ? editor.IsFormStep : editor.ShareFlowStep == ShareFlowStep.Describe);
        Assert.False(editor.HasShareUpload);
        Assert.Empty(editor.ShareHostPassedChecks);
        Assert.Equal("Maxi", editor.Authors);
    }

    [Fact]
    public async Task ChooseInTheSidePanel_DropsTheUpload_AndKeepsTheDraft()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost());
        var editor = await ShareWithFieldsAsync(harness);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        editor.OpenShareStep("choose");
        await editor.ShareChooseLoad;

        Assert.Equal(ShareFlowStep.Choose, editor.ShareFlowStep);
        Assert.False(editor.HasShareUpload);
        Assert.True(editor.HasShare);
        Assert.Equal("Example Rover", editor.SelectedShareRow?.Item.Name);
    }

    [Fact]
    public async Task ShareAnother_ForgetsTheShare_AndOpensTheList()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost());
        var editor = await PublishAsync(harness);

        editor.ShareAnotherCommand.Execute(null);
        await editor.ShareChooseLoad;

        Assert.Equal(ShareFlowStep.Choose, editor.ShareFlowStep);
        Assert.False(editor.HasShare);
        Assert.Null(editor.ShareDoneHeadingText);
        Assert.Equal([ListingStepState.Current, ListingStepState.ToDo, ListingStepState.ToDo, ListingStepState.ToDo], editor.ShareSteps.Select(step => step.State));
        Assert.True(editor.IsShareChooseVehicle);
        Assert.Single(editor.ShareChooseRows);
    }

    [Fact]
    public async Task EntryDuringARun_OnlyGoesBackToIt()
    {
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost
        {
            DuringSend = () =>
            {
                arrived.TrySetResult();
                return answer.Task;
            },
        };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await ShareWithFieldsAsync(harness);
        var viewModel = harness.ViewModel;
        var upload = editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(30));
        viewModel.SetMainWindowDiscoverCommand.Execute(null);

        await Assert.Single(viewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowListing);
        Assert.True(editor.IsSharing);
        viewModel.OpenDiscoverTab(ContentType.Save);
        await viewModel.ShareFromDiscoverCommand.ExecuteAsync(null);
        Assert.True(viewModel.CurrentWindowListing);
        Assert.True(editor.IsSharing);
        answer.SetResult();
        await upload;
        Assert.True(editor.HasShareUpload);
        Assert.Equal(ShareFlowStep.Publish, editor.ShareFlowStep);
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

        Assert.True(editor.IsAskingShareListing);
        Assert.Equal(harness.Localization.FormatShareLooksLike("Example Rover", "ksamods.gg"), editor.ShareLooksLikeText);
        Assert.Equal(harness.Localization.FormatShareVersionOnline("1.0.0"), editor.ShareVersionOnlineText);
        Assert.Equal(harness.Localization.FormatShareAsNewVersion("Example Rover"), editor.ShareAsNewVersionText);
        Assert.Null(editor.ShareIdError);
        Assert.False(editor.CanContinueShare);
        Assert.True(editor.ShowsPreviewCard);
        Assert.Contains(editor.Errors, issue => issue.Location == "id");

        editor.ShareAsNewVersionCommand.Execute(null);

        Assert.False(editor.IsAskingShareListing);
        Assert.True(editor.IsNewVersionShare);
        Assert.Equal(harness.Localization.FormatShareNewVersionLine("Example Rover", "ksamods.gg"), editor.ShareNewVersionText);
        Assert.DoesNotContain(editor.Errors, issue => issue.Location == "id");
        Assert.Null(editor.ShareIdError);
        Assert.Null(editor.ShareNextReason);
        Assert.False(editor.HasShareOtherFields);
        Assert.True(editor.ShowsPreviewCard);
        Assert.Equal("Example Rover", editor.PreviewName);
        Assert.Equal(harness.Localization.FormatContentByAuthor("Maxi"), editor.PreviewAuthors);
        Assert.Equal("A small rover.", editor.PreviewAbstract);
        Assert.True(editor.CanContinueShare);

        // The full form can open a pull request for a new file, so there the id counts as held.
        editor.EditAllShareFieldsCommand.Execute(null);
        editor.Authors = "Maxi";
        editor.License = "CC-BY-4.0";
        editor.Abstract = "A six-wheel rover for the Moon.";
        editor.Name = "Typed Rover";
        Assert.Contains(editor.Errors, issue => issue.Location == "id");
        Assert.False(editor.CanOpenPullRequest);
        Assert.Equal("Typed Rover", editor.PreviewName);
        Assert.Equal("A six-wheel rover for the Moon.", editor.PreviewAbstract);

        editor.Authors = "Someone Else";
        Assert.Equal(harness.Localization.FormatContentByAuthor("Someone Else"), editor.PreviewAuthors);

        editor.BackToShareStepsCommand.Execute(null);
        Assert.DoesNotContain(editor.Errors, issue => issue.Location == "id");
        Assert.True(editor.CanContinueShare);
        Assert.Equal("Example Rover", editor.PreviewName);
        Assert.Equal(harness.Localization.FormatContentByAuthor("Maxi"), editor.PreviewAuthors);
        Assert.Equal("A small rover.", editor.PreviewAbstract);
    }

    [Theory]
    [InlineData("ExampleRover", "ExampleRover")]
    [InlineData("MyRover", null)]
    public async Task IdOnTheFullForm_DecidesWhetherTheShareStaysANewVersion(string id, string? listingId)
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: ListedRover), shareHostClient: host);
        var editor = await ShareAsync(harness);
        editor.ShareAsNewVersionCommand.Execute(null);

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
        var editor = await ShareWithFieldsAsync(harness);
        Assert.Equal(ShareFlowStep.Publish, editor.ShareFlowStep);
        Assert.False(editor.IsAskingShareListing);
        Assert.Empty(editor.ShareHostRows);

        harness.IndexOffline = false;
        await harness.ViewModel.OpenListingAsync();
        await editor.ShareHostsLoad;

        Assert.Equal(ShareFlowStep.Describe, editor.ShareFlowStep);
        Assert.True(editor.IsAskingShareListing);
        editor.ShareAsNewVersionCommand.Execute(null);
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
        editor.ShareAsNewVersionCommand.Execute(null);

        Assert.Equal("ksamods", Assert.Single(editor.ShareHostRows).Host.Key);
    }

    [Fact]
    public async Task HostError_ShowsItsMessage()
    {
        var host = new FakeHost { UploadFailure = new ShareHostException(ShareHostFailure.Host, "ExampleRover belongs to another account.", "some_new_code") };
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
    public async Task OneHost_ShowsItsLimitAndRules_AndThePublicNoticeNamesIt()
    {
        const string hosts = """
            { "ksamods": {
                "kind": "share", "name": "ksamods.gg", "api": "https://ksamods.gg/api/share/v1/", "policy": "https://ksamods.gg/rules",
                "types": ["vehicle"], "max_bytes": { "vehicle": 10485760 }
            } }
            """;
        var opened = new ConcurrentQueue<string>();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(hosts), respond: Serve(), shareHostClient: new FakeHost());
        harness.ViewModel.OpenWithSystem = opened.Enqueue;
        var text = harness.Localization;
        var editor = await ShareAsync(harness);

        var row = Assert.Single(editor.ShareHostRows);
        Assert.False(editor.HasSeveralShareHosts);
        Assert.Same(row, editor.SelectedShareHostRow);
        Assert.Equal(text.FormatShareHostOne("ksamods.gg"), editor.ShareHostHeadingText);
        Assert.Equal(text.FormatShareHostLimitVehicle("10 MB"), row.LimitText);
        Assert.Equal(text.FormatShareHostRules("ksamods.gg"), row.RulesText);
        Assert.Null(editor.SharePublicNoticeText);

        editor.OpenShareHostRulesCommand.Execute(row.Host);
        await editor.UploadToShareHostCommand.ExecuteAsync(row);

        Assert.Equal(["https://ksamods.gg/rules"], opened);
        Assert.Equal(text.FormatSharePublicNotice("ksamods.gg"), editor.SharePublicNoticeText);
        Assert.Equal(text.FormatShareHostRules("ksamods.gg"), editor.ShareUploadRulesText);
    }

    [Fact]
    public async Task SeveralHosts_AreAChoiceWithTheFirstSelected_AndTheCheckNamesTheChosenOne()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(TwoHosts), respond: Serve(), shareHostClient: new FakeHost());
        var text = harness.Localization;
        var editor = await ShareAsync(harness);

        Assert.True(editor.HasSeveralShareHosts);
        Assert.Equal(text.ShareHostChoiceHeading, editor.ShareHostHeadingText);
        Assert.Same(editor.ShareHostRows[0], editor.SelectedShareHostRow);
        Assert.Null(editor.ShareHostRows[0].LimitText);

        var chosen = editor.ShareHostRows[1];
        editor.SelectedShareHostRow = chosen;
        Assert.Equal(text.FormatShareCheckOnHost(chosen.Host.Name), editor.ShareCheckOnHostText);
        await editor.UploadToShareHostCommand.ExecuteAsync(editor.SelectedShareHostRow);

        Assert.Equal(text.FormatShareChecksPassed(2, chosen.Host.Name), editor.ShareChecksPassedText);
    }

    [Fact]
    public async Task Summary_NamesTheLicenseInWordsTheModsAndTheBuild()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost());
        var text = harness.Localization;
        var editor = await ShareAsync(harness);
        editor.License = "CC-BY-4.0";

        Assert.Equal(text.ShareLicenseCcBy, editor.ShareLicenseText);
        Assert.Equal(text.FormatShareSummaryMods("ExamplePartsMod"), editor.ShareSummaryModsText);
        Assert.Equal(text.FormatShareNeedsBuild("2026.10.19.5638"), editor.ShareNeedsBuildText);

        Assert.Single(editor.Dependencies).RemoveCommand.Execute(null);

        Assert.Equal(text.ShareSummaryNoMods, editor.ShareSummaryModsText);
    }

    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("1.1.0-beta.1", true)]
    [InlineData("1.0", false)]
    [InlineData("", false)]
    public async Task Publish_NeedsThreeNumbers_AndAProposalThatHasNoneOpensTheField(string version, bool canPublish)
    {
        var host = new FakeHost { Upload = Checked with { ProposedVersion = version } };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: ListedRover), shareHostClient: host);
        var text = harness.Localization;
        var editor = await ShareAsync(harness);
        editor.ShareAsNewVersionCommand.Execute(null);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        Assert.Equal(!canPublish, editor.IsEditingShareVersion);
        Assert.Equal(canPublish, editor.CanPublishToShareHost);
        Assert.Equal(canPublish ? null : text.ShareVersionInvalid, editor.ShareVersionError);
        Assert.Equal(canPublish ? text.FormatSharePublishVersion(version) : text.ListingShareHostPublish, editor.SharePublishText);

        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        Assert.Equal(canPublish ? 1 : 0, host.Publishes.Count);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(9)]
    public async Task ChecksThatPass_SayItInOneLine_WithTheCountOfTheAnswerAndTheMessagesInTheDetails(int count)
    {
        var checks = Enumerable.Range(1, count).Select(number => new ShareCheck($"check {number}", true, $"Message {number}.")).ToList();
        var host = new FakeHost { Upload = Checked with { Checks = checks } };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var text = harness.Localization;
        var editor = await ShareAsync(harness);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        Assert.Equal(text.FormatShareChecksPassed(count, "ksamods.gg"), editor.ShareChecksPassedText);
        Assert.False(editor.IsShareDetailsOpen);
        Assert.True(editor.HasShareDetails);
        Assert.Equal(text.FormatShareCheckDetail("check 1", "Message 1."), editor.ShareHostPassedChecks[0]);
        Assert.Empty(editor.ShareHostFailedChecks);
        Assert.False(editor.ShowsShareRename);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://ksamods.gg/device?user_code=ROVR-1234")]
    public async Task SignIn_OpensACompletePageOnce_AndOtherwiseLeavesTheCodeToThePlayer(string? complete)
    {
        var opened = new ConcurrentQueue<string>();
        var signIn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost { Code = new GitHubDeviceCode("ROVR-1234", "https://ksamods.gg/device", complete), DuringSignIn = () => signIn.Task };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        harness.ViewModel.OpenWithSystem = opened.Enqueue;
        var text = harness.Localization;
        var editor = await ShareAsync(harness);
        var codeShown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        editor.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ListingEditor.ShareHostProgressText) && editor.ShareHostUserCode is not null)
                codeShown.TrySetResult();
        };

        var upload = editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await codeShown.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(complete is null ? [] : [complete], opened);
        Assert.Equal(complete is not null, editor.HasShareCodePage);
        Assert.Equal(complete is null ? text.FormatListingShareHostCode("https://ksamods.gg/device") : text.FormatShareSignIn("ksamods.gg"), editor.ShareHostCodeText);
        Assert.Equal(text.ShareWaitingForApproval, editor.ShareHostProgressText);
        Assert.Equal(text.FormatShareCodeWarning("ksamods.gg"), editor.ShareCodeWarningText);

        signIn.SetResult();
        await upload;

        Assert.Equal(complete is null ? [] : [complete], opened);
        Assert.Null(editor.ShareHostUserCode);
    }

    [Fact]
    public async Task PageThatCannotOpen_SaysSoWithoutNamingGitHub()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost());
        harness.ViewModel.OpenWithSystem = _ => throw new InvalidOperationException("No browser.");
        var editor = await ShareAsync(harness);
        var row = Assert.Single(editor.ShareHostRows);

        editor.OpenShareHostRulesCommand.Execute(row.Host with { Policy = new Uri("https://ksamods.gg/rules") });

        Assert.Equal(harness.Localization.FormatListingShareHostOpenFailed("No browser."), editor.ShareHostError);
    }

    [Theory]
    [InlineData("ExampleRover", "ExampleRover2")]
    [InlineData("MyRover", "MyRover")]
    public async Task RenameThatAFailedCheckOffers_ChecksAgainWithTheSameHost_AndARefusedNameContinuesNothing(string id, string idAfter)
    {
        var host = new FakeHost { Upload = new ShareUpload("u42", [new ShareCheck("folder", false, "Taken by OtherRover.")], "1.0.0", null) };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var text = harness.Localization;
        var viewModel = harness.ViewModel;
        var editor = await ShareAsync(harness);
        editor.Authors = "Maxi";
        editor.License = "CC-BY-4.0";
        editor.Abstract = "A six-wheel rover for the Moon.";
        editor.Id = id;
        editor.ContinueShareCommand.Execute(null);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        editor.RenameForShareCommand.Execute(null);

        Assert.Equal(text.ShareFolderCheckVehicle, viewModel.GameSaveRenameReason);
        Assert.Equal(text.GameSaveRenameContinue, viewModel.NameModalConfirmText);
        Assert.Equal("Example Rover", viewModel.ModalInstanceName);
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);
        Assert.Equal(text.GameSaveRenameChooseAnother, viewModel.InstanceError);
        Assert.Single(host.Uploads);

        viewModel.ModalInstanceName = "Example  Rover";
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);
        Assert.NotEqual(text.GameSaveRenameChooseAnother, viewModel.InstanceError);
        Assert.NotNull(viewModel.InstanceError);
        Assert.Single(host.Uploads);

        host.Upload = Checked;
        viewModel.ModalInstanceName = "Example Rover 2";
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);
        await editor.ShareCheckAfterRename;

        Assert.Null(viewModel.RenamingGameSave);
        Assert.Equal(["Example Rover.zip", "Example Rover 2.zip"], host.Uploads.Select(upload => upload.FileName));
        Assert.Equal(("Example Rover 2", idAfter), (editor.Name, editor.Id));
        Assert.Equal(ShareFlowStep.Publish, editor.ShareFlowStep);
        Assert.True(editor.HasShareUpload);
        Assert.False(editor.ShowsShareRename);
    }

    [Fact]
    public async Task RenameOfANewVersion_KeepsItsListing_AndACancelContinuesNothing()
    {
        var host = new FakeHost { Upload = new ShareUpload("u42", [new ShareCheck("folder", false, "Taken by OtherRover.")], "1.0.0", null) };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: ListedRover), shareHostClient: host);
        var viewModel = harness.ViewModel;
        var editor = await ShareAsync(harness);
        editor.ShareAsNewVersionCommand.Execute(null);
        editor.ContinueShareCommand.Execute(null);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        editor.RenameForShareCommand.Execute(null);
        viewModel.CancelNameModalCommand.Execute(null);
        Assert.Single(host.Uploads);

        host.Upload = Checked;
        editor.RenameForShareCommand.Execute(null);
        viewModel.ModalInstanceName = "Example Rover 2";
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);
        await editor.ShareCheckAfterRename;

        Assert.Equal("ExampleRover", editor.Id);
        Assert.True(editor.IsNewVersionShare);
        Assert.Equal(("Example Rover 2.zip", "ExampleRover"), (host.Uploads.Last().FileName, host.Uploads.Last().ListingId));
    }

    [Fact]
    public async Task RenameThatEndsAfterThePlayerWentBack_TakesTheNameAndChecksNothing()
    {
        var host = new FakeHost { Upload = new ShareUpload("u42", [new ShareCheck("folder", false, "Taken by OtherRover.")], "1.0.0", null) };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: ListedRover), shareHostClient: host);
        var viewModel = harness.ViewModel;
        var editor = await ShareAsync(harness);
        editor.ShareAsNewVersionCommand.Execute(null);
        editor.ContinueShareCommand.Execute(null);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        editor.RenameForShareCommand.Execute(null);
        viewModel.ModalInstanceName = "Example Rover 2";
        ClickWhenTheModalCloses(viewModel, () => editor.BackToShareDescribeCommand.Execute(null));
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);
        await editor.ShareCheckAfterRename;

        Assert.Equal(ShareFlowStep.Describe, editor.ShareFlowStep);
        Assert.Equal("Example Rover 2", editor.Name);
        Assert.Single(host.Uploads);

        host.Upload = Checked;
        editor.ContinueShareCommand.Execute(null);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        Assert.Equal("Example Rover 2.zip", host.Uploads.Last().FileName);
    }

    [Fact]
    public async Task RenameThatEndsAfterAnotherShareStarted_LeavesThatShareAlone()
    {
        var host = new FakeHost { Upload = new ShareUpload("u42", [new ShareCheck("folder", false, "Taken by OtherRover.")], "1.0.0", null) };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: ListedRover), shareHostClient: host);
        var viewModel = harness.ViewModel;
        var editor = await ShareAsync(harness);
        var row = Assert.Single(viewModel.VehiclesSection.Items);
        var other = new GameSaveShareStart(row.InstanceId, row.Entry, new GameFolderContent("Other Rover", null, new ContentIds(), [], []), new ContentShare([], []), null);
        editor.ShareAsNewVersionCommand.Execute(null);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        editor.RenameForShareCommand.Execute(null);
        viewModel.ModalInstanceName = "Example Rover 2";
        ClickWhenTheModalCloses(viewModel, () => editor.StartShare(other));
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);
        await editor.ShareCheckAfterRename;

        Assert.Equal("Other Rover", editor.Name);
        Assert.Single(host.Uploads);
    }

    [Fact]
    public async Task UploadWithNoChecks_ClaimsNoChecks()
    {
        var host = new FakeHost { Upload = Checked with { Checks = [] } };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await ShareAsync(harness);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        Assert.True(editor.HasShareUpload);
        Assert.Null(editor.ShareChecksPassedText);
    }

    [Fact]
    public async Task ChannelOtherThanStable_ShowsAtTheNextCheck_AndTheNextShareStartsOnStable()
    {
        var host = new FakeHost { PullRequest = null };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: ListedRover), shareHostClient: host);
        var editor = await ShareAsync(harness);
        editor.ShareAsNewVersionCommand.Execute(null);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        Assert.False(editor.IsEditingShareVersion);
        editor.SelectedShareStatus = editor.ShareStatusChoices[1];

        editor.BackToShareDescribeCommand.Execute(null);
        editor.ContinueShareCommand.Execute(null);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        Assert.True(editor.IsEditingShareVersion);
        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);
        await editor.ShareHostsLoad;
        editor.ShareAsNewVersionCommand.Execute(null);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        Assert.False(editor.IsEditingShareVersion);
        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        Assert.Equal(["testing", "stable"], host.Publishes.Select(publish => publish.Status));
    }

    [Fact]
    public async Task SignInPageThatCannotOpen_NamesTheAddress_AndACancelTakesTheErrorAway()
    {
        var signIn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost { Code = new GitHubDeviceCode("ROVR-1234", "https://ksamods.gg/device", "https://ksamods.gg/device?user_code=ROVR-1234"), DuringSignIn = () => signIn.Task };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        harness.ViewModel.OpenWithSystem = _ => throw new InvalidOperationException("No browser.");
        var text = harness.Localization;
        var editor = await ShareAsync(harness);
        var codeShown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        editor.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ListingEditor.ShareHostProgressText) && editor.ShareHostUserCode is not null)
                codeShown.TrySetResult();
        };

        var upload = editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await codeShown.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.False(editor.HasShareCodePage);
        Assert.Equal(text.FormatListingShareHostCode("https://ksamods.gg/device"), editor.ShareHostCodeText);
        Assert.Equal(text.FormatListingShareHostOpenFailed("No browser."), editor.ShareHostError);

        editor.CancelShareHostCommand.Execute(null);
        signIn.SetResult();
        await upload;

        Assert.Null(editor.ShareHostError);
    }

    [Fact]
    public async Task RenameToATakenShortName_GoesBackToTheStepThatSaysIt()
    {
        var taken = Listed("TakenRover", "Taken Rover", "Taken Rover", "\"github\": \"someone/rover\"");
        var host = new FakeHost { Upload = new ShareUpload("u42", [new ShareCheck("name", false, "Not this name.")], "1.0.0", null) };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: taken), shareHostClient: host);
        var viewModel = harness.ViewModel;
        var editor = await ShareAsync(harness);
        editor.Authors = "Maxi";
        editor.License = "CC-BY-4.0";
        editor.Abstract = "A six-wheel rover for the Moon.";
        editor.ContinueShareCommand.Execute(null);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        editor.RenameForShareCommand.Execute(null);
        Assert.Equal(harness.Localization.FormatGameSaveShareRename("Example Rover"), viewModel.GameSaveRenameReason);
        viewModel.ModalInstanceName = "TakenRover";
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);

        Assert.Equal(ShareFlowStep.Describe, editor.ShareFlowStep);
        Assert.Equal(harness.Localization.FormatShareIdHeld("TakenRover"), editor.ShareIdError);
        Assert.Single(host.Uploads);
    }

    [Fact]
    public async Task NoHost_SaysSo_AndTryAgainReadsTheHostsAgain()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost(), indexOffline: true);
        var editor = await ShareAsync(harness);
        Assert.Equal(harness.Localization.ShareNoHostVehicle, editor.ShareNoHostText);
        Assert.False(editor.ShowsShareCheckButton);

        var textWentAway = false;
        void Watch(object? sender, PropertyChangedEventArgs args) => textWentAway |= args.PropertyName == nameof(ListingEditor.ShareNoHostText) && editor.ShareNoHostText is null;
        editor.PropertyChanged += Watch;
        await editor.RetryShareHostsCommand.ExecuteAsync(null);
        editor.PropertyChanged -= Watch;
        Assert.False(textWentAway);
        Assert.Equal(harness.Localization.ShareNoHostVehicle, editor.ShareNoHostText);

        harness.IndexOffline = false;
        await editor.RetryShareHostsCommand.ExecuteAsync(null);

        Assert.Null(editor.ShareNoHostText);
        Assert.Single(editor.ShareHostRows);
        Assert.True(editor.ShowsShareCheckButton);
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

    [Theory]
    [InlineData("folder", true, true)]
    [InlineData("name", true, false)]
    [InlineData("version", false, false)]
    public async Task FailedCheck_ComesFirstWithItsMessageAndBlocksThePublish_AndANameProblemOffersTheRename(string check, bool offersRename, bool isFolder)
    {
        var host = new FakeHost { Upload = new ShareUpload("u42", [new ShareCheck("meta.toml", true, null), new ShareCheck(check, false, "Taken by OtherRover.")], "1.0.0", null) };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await ShareAsync(harness);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        Assert.Equal([isFolder ? harness.Localization.ShareFolderCheckVehicle : harness.Localization.FormatListingShareCheckFailed(check, "Taken by OtherRover.")], editor.ShareHostFailedChecks);
        Assert.Equal([harness.Localization.FormatListingShareCheckPassed("meta.toml")], editor.ShareHostPassedChecks);
        Assert.False(editor.CanPublishToShareHost);
        Assert.Equal(harness.Localization.FormatListingShareHostChecksFailed("ksamods.gg"), editor.ShareHostError);
        Assert.False(editor.HasShareUpload);
        Assert.True(editor.ShowsShareCheckButton);
        Assert.Equal(offersRename, editor.ShowsShareRename);
        Assert.Equal(isFolder ? harness.Localization.ShareFolderCheckVehicle : null, editor.ShareFolderCheckText);
    }

    [Fact]
    public async Task RefusedUpload_ShowsItsChecks()
    {
        var failure = new ShareHostException(ShareHostFailure.Host, "A check failed.", "invalid_archive") { Checks = [new ShareCheck("meta.toml", true, null), new ShareCheck("meta.toml", true, null), new ShareCheck("folder", false, "Taken by OtherRover.")] };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost { UploadFailure = failure });
        var editor = await ShareAsync(harness);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        Assert.Equal([harness.Localization.FormatListingShareCheckPassed("meta.toml")], editor.ShareHostPassedChecks);
        Assert.Equal([harness.Localization.ShareFolderCheckVehicle], editor.ShareHostFailedChecks);
        Assert.Equal(harness.Localization.FormatListingShareHostChecksFailed("ksamods.gg"), editor.ShareHostError);
        Assert.False(editor.HasShareUpload);
        Assert.True(editor.ShowsShareRename);
    }

    [Fact]
    public async Task RefusedToken_SignsInAgainAndSendsOnceMore()
    {
        var host = new FakeHost { TokenRefusals = 1 };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: ListedRover), shareHostClient: host);
        var editor = await ShareAsync(harness);
        editor.ShareAsNewVersionCommand.Execute(null);

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
        Assert.Equal(ShareFlowStep.Done, editor.ShareFlowStep);
        Assert.Null(editor.ShareHostError);
        Assert.Equal(0, host.SignOuts);
    }

    [Fact]
    public async Task SignInThatFailsDuringAPublish_TakesTheCodeAwayAndKeepsTheUpload()
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: ListedRover), shareHostClient: host);
        var editor = await ShareAsync(harness);
        editor.ShareAsNewVersionCommand.Execute(null);
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
        Assert.True(editor.CanCancelShareHost);

        signIn.SetException(new ShareHostException(ShareHostFailure.SignInRefused, "The sign-in code expired.", "expired_token"));
        await publish;

        Assert.Null(editor.ShareHostUserCode);
        Assert.Null(editor.ShareHostCodeText);
        Assert.Equal(harness.Localization.FormatListingShareHostSignInRefused("ksamods.gg"), editor.ShareHostError);
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
        Assert.Equal(harness.Localization.FormatListingShareHostSignedOut("ksamods.gg"), editor.ShareHostError);
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
        editor.ShareAsNewVersionCommand.Execute(null);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        Assert.Equal("ExampleRover", Assert.Single(host.Uploads).ListingId);
        Assert.Null(Assert.Single(host.Publishes).Listing);
        Assert.Equal(ShareFlowStep.Done, editor.ShareFlowStep);
        Assert.Null(editor.SharePullRequest);
        Assert.Null(editor.ShareHostError);
        Assert.False(editor.HasShareUpload);
        Assert.Equal("ExampleRover", Assert.Single(await harness.Services.SharedContent.ListAsync()).ListingId);
        Assert.Null(await harness.Services.SharedContent.GetAuthorAsync());
    }

    [Fact]
    public async Task NewVersionAnswerWithoutId_RecordsTheIdOfTheListing()
    {
        var host = new FakeHost { PublishedId = null, PullRequest = null };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: ListedRover), shareHostClient: host);
        var editor = await ShareAsync(harness);
        editor.ShareAsNewVersionCommand.Execute(null);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        Assert.Equal("ExampleRover", Assert.Single(await harness.Services.SharedContent.ListAsync()).ListingId);
    }

    [Fact]
    public async Task EntryOfAShare_FindsTheListingBeforeTheIndexHoldsIt()
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        await harness.Services.SharedContent.RecordAsync(new SharedContentEntry(ContentType.Vehicle, "ksamods", "ksamods.gg", "RoverOfMaxi", "Example Rover", "1.2.0", null), null);

        var editor = await ShareAsync(harness);

        Assert.Equal(harness.Localization.FormatShareLooksLike("Example Rover", "ksamods.gg"), editor.ShareLooksLikeText);
        Assert.Equal(harness.Localization.FormatShareVersionOnline("1.2.0"), editor.ShareVersionOnlineText);

        editor.ShareAsNewVersionCommand.Execute(null);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        Assert.Equal("RoverOfMaxi", editor.Id);
        Assert.Equal("RoverOfMaxi", Assert.Single(host.Uploads).ListingId);
    }

    [Theory]
    [InlineData("1.2.0", false, "1.2.0")]
    [InlineData("0.9.0", false, "1.0.0")]
    [InlineData("1.0.0", true, null)]
    public async Task EntryOfAListingInTheIndex_ShowsTheVersionThatIsOnline(string recorded, bool yanked, string? online)
    {
        var vehicles = yanked ? ListedRover.Replace("\"release_status\"", "\"yanked\": true, \"release_status\"", StringComparison.Ordinal) : ListedRover;
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: vehicles), shareHostClient: new FakeHost());
        await harness.Services.SharedContent.RecordAsync(new SharedContentEntry(ContentType.Vehicle, "ksamods", "ksamods.gg", "ExampleRover", "Example Rover", recorded, null), null);

        var editor = await ShareAsync(harness);

        Assert.Equal(online is null ? null : harness.Localization.FormatShareVersionOnline(online), editor.ShareVersionOnlineText);
    }

    /// <summary>A listing that the index does not hold says nothing about the folder, and the check of the host decides. A rename has moved the record, which then goes too.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EntryOfADifferentItem_IsRemoved_AndAsksNoMore(bool indexHoldsTheFolder)
    {
        var vehicles = indexHoldsTheFolder ? Listed("RoverOfMaxi", "Rover of Maxi", "Example Rover", "\"ksamods\": \"RoverOfMaxi\"") : null;
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: vehicles), shareHostClient: new FakeHost());
        var viewModel = harness.ViewModel;
        await harness.Services.SharedContent.RecordAsync(new SharedContentEntry(ContentType.Vehicle, "ksamods", "ksamods.gg", "RoverOfMaxi", "Example Rover", "1.2.0", null), null);
        var editor = await ShareAsync(harness);

        await editor.ShareIsDifferentCommand.ExecuteAsync(null);

        Assert.Equal(indexHoldsTheFolder, viewModel.RenamingGameSave is not null);
        if (indexHoldsTheFolder)
        {
            viewModel.ModalInstanceName = "Example Rover 2";
            await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);
        }

        Assert.False(editor.IsAskingShareListing);
        Assert.False(editor.IsNewVersionShare);
        Assert.Empty(await harness.Services.SharedContent.ListAsync());
    }

    [Fact]
    public async Task InstallRecord_FindsTheListingOfAnItemWithAnotherName()
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: ListedRover), shareHostClient: host);
        var instance = await GameSaveShareTests.CreateInstanceAsync(harness, "Rover B");
        var folder = Path.Combine(harness.Services.Paths.GetInstanceVehiclesFolder(instance), "Rover B");
        await GameSaveShareTests.RecordAsync(harness, instance, folder);
        File.WriteAllText(Path.Combine(folder, "vehicle.xml"), "<VehicleSaveData Id=\"Rover B\" />");
        await GameSaveShareTests.OpenAsync(harness);
        var editor = harness.ViewModel.ListingEditor;
        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);
        await editor.ShareRowCompare;

        await editor.ContinueShareChooseCommand.ExecuteAsync(null);
        await editor.ShareHostsLoad;

        Assert.Equal(harness.Localization.FormatShareLooksLike("Example Rover", "ksamods.gg"), editor.ShareLooksLikeText);
        editor.ShareAsNewVersionCommand.Execute(null);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        Assert.Equal("ExampleRover", Assert.Single(host.Uploads).ListingId);
    }

    [Fact]
    public async Task FolderOfAListingOnAHost_OffersTheNewVersion_AndTheEnglishNoteStaysOnTheFullForm()
    {
        var host = new FakeHost();
        var listed = Listed("RoverOfMaxi", "Rover of Maxi", "Example Rover", "\"ksamods\": \"RoverOfMaxi\"");
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: listed), shareHostClient: host);
        var editor = await ShareAsync(harness);

        Assert.Equal(harness.Localization.FormatShareLooksLike("Rover of Maxi", "ksamods.gg"), editor.ShareLooksLikeText);
        Assert.DoesNotContain(editor.Notes, issue => issue.Location == "name");
        editor.EditAllShareFieldsCommand.Execute(null);
        Assert.Contains(editor.Notes, issue => issue.Location == "name");
        editor.BackToShareStepsCommand.Execute(null);

        editor.ShareAsNewVersionCommand.Execute(null);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        Assert.Equal("RoverOfMaxi", Assert.Single(host.Uploads).ListingId);
        Assert.DoesNotContain(editor.Errors, issue => issue.Location == "id");
    }

    [Fact]
    public async Task DifferentItemWithTheFolderOfAListing_OpensTheRename()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: Listed("RoverOfMaxi", "Rover of Maxi", "Example Rover", "\"ksamods\": \"RoverOfMaxi\"")), shareHostClient: new FakeHost());
        var viewModel = harness.ViewModel;
        var editor = await ShareAsync(harness);

        await editor.ShareIsDifferentCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatShareFolderTakenVehicle("Example Rover"), viewModel.GameSaveRenameReason);
        viewModel.CancelNameModalCommand.Execute(null);
        Assert.True(editor.IsAskingShareListing);

        await editor.ShareIsDifferentCommand.ExecuteAsync(null);
        viewModel.ModalInstanceName = "Example Rover 2";
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);

        Assert.False(editor.IsAskingShareListing);
        Assert.False(editor.IsNewVersionShare);
        Assert.Equal(("Example Rover 2", "ExampleRover2"), (editor.Name, editor.Id));
        Assert.Equal(ShareFlowStep.Describe, editor.ShareFlowStep);
    }

    [Theory]
    [InlineData("\"ksamods\": \"RoverOfMaxi\"")]
    [InlineData("\"github\": \"maxi/rover\"")]
    public async Task ListingWithTheSameNameAndAnotherFolder_LeavesTheShareANewListing(string releases)
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: Listed("RoverOfMaxi", "Example Rover", "Another Folder", releases)), shareHostClient: new FakeHost());

        var editor = await ShareAsync(harness);

        Assert.Null(harness.ViewModel.RenamingGameSave);
        Assert.Equal(ShareFlowStep.Describe, editor.ShareFlowStep);
        Assert.False(editor.IsAskingShareListing);
        Assert.False(editor.IsNewVersionShare);
        Assert.Equal("ExampleRover", editor.Id);
    }

    [Fact]
    public async Task FolderOfAListingOffAHost_OpensTheRenameWhenTheItemIsChosen_AndTheShareStartsAfterIt()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: Listed("RoverOfMaxi", "Rover of Maxi", "Example Rover", "\"github\": \"maxi/rover\"")), shareHostClient: new FakeHost());
        var viewModel = harness.ViewModel;
        var editor = viewModel.ListingEditor;
        var taken = harness.Localization.FormatShareFolderTakenVehicle("Example Rover");
        await GameSaveShareTests.CreateInstanceAsync(harness, "Example Rover");
        await GameSaveShareTests.OpenAsync(harness);

        await Assert.Single(viewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);
        await editor.ShareChooseLoad;

        Assert.Equal(taken, viewModel.GameSaveRenameReason);
        Assert.False(editor.HasShare);
        Assert.Equal(ShareFlowStep.Choose, editor.ShareFlowStep);
        Assert.Equal(taken, editor.ShareChooseError);

        viewModel.CancelNameModalCommand.Execute(null);
        await editor.ContinueShareChooseCommand.ExecuteAsync(null);
        viewModel.ModalInstanceName = "Example Rover 2";
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);

        Assert.True(editor.HasShare);
        Assert.Equal(ShareFlowStep.Describe, editor.ShareFlowStep);
        Assert.False(editor.IsAskingShareListing);
        Assert.Equal("Example Rover 2", editor.Name);
    }

    [Fact]
    public async Task NewVersion_AsksAgainAfterBackAndNext_SoThePlayerCanChangeTheAnswer()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: Listed("ExampleRover", "Rover of Maxi", "Another Folder", "\"ksamods\": \"ExampleRover\"")), shareHostClient: new FakeHost());
        var editor = await ShareAsync(harness);
        editor.ShareAsNewVersionCommand.Execute(null);

        editor.BackToShareChooseCommand.Execute(null);
        await editor.ShareChooseLoad;
        await editor.ContinueShareChooseCommand.ExecuteAsync(null);
        await editor.ShareHostsLoad;

        Assert.True(editor.IsAskingShareListing);
        Assert.False(editor.IsNewVersionShare);

        await editor.ShareIsDifferentCommand.ExecuteAsync(null);

        Assert.False(editor.IsAskingShareListing);
        Assert.False(editor.IsNewVersionShare);
    }

    [Fact]
    public async Task ListingWithOnlyTheId_DifferentOpensMoreOptionsWithTheError_AndAsksNoMore()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: Listed("ExampleRover", "Rover of Maxi", "Another Folder", "\"ksamods\": \"ExampleRover\"")), shareHostClient: new FakeHost());
        var editor = await ShareAsync(harness);

        await editor.ShareIsDifferentCommand.ExecuteAsync(null);

        Assert.Null(harness.ViewModel.RenamingGameSave);
        Assert.False(editor.IsAskingShareListing);
        Assert.False(editor.IsNewVersionShare);
        Assert.True(editor.IsShareMoreOptionsOpen);
        Assert.Equal(harness.Localization.FormatShareIdHeld("ExampleRover"), editor.ShareIdError);
    }

    [Fact]
    public async Task NoHostInContentIndex_OffersNone()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve(), shareHostClient: new FakeHost());

        var editor = await ShareAsync(harness);

        Assert.False(editor.HasShareHosts);
        Assert.Equal(harness.Localization.ShareNoHostVehicle, editor.ShareNoHostText);
        Assert.DoesNotContain(harness.Requests, uri => uri.AbsolutePath.EndsWith("/hosts.toml", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HostWithoutStorage_IsOfferedByItsName()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(DataHost), respond: Serve(), shareHostClient: new FakeHost());

        var editor = await ShareAsync(harness);

        Assert.Equal(harness.Localization.FormatShareHostOne("own.example"), editor.ShareHostHeadingText);
    }

    [Theory]
    [InlineData("not_owner", true)]
    [InlineData("too_large", true)]
    [InlineData("type_not_accepted", true)]
    [InlineData("invalid_archive", true)]
    [InlineData("some_new_code", true)]
    [InlineData("id_taken", false)]
    [InlineData("version_taken", false)]
    [InlineData("upload_expired", false)]
    public async Task RefusalOfTheHost_ShowsTheTextOfBorea_TheMessageOfAnUnknownCode_AndOneAction(string code, bool atUpload)
    {
        const string said = "Said by the host.";
        var refusal = new ShareHostException(ShareHostFailure.Host, said, code);
        var host = new FakeHost { UploadFailure = atUpload ? refusal : null, PublishFailure = atUpload ? null : refusal };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var text = harness.Localization;
        var editor = await ShareWithFieldsAsync(harness);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        (string Text, string? Detail, string? Action) expected = code switch
        {
            "not_owner" => (text.FormatShareErrorNotOwner("ksamods.gg", "Example Rover"), null, text.FormatShareSignOut("ksamods.gg")),
            "too_large" => (text.FormatShareErrorTooLarge("ksamods.gg"), null, null),
            "type_not_accepted" => (text.FormatShareErrorNoVehicles("ksamods.gg"), null, null),
            "invalid_archive" => (text.FormatListingShareHostChecksFailed("ksamods.gg"), null, null),
            "id_taken" => (text.FormatShareErrorIdTaken("ExampleRover", "ksamods.gg"), null, text.ShareBackToDescription),
            "version_taken" => (text.FormatShareErrorVersionTaken("Example Rover", "1.0.0", "1.1.0"), null, null),
            "upload_expired" => (text.FormatShareErrorUploadGone("ksamods.gg"), null, text.FormatShareOpenHost("ksamods.gg")),
            _ => (text.FormatListingShareHostSaid("ksamods.gg", said), null, null),
        };
        Assert.Equal(expected, (editor.ShareHostError, editor.ShareHostErrorDetail, editor.ShareErrorActionText));
        Assert.Equal(code == "invalid_archive" ? said : null, editor.ShareHostFileDetail);
        Assert.Equal(code == "version_taken", editor.HasShareUpload);
        Assert.Null(editor.ShareChecksPassedText);
        Assert.False(editor.CanPublishToShareHost);
        Assert.Equal(code == "version_taken", editor.IsEditingShareVersion);
        Assert.Equal(code == "upload_expired" || atUpload, editor.ShowsShareCheckButton);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(30, 1)]
    [InlineData(3700, 62)]
    public async Task RateLimit_SaysHowLongToWait(int? seconds, int? minutes)
    {
        var refusal = new ShareHostException(ShareHostFailure.Host, "Too many.", "rate_limited") { RetryAfter = seconds is { } wait ? TimeSpan.FromSeconds(wait) : null };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost { UploadFailure = refusal });
        var text = harness.Localization;
        var editor = await ShareWithFieldsAsync(harness);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        var expected = minutes is { } rounded ? text.FormatShareErrorRateLimited("ksamods.gg", text.FormatDuration(TimeSpan.FromMinutes(rounded))) : text.FormatShareErrorRateLimitedLater("ksamods.gg");
        Assert.Equal(expected, editor.ShareHostError);
        Assert.Null(editor.ShareErrorActionText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidRequest_ShowsTheLinesOfItsFieldsWhenItHasThem(bool hasFields)
    {
        var refusal = new ShareHostException(ShareHostFailure.Host, "The version is wrong.", "invalid_request")
        {
            FieldErrors = hasFields ? new Dictionary<string, IReadOnlyList<string>> { ["version"] = ["Use three numbers.", "Use a higher one."], ["name"] = ["Use another name."] } : null,
        };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost { PublishFailure = refusal });
        var editor = await ShareWithFieldsAsync(harness);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatListingShareHostSaid("ksamods.gg", "The version is wrong."), editor.ShareHostError);
        Assert.Equal(hasFields ? "Use three numbers.\nUse a higher one.\nUse another name." : null, editor.ShareHostErrorDetail);
    }

    [Theory]
    [InlineData(ShareHostFailure.NetworkError, false, false)]
    [InlineData(ShareHostFailure.Unavailable, false, false)]
    [InlineData(ShareHostFailure.UnexpectedResponse, false, false)]
    [InlineData(ShareHostFailure.NetworkError, false, true)]
    [InlineData(ShareHostFailure.Unavailable, false, true)]
    [InlineData(ShareHostFailure.UnexpectedResponse, false, true)]
    [InlineData(ShareHostFailure.Host, true, false)]
    [InlineData(ShareHostFailure.Host, true, true)]
    public async Task NoClearAnswer_AtPublishSaysThatASecondClickIsSafe_AndKeepsPublish(ShareHostFailure failure, bool isServerError, bool atPublish)
    {
        var lost = new ShareHostException(failure, "Server Error") { IsServerError = isServerError };
        var host = new FakeHost { UploadFailure = atPublish ? null : lost, PublishFailure = atPublish ? lost : null };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var text = harness.Localization;
        var editor = await ShareWithFieldsAsync(harness);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        var expected = atPublish
            ? text.FormatShareErrorLostAnswer("ksamods.gg")
            : failure switch
            {
                ShareHostFailure.NetworkError => text.FormatListingShareHostNetwork("ksamods.gg"),
                ShareHostFailure.Unavailable => text.FormatShareErrorUnavailable("ksamods.gg"),
                ShareHostFailure.Host => text.FormatListingShareHostSaid("ksamods.gg", "Server Error"),
                _ => text.FormatListingShareHostUnexpected("ksamods.gg"),
            };
        Assert.Equal(expected, editor.ShareHostError);
        Assert.Equal(isServerError && atPublish ? "Server Error" : null, editor.ShareHostErrorDetail);
        Assert.Equal(atPublish ? text.FormatShareOpenHost("ksamods.gg") : null, editor.ShareErrorActionText);
        Assert.Equal(atPublish, editor.CanPublishToShareHost);
    }

    [Fact]
    public async Task UploadThatIsGone_OffersANewCheck_AndItsActionOpensTheSiteOfTheHost()
    {
        var opened = new ConcurrentQueue<string>();
        var host = new FakeHost { PublishFailure = new ShareHostException(ShareHostFailure.Host, "It is gone.", "upload_expired") };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        harness.ViewModel.OpenWithSystem = opened.Enqueue;
        var editor = await ShareWithFieldsAsync(harness);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        Assert.False(editor.HasShareUpload);
        Assert.Empty(editor.ShareHostPassedChecks);
        Assert.Null(editor.ShareHostProgress);
        editor.RunShareErrorActionCommand.Execute(null);
        Assert.Equal(["https://ksamods.gg/"], opened);
    }

    [Fact]
    public async Task HostPageThatCannotOpen_KeepsTheTextOfTheErrorAndItsLink()
    {
        var host = new FakeHost { PublishFailure = new ShareHostException(ShareHostFailure.NetworkError, "No answer.") };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        harness.ViewModel.OpenWithSystem = _ => throw new InvalidOperationException("No browser.");
        var text = harness.Localization;
        var editor = await ShareWithFieldsAsync(harness);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        editor.RunShareErrorActionCommand.Execute(null);

        Assert.Equal(text.FormatShareErrorLostAnswer("ksamods.gg"), editor.ShareHostError);
        Assert.Equal(text.FormatListingShareHostOpenFailed("No browser."), editor.ShareHostErrorDetail);
        Assert.Equal(text.FormatShareOpenHost("ksamods.gg"), editor.ShareErrorActionText);
    }

    [Fact]
    public async Task PublishThatFailsACheck_DropsTheUpload_AndOffersANewCheck()
    {
        var refusal = new ShareHostException(ShareHostFailure.Host, "A check failed.", "invalid_archive") { Checks = [new ShareCheck("folder", false, "Taken by OtherRover.")] };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost { PublishFailure = refusal });
        var editor = await ShareWithFieldsAsync(harness);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatListingShareHostChecksFailed("ksamods.gg"), editor.ShareHostError);
        Assert.Single(editor.ShareHostFailedChecks);
        Assert.Null(editor.ShareChecksPassedText);
        Assert.False(editor.HasShareUpload);
        Assert.True(editor.ShowsShareCheckButton);
        Assert.True(editor.ShowsShareRename);
    }

    [Fact]
    public async Task HostThatTakesNoSaves_SaysSoForASave()
    {
        var host = new FakeHost { UploadFailure = new ShareHostException(ShareHostFailure.Host, "No saves.", "type_not_accepted") };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var instance = await GameSaveShareTests.CreateInstanceAsync(harness, "Example Rover");
        GameSaveShareTests.WriteItem(harness.Services.Paths.GetInstanceSavesFolder(instance), "Moon Base", "universe.xml", "<UniverseData><System><Id Id=\"Sol\" /></System></UniverseData>");
        await GameSaveShareTests.OpenAsync(harness);
        await Assert.Single(harness.ViewModel.SavesSection.Items).ShareCommand.ExecuteAsync(null);
        var editor = harness.ViewModel.ListingEditor;
        await editor.ShareHostsLoad;
        FillRequiredFields(editor);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        Assert.Equal(harness.Localization.FormatShareErrorNoSaves("ksamods.gg"), editor.ShareHostError);
    }

    [Fact]
    public async Task NotOwner_SignsOutOfTheHostThatItNames_WhenAnotherHostIsSelected()
    {
        var host = new FakeHost { UploadFailure = new ShareHostException(ShareHostFailure.Host, "Not yours.", "not_owner") };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(TwoHosts), respond: Serve(), shareHostClient: host);
        var editor = await ShareWithFieldsAsync(harness);
        var refused = editor.ShareHostRows[0].Host;
        await editor.UploadToShareHostCommand.ExecuteAsync(editor.ShareHostRows[0]);

        editor.SelectedShareHostRow = editor.ShareHostRows[1];

        Assert.Equal(harness.Localization.FormatShareSignOut(refused.Name), editor.ShareErrorActionText);
        editor.RunShareErrorActionCommand.Execute(null);
        Assert.Equal(refused.Key, host.SignedOutOf);
    }

    [Fact]
    public async Task NotOwner_DropsTheLocalEntry_AndTheSignOutLeadsToANewSignIn()
    {
        var host = new FakeHost { UploadFailure = new ShareHostException(ShareHostFailure.Host, "Your account does not own ExampleRover.", "not_owner") };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        await harness.Services.SharedContent.RecordAsync(new SharedContentEntry(ContentType.Vehicle, "ksamods", "ksamods.gg", "ExampleRover", "Example Rover", "1.0.0", null), null);
        var editor = await ShareWithFieldsAsync(harness);
        var row = Assert.Single(harness.ViewModel.VehiclesSection.Items);
        Assert.NotNull(row.SharedOnText);
        editor.ShareAsNewVersionCommand.Execute(null);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        Assert.Empty(await harness.Services.SharedContent.ListAsync());
        Assert.Null(row.SharedOnText);
        Assert.Null(editor.ShareSignedInText);

        editor.RunShareErrorActionCommand.Execute(null);

        Assert.Equal(1, host.SignOuts);
        Assert.Null(editor.ShareHostError);
        Assert.Null(editor.ShareErrorActionText);

        host.UploadFailure = null;
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        Assert.Equal(2, host.SignIns);
        Assert.True(editor.HasShareUpload);
    }

    [Fact]
    public async Task SignOutLink_ShowsOnceSignedIn_AndDropsTheUpload()
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var text = harness.Localization;
        var editor = await ShareWithFieldsAsync(harness);
        Assert.Null(editor.ShareSignedInText);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        Assert.Equal((text.FormatShareSignedIn("ksamods.gg"), text.FormatShareSignOut("ksamods.gg")), (editor.ShareSignedInText, editor.ShareSignOutText));
        Assert.True(editor.HasShareUpload);

        editor.SignOutOfShareHostCommand.Execute(null);

        Assert.Equal(1, host.SignOuts);
        Assert.False(editor.HasShareUpload);
        Assert.Null(editor.ShareSignedInText);
    }

    [Fact]
    public async Task VersionTaken_HoldsPublishUntilThePlayerChangesTheVersion()
    {
        var host = new FakeHost { PublishFailure = new ShareHostException(ShareHostFailure.Host, "It had that version.", "version_taken") };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await ShareWithFieldsAsync(harness);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        var passed = editor.ShareChecksPassedText;
        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        Assert.Null(editor.ShareChecksPassedText);
        Assert.False(editor.CanPublishToShareHost);
        Assert.True(editor.IsEditingShareVersion);

        editor.ShareHostVersion = "1.1.0";

        Assert.Equal(passed, editor.ShareChecksPassedText);
        Assert.True(editor.CanPublishToShareHost);
        Assert.Null(editor.ShareHostError);
    }

    [Fact]
    public async Task IdTaken_GoesBackToTheShortName_AndAnotherOneGoesOn()
    {
        var host = new FakeHost { PublishFailure = new ShareHostException(ShareHostFailure.Host, "It is taken.", "id_taken") };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await ShareWithFieldsAsync(harness);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await editor.PublishToShareHostCommand.ExecuteAsync(null);

        editor.RunShareErrorActionCommand.Execute(null);

        Assert.Equal(ShareFlowStep.Describe, editor.ShareFlowStep);
        Assert.False(editor.HasShareUpload);
        Assert.True(editor.IsShareMoreOptionsOpen);
        Assert.Equal(harness.Localization.FormatShareIdHeld("ExampleRover"), editor.ShareIdError);
        Assert.False(editor.CanContinueShare);

        editor.Id = "MyRover";

        Assert.Null(editor.ShareIdError);
        Assert.True(editor.CanContinueShare);
    }

    [Fact]
    public async Task WhileAPublishIsOut_CancelAndBackAreGone_ButAnUploadCanBeCancelled()
    {
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost
        {
            DuringSend = () =>
            {
                arrived.TrySetResult();
                return answer.Task;
            },
        };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await ShareWithFieldsAsync(harness);

        var upload = editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(editor.CanCancelShareHost);
        Assert.False(editor.IsSendingSharePublish);
        answer.SetResult();
        await upload;

        answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publish = editor.PublishToShareHostCommand.ExecuteAsync(null);

        Assert.False(editor.CanCancelShareHost);
        Assert.True(editor.IsSendingSharePublish);
        editor.OpenShareStep("describe");
        editor.OpenShareStep("choose");
        Assert.Equal(ShareFlowStep.Publish, editor.ShareFlowStep);

        answer.SetResult();
        await publish;

        Assert.False(editor.IsSendingSharePublish);
        Assert.Null(editor.ShareHostError);
    }

    [Fact]
    public async Task FolderThatIsGone_NamesTheItem_WithTheExceptionInTheDetails()
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await ShareWithFieldsAsync(harness);
        Directory.Delete(Assert.Single(harness.ViewModel.VehiclesSection.Items).Entry.Path, recursive: true);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        Assert.Equal(harness.Localization.FormatShareErrorFile("Example Rover"), editor.ShareHostError);
        Assert.NotNull(editor.ShareHostFileDetail);
        Assert.True(editor.HasShareDetails);
        Assert.Empty(host.Uploads);
        Assert.Null(editor.ShareHostProgress);
    }

    [Fact]
    public async Task FileErrorAfterACancel_ShowsNothing()
    {
        ListingEditor? editor = null;
        var host = new FakeHost
        {
            DuringSend = () =>
            {
                editor!.CancelShareHostCommand.Execute(null);
                throw new IOException("The folder is gone.");
            },
        };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        editor = await ShareWithFieldsAsync(harness);

        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));

        Assert.Null(editor.ShareHostError);
        Assert.Null(editor.ShareHostFileDetail);
        Assert.Null(editor.ShareHostProgress);
    }

    /// <summary>A harness whose index lists the rover of the instance on a share host, so a share of it is a new version.</summary>
    internal static Task<ViewModelHarness> ListedRoverHarnessAsync() =>
        ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles: ListedRover), shareHostClient: new FakeHost());

    /// <summary>A new listing on the publish step with two pictures, which then go to a host that takes them.</summary>
    internal static async Task<ViewModelHarness> PictureHarnessAsync(bool publish)
    {
        var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost());
        var editor = await SharePicturesAsync(harness, 2);
        if (publish)
        {
            await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
            await editor.PublishToShareHostCommand.ExecuteAsync(null);
        }

        return harness;
    }

    /// <summary>A published listing whose host holds the first of two pictures until the run is cancelled. The test cancels and awaits the task that this returns.</summary>
    internal static async Task<(ViewModelHarness Harness, Task Publish)> HeldPictureHarnessAsync()
    {
        var host = new FakeHost { ImageGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await SharePicturesAsync(harness, 2);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        var publish = editor.PublishToShareHostCommand.ExecuteAsync(null);
        await host.ImageStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        return (harness, publish);
    }

    /// <summary>A new listing on the publish step, which shows the cover that Borea drew.</summary>
    internal static async Task<ViewModelHarness> CoverHarnessAsync()
    {
        var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost());
        var (service, encoder) = CoverParts();
        await ShareWithCoverAsync(harness, service, encoder);
        return harness;
    }

    private static (FakeVehiclePreview Service, CoverEncoder Encoder) CoverParts() =>
        (new FakeVehiclePreview { Vehicle = VehiclePreviewViewModelTests.TriangleVehicle(), Files = VehiclePreviewViewModelTests.TriangleFiles }, new CoverEncoder());

    /// <summary>A share of a new listing that stands on the publish step and has finished its drawing.</summary>
    private static async Task<ListingEditor> ShareWithCoverAsync(ViewModelHarness harness, FakeVehiclePreview service, CoverEncoder encoder)
    {
        harness.ViewModel.VehiclePreviewServiceOf = _ => service;
        harness.ViewModel.VehiclePreviewEncoder = encoder;
        var editor = await ShareWithFieldsAsync(harness);
        await editor.ShareCoverDraw;
        return editor;
    }

    /// <summary>A share of a new listing that a host with a request has published.</summary>
    internal static async Task<ViewModelHarness> PublishedHarnessAsync()
    {
        var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: new FakeHost());
        await PublishAsync(harness);
        return harness;
    }

    private static async Task<ListingEditor> PublishAsync(ViewModelHarness harness)
    {
        var editor = await ShareWithFieldsAsync(harness);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await editor.PublishToShareHostCommand.ExecuteAsync(null);
        return editor;
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

    /// <summary>A valid picture whose length tells it from the others, because the bytes after the end of a PNG do not count.</summary>
    private static byte[] Picture(int number) => [.. ListingEditorTests.Png(16, 9), .. new byte[number]];

    /// <summary>A share of a new listing that stands on the publish step, with pictures numbered from 1.</summary>
    private static async Task<ListingEditor> SharePicturesAsync(ViewModelHarness harness, int count)
    {
        var editor = await ShareAsync(harness);
        var window = new PictureWindow();
        harness.ViewModel.WindowServices = window;
        for (var number = 1; number <= count; number++)
        {
            window.File = new PickedBinaryFile($"shot{number}.png", Picture(number));
            await editor.AddSharePictureCommand.ExecuteAsync(null);
        }

        FillRequiredFields(editor);
        return editor;
    }

    private static async Task<ListingEditor> PublishPicturesAsync(ViewModelHarness harness, int count)
    {
        var editor = await SharePicturesAsync(harness, count);
        await editor.UploadToShareHostCommand.ExecuteAsync(Assert.Single(editor.ShareHostRows));
        await editor.PublishToShareHostCommand.ExecuteAsync(null);
        return editor;
    }

    /// <summary>A share of a new listing that stands on the publish step.</summary>
    private static async Task<ListingEditor> ShareWithFieldsAsync(ViewModelHarness harness)
    {
        var editor = await ShareAsync(harness);
        FillRequiredFields(editor);
        return editor;
    }

    private static void FillRequiredFields(ListingEditor editor)
    {
        editor.Authors = "Maxi";
        editor.License = "CC-BY-4.0";
        editor.Abstract = "A six-wheel rover for the Moon.";
        editor.ContinueShareCommand.Execute(null);
    }

    private const string OwnPage = "https://ksamods.gg/vehicles/ExampleRover";

    private static readonly SharedContentEntry OwnRecord = new(ContentType.Vehicle, "ksamods", "ksamods.gg", "ExampleRover", "Example Rover", "1.2.0", new Uri(OwnPage));

    /// <summary>The page of the listed rover, whose owner the record says the player is when <paramref name="record"/> is given.</summary>
    private static async Task<ViewModelHarness> OwnedPageAsync(FakeHost host, SharedContentEntry? record, string? homepage = null, bool isYanked = false)
    {
        var links = homepage is null ? string.Empty : $$""" "links": { "homepage": "{{homepage}}" }, """;
        var vehicles = ListedRover.Replace("\"releases\": { \"ksamods\"", links + "\"releases\": { \"ksamods\"", StringComparison.Ordinal);
        if (isYanked)
            vehicles = vehicles.Replace("\"release_status\": \"stable\"", "\"release_status\": \"stable\", \"yanked\": true", StringComparison.Ordinal);

        var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(vehicles), shareHostClient: host);
        if (record is not null)
            await harness.Services.SharedContent.RecordAsync(record, author: null);

        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);
        await Assert.Single(viewModel.DiscoverItems).OpenCommand.ExecuteAsync(null);
        return harness;
    }

    /// <summary>The page of a rover that the player owns, for the views.</summary>
    internal static Task<ViewModelHarness> OwnedPageHarnessAsync() => OwnedPageAsync(new FakeHost(), OwnRecord);

    /// <summary>The page of a rover whose last version the player took down, and the index knows that too.</summary>
    internal static Task<ViewModelHarness> OwnedPageWithoutVersionsHarnessAsync() => OwnedPageAsync(new FakeHost(), OwnRecord with { Version = string.Empty }, isYanked: true);

    private static async Task<ShareManageDialog> TakeDownDialogAsync(ViewModelHarness harness)
    {
        await harness.ViewModel.TakeDownContentVersionCommand.ExecuteAsync(null);
        return Assert.IsType<ShareManageDialog>(harness.ViewModel.ShareManage);
    }

    private static ShareManageDialog ChangeDialog(ViewModelHarness harness)
    {
        harness.ViewModel.ChangeContentOnHostCommand.Execute(null);
        return Assert.IsType<ShareManageDialog>(harness.ViewModel.ShareManage);
    }

    [Theory]
    [InlineData(OwnPage, null, OwnPage)]
    [InlineData(null, OwnPage, OwnPage)]
    [InlineData(null, "https://evil.example/ExampleRover", null)]
    [InlineData(null, null, null)]
    public async Task ContentPage_OwnListing_LinksThePageOfTheRecordOrAHomepageOnTheHost(string? recorded, string? homepage, string? expected)
    {
        var record = OwnRecord with { Page = recorded is null ? null : new Uri(recorded) };
        using var harness = await OwnedPageAsync(new FakeHost(), record, homepage);
        var viewModel = harness.ViewModel;
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;

        Assert.True(viewModel.CanManageContent);
        Assert.Equal(expected is null ? null : harness.Localization.FormatShareManageOnHost("ksamods.gg"), viewModel.ContentManageOnHostText);

        viewModel.OpenContentManagePageCommand.Execute(null);

        Assert.Equal(expected is null ? [] : [expected], opened);
    }

    [Theory]
    [InlineData(ContentType.Vehicle, "OtherRover", "ksamods")]
    [InlineData(ContentType.Vehicle, "ExampleRover", "ownhost")]
    [InlineData(ContentType.Save, "ExampleRover", "ksamods")]
    public async Task ContentPage_RecordOfAnotherListing_OffersNothing(ContentType type, string id, string hostKey)
    {
        using var harness = await OwnedPageAsync(new FakeHost(), OwnRecord with { Type = type, ListingId = id, HostKey = hostKey });

        Assert.False(harness.ViewModel.CanManageContent);
        Assert.Null(harness.ViewModel.ContentManageOnHostText);
    }

    [Fact]
    public async Task ContentPage_NoRecord_OffersNothing_AndTheCommandsOpenNoDialog()
    {
        using var harness = await OwnedPageAsync(new FakeHost(), record: null, homepage: OwnPage);
        var viewModel = harness.ViewModel;

        viewModel.ChangeContentOnHostCommand.Execute(null);
        await viewModel.TakeDownContentVersionCommand.ExecuteAsync(null);

        Assert.False(viewModel.CanManageContent);
        Assert.False(viewModel.IsShareManageOpen);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Change_SendsOnlyTheChangedFields_AndSaysThatItGoesToDiscoverByARequest(bool hasRequest)
    {
        var host = new FakeHost { PullRequest = hasRequest ? new Uri("https://github.com/KSAModding/content-index/pull/130") : null };
        using var harness = await OwnedPageAsync(host, OwnRecord);
        var dialog = ChangeDialog(harness);
        Assert.Equal(("Example Rover", "A small rover.", "CC0-1.0"), (dialog.Name, dialog.Abstract, dialog.License));
        Assert.Equal(harness.Localization.ShareManageNoChange, dialog.SendReason);
        Assert.False(dialog.SendChangeCommand.CanExecute(null));

        dialog.Abstract = "A sturdier rover.";
        dialog.FreeTags = "Rover, moon";
        await dialog.SendChangeCommand.ExecuteAsync(null);

        Assert.Equal(1, host.SignIns);
        var sent = Assert.Single(host.Changes);
        Assert.Equal(("vehicle", "ExampleRover"), (sent.Type, sent.Id));
        Assert.Equal((null, "A sturdier rover.", null, null), (sent.Fields.Name, sent.Fields.Abstract, sent.Fields.Description, sent.Fields.License));
        Assert.Equal(["rover", "moon"], sent.Fields.Tags);
        Assert.Equal(hasRequest ? harness.Localization.FormatShareManageSent("ksamods.gg") : harness.Localization.FormatShareManageSentLater("ksamods.gg"), dialog.DoneText);
        Assert.Equal(host.PullRequest, dialog.PullRequest);
        Assert.False(dialog.ShowsChangeFields);
        Assert.False(dialog.SendChangeCommand.CanExecute(null));
    }

    [Fact]
    public async Task Change_ChecksWhatChanged_LikeTheDescribeStep()
    {
        var host = new FakeHost();
        using var harness = await OwnedPageAsync(host, OwnRecord);
        var localization = harness.Localization;
        var dialog = ChangeDialog(harness);

        dialog.Abstract = string.Empty;
        Assert.Equal(localization.FormatListingStillMissing(localization.ShareMissingSentence), dialog.SendReason);

        dialog.Abstract = new string('a', ListingDraft.AbstractLimit + 1);
        Assert.Equal(localization.FormatShareSentenceTooLong(ListingDraft.AbstractLimit + 1, ListingDraft.AbstractLimit), dialog.SendReason);

        dialog.Abstract = "A small rover.";
        dialog.License = "Not a license";
        Assert.Equal(localization.ShareLicenseInvalid, dialog.SendReason);

        dialog.License = "CC0-1.0";
        dialog.FreeTags = "!!";
        Assert.Equal(localization.FormatListingTagInvalid("!!"), dialog.SendReason);
        Assert.False(dialog.SendChangeCommand.CanExecute(null));

        dialog.FreeTags = string.Empty;
        dialog.Description = "Longer text.";
        Assert.Null(dialog.SendReason);
        Assert.True(dialog.SendChangeCommand.CanExecute(null));
        Assert.Empty(host.Changes);
    }

    [Fact]
    public async Task Change_OpenedAgain_StartsFromWhatTheHostHolds_AndCanSendTheOldTextBack()
    {
        var host = new FakeHost();
        using var harness = await OwnedPageAsync(host, OwnRecord);
        var first = ChangeDialog(harness);
        first.Abstract = "A sturdier rover.";
        await first.SendChangeCommand.ExecuteAsync(null);
        first.CloseCommand.Execute(null);
        var second = ChangeDialog(harness);
        Assert.Equal(harness.Localization.ShareManageNoChange, second.SendReason);
        second.Name = "Sturdy Rover";
        await second.SendChangeCommand.ExecuteAsync(null);
        second.CloseCommand.Execute(null);

        var third = ChangeDialog(harness);

        Assert.Equal(("Sturdy Rover", "A sturdier rover."), (third.Name, third.Abstract));
        Assert.Equal(harness.Localization.ShareManageNoChange, third.SendReason);

        third.Abstract = "A small rover.";
        await third.SendChangeCommand.ExecuteAsync(null);

        var sent = host.Changes.Last().Fields;
        Assert.Equal((null, "A small rover."), (sent.Name, sent.Abstract));
    }

    [Fact]
    public async Task TakeDown_OpeningSendsNothing_TheConfirmationNamesTheChosenVersion_AndOnlyItsButtonSends()
    {
        var host = new FakeHost();
        using var harness = await OwnedPageAsync(host, OwnRecord);
        var localization = harness.Localization;

        var dialog = await TakeDownDialogAsync(harness);

        Assert.Equal(["1.2.0", "1.0.0"], dialog.Versions);
        Assert.Null(dialog.SelectedVersion);
        Assert.Null(dialog.TakeDownWarning);
        Assert.True(dialog.ShowsTakeDownButton);
        Assert.Equal(localization.ShareTakeDownButton, dialog.TakeDownText);
        Assert.False(dialog.TakeDownCommand.CanExecute(null));

        dialog.SelectedVersion = "1.2.0";

        Assert.Equal(localization.FormatShareTakeDownWarning("1.2.0", "Example Rover", "ksamods.gg"), dialog.TakeDownWarning);
        Assert.Equal(localization.FormatShareTakeDownVersion("1.2.0"), dialog.TakeDownText);
        Assert.Empty(host.TakeDowns);

        await dialog.TakeDownCommand.ExecuteAsync(null);

        Assert.Equal(new TakeDown("vehicle", "ExampleRover", "1.2.0"), Assert.Single(host.TakeDowns));
        Assert.Equal(localization.FormatShareTakenDown("1.2.0"), dialog.DoneText);
        Assert.Equal("1.0.0", Assert.Single(await harness.Services.SharedContent.ListAsync()).Version);
        Assert.False(dialog.TakeDownCommand.CanExecute(null));
    }

    [Fact]
    public async Task DoneStep_TakesDownThePublishedVersion_AndNoLongerSaysThatItIsOnline()
    {
        var host = new FakeHost { Page = new Uri(OwnPage) };
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await PublishAsync(harness);
        var localization = harness.Localization;
        Assert.Equal(localization.ShareDoneNow, editor.ShareDoneStateText);
        Assert.Equal(localization.FormatShareManageOnHost("ksamods.gg"), editor.ShareDoneManageOnHostText);

        editor.TakeDownShareDoneCommand.Execute(null);
        var dialog = Assert.IsType<ShareManageDialog>(harness.ViewModel.ShareManage);
        Assert.Equal("1.0.0", dialog.SelectedVersion);
        Assert.False(dialog.ShowsVersionChoice);
        Assert.Empty(host.TakeDowns);

        await dialog.TakeDownCommand.ExecuteAsync(null);
        dialog.CloseCommand.Execute(null);

        Assert.Equal(new TakeDown("vehicle", "ExampleRover", "1.0.0"), Assert.Single(host.TakeDowns));
        Assert.False(harness.ViewModel.IsShareManageOpen);
        Assert.Equal(localization.FormatShareTakenDownHeading("Example Rover", "1.0.0"), editor.ShareDoneHeadingText);
        Assert.Equal(localization.FormatShareTakenDown("1.0.0"), editor.ShareDoneStateText);
        Assert.Null(editor.ShareDoneDiscoverText);
        Assert.Null(editor.ShareDoneTakeDownText);
        Assert.Equal(string.Empty, Assert.Single(await harness.Services.SharedContent.ListAsync()).Version);

        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);

        Assert.True(editor.IsAskingShareListing);
        Assert.Null(editor.ShareVersionOnlineText);
    }

    [Fact]
    public async Task DoneStep_NewListing_ChangeStartsFromWhatItPublished()
    {
        var host = new FakeHost();
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(Hosts), respond: Serve(), shareHostClient: host);
        var editor = await PublishAsync(harness);

        editor.ChangeShareDoneOnHostCommand.Execute(null);

        var dialog = Assert.IsType<ShareManageDialog>(harness.ViewModel.ShareManage);
        Assert.Equal((editor.Draft.Name, editor.Draft.Abstract, editor.Draft.License), (dialog.Name, dialog.Abstract, dialog.License));
        Assert.Null(editor.ShareDoneManageOnHostText);
        Assert.Equal(harness.Localization.FormatShareOpenHost("ksamods.gg"), dialog.ManageLinkText);
    }

    [Fact]
    public async Task Refusal_NotOwner_ShowsTheTextOfBorea_KeepsTheRecord_AndOffersAnotherAccount()
    {
        var host = new FakeHost { ManageFailure = new ShareHostException(ShareHostFailure.Host, "Your account does not own ExampleRover.", "not_owner") };
        using var harness = await OwnedPageAsync(host, OwnRecord);
        var dialog = ChangeDialog(harness);
        dialog.Name = "New name";

        await dialog.SendChangeCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatShareErrorNotOwner("ksamods.gg", "Example Rover"), dialog.Error);
        Assert.Null(dialog.DoneText);
        Assert.Equal(harness.Localization.FormatShareSignOut("ksamods.gg"), dialog.SignOutText);
        Assert.Single(await harness.Services.SharedContent.ListAsync());

        dialog.SignOutCommand.Execute(null);

        Assert.Equal(1, host.SignOuts);
        Assert.Null(dialog.SignOutText);
        Assert.Null(dialog.Error);
    }

    [Fact]
    public async Task Refusal_HostWithoutTheRequest_SaysSo_AndTheLinkStays()
    {
        var host = new FakeHost { ManageFailure = new ShareHostException(ShareHostFailure.NotOffered, "ksamods.gg does not offer this request.") };
        using var harness = await OwnedPageAsync(host, OwnRecord);
        var opened = new List<string>();
        harness.ViewModel.OpenWithSystem = opened.Add;
        var dialog = await TakeDownDialogAsync(harness);
        dialog.SelectedVersion = "1.0.0";

        await dialog.TakeDownCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatShareManageNotOffered("ksamods.gg"), dialog.Error);
        Assert.Null(dialog.DoneText);
        Assert.Equal("1.2.0", Assert.Single(await harness.Services.SharedContent.ListAsync()).Version);
        Assert.Equal(harness.Localization.FormatShareManageOnHost("ksamods.gg"), dialog.ManageLinkText);

        dialog.OpenManageLinkCommand.Execute(null);

        Assert.Equal([OwnPage], opened);
    }

    [Theory]
    [InlineData(ShareHostFailure.NetworkError, false)]
    [InlineData(ShareHostFailure.UnexpectedResponse, false)]
    [InlineData(ShareHostFailure.Unavailable, false)]
    [InlineData(ShareHostFailure.Host, true)]
    public async Task TakeDown_WithoutAClearAnswer_SaysThatItMayHaveGoneThrough(ShareHostFailure failure, bool isServerError)
    {
        var host = new FakeHost { ManageFailure = new ShareHostException(failure, "Busy.") { IsServerError = isServerError } };
        using var harness = await OwnedPageAsync(host, OwnRecord);
        var dialog = await TakeDownDialogAsync(harness);
        dialog.SelectedVersion = "1.0.0";

        await dialog.TakeDownCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatShareTakeDownLostAnswer("ksamods.gg", "1.0.0"), dialog.Error);
        Assert.Null(dialog.DoneText);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task TakeDown_SignInFails_SaysWhatFailed_AndNotThatItMayHaveGoneThrough(int tokenRefusals)
    {
        var host = new FakeHost { TokenRefusals = tokenRefusals };
        host.DuringSignIn = () => host.SignIns > tokenRefusals ? throw new ShareHostException(ShareHostFailure.NetworkError, "No network.") : Task.CompletedTask;
        using var harness = await OwnedPageAsync(host, OwnRecord);
        var dialog = await TakeDownDialogAsync(harness);
        dialog.SelectedVersion = "1.0.0";

        await dialog.TakeDownCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatListingShareHostNetwork("ksamods.gg"), dialog.Error);
        Assert.Equal(tokenRefusals, host.TakeDowns.Count);
    }

    [Fact]
    public async Task TakeDown_TokenRefusedOnce_SignsInAndSendsOnceMore()
    {
        var host = new FakeHost { TokenRefusals = 1 };
        using var harness = await OwnedPageAsync(host, OwnRecord);
        var dialog = await TakeDownDialogAsync(harness);
        dialog.SelectedVersion = "1.0.0";

        await dialog.TakeDownCommand.ExecuteAsync(null);

        Assert.Equal(2, host.SignIns);
        Assert.Equal(2, host.TakeDowns.Count);
        Assert.Equal(harness.Localization.FormatShareTakenDown("1.0.0"), dialog.DoneText);
    }

    [Fact]
    public async Task Close_DuringTheSignIn_SendsNothing()
    {
        var host = new FakeHost();
        using var harness = await OwnedPageAsync(host, OwnRecord);
        var dialog = await TakeDownDialogAsync(harness);
        dialog.SelectedVersion = "1.0.0";
        host.DuringSignIn = () =>
        {
            dialog.CloseCommand.Execute(null);
            return Task.CompletedTask;
        };

        await dialog.TakeDownCommand.ExecuteAsync(null);

        Assert.Empty(host.TakeDowns);
        Assert.False(harness.ViewModel.IsShareManageOpen);
    }

    [Fact]
    public async Task Close_IsOffWhileARequestIsOut()
    {
        var answer = new TaskCompletionSource();
        var host = new FakeHost { DuringSend = () => answer.Task };
        using var harness = await OwnedPageAsync(host, OwnRecord);
        var dialog = await TakeDownDialogAsync(harness);
        dialog.SelectedVersion = "1.0.0";
        var sending = dialog.TakeDownCommand.ExecuteAsync(null);

        Assert.True(dialog.IsSending);
        Assert.False(dialog.CloseCommand.CanExecute(null));

        answer.SetResult();
        await sending;

        Assert.True(dialog.CloseCommand.CanExecute(null));
    }

    /// <summary>Runs a click at the first moment the page takes one again, which is before the rename lists the instance again.</summary>
    private static void ClickWhenTheModalCloses(MainViewModel viewModel, Action click)
    {
        PropertyChangedEventHandler? closed = null;
        closed = (_, args) =>
        {
            if (args.PropertyName != nameof(MainViewModel.RenamingGameSave) || viewModel.RenamingGameSave is not null)
                return;

            viewModel.PropertyChanged -= closed;
            click();
        };
        viewModel.PropertyChanged += closed;
    }

    /// <summary>Serves the schema without its gate and the vehicles of the index.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage?> Serve(string? vehicles = null) => request => request.RequestUri?.AbsoluteUri switch
    {
        { } uri when uri == VehiclesUrl && vehicles is not null => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(vehicles) },
        _ => null,
    };

    private static readonly string ListedRover = Listed("ExampleRover", "Example Rover", "Example Rover", "\"ksamods\": \"ExampleRover\"");

    /// <summary>The vehicles of an index with one listing of that name, whose one release installs into the folder.</summary>
    private static string Listed(string id, string name, string folder, string releases) => $$"""
        { "snapshot_version": 1, "{{ContentIndexTypeFiles.Vehicles.ArrayKey}}": [{
            "id": "{{id}}",
            "authored": { "spec_version": 1, "id": "{{id}}", "type": "vehicle", "name": "{{name}}", "authors": ["Maxi"], "abstract": "A small rover.", "license": "CC0-1.0", "releases": { {{releases}} } },
            "releases": [{
                "spec_version": 1, "id": "{{id}}", "type": "vehicle", "version": "1.0.0", "version_scheme": "semver", "release_status": "stable",
                "release_date": "2026-10-20T12:00:00Z", "game_min": "2026.10.19.5638", "game_min_revision": 5638,
                "download": { "url": "https://example.test/rover.zip", "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "size": 1024, "content_type": "application/zip" },
                "install": { "derived": true, "folder": "{{folder}}" }, "install_size": 2048, "dependencies": [],
                "listing": { "name": "{{name}}", "authors": ["Maxi"], "abstract": "A small rover.", "license": "CC0-1.0", "tags": [], "links": {} }
            }]
        }] }
        """;

    private sealed record Upload(string FileName, long Size, IReadOnlyList<ListingDependency> Dependencies, string? ListingId);

    private sealed record Publish(string UploadId, string Version, string Status, ListingDraft? Listing);

    private sealed record ImageUpload(string ListingType, string ListingId, int Size);

    /// <summary>Gives a small valid PNG in place of the frame, and keeps what a test asks about that frame.</summary>
    private sealed class CoverEncoder : IPngEncoder
    {
        public static readonly byte[] Png = ListingEditorTests.Png(16, 16);

        public int Width { get; private set; }

        public int Height { get; private set; }

        public bool IsOpaque { get; private set; }

        public TaskCompletionSource? Reached { get; init; }

        /// <summary>Holds the encode, which comes after the last check of a cancel, until the test completes the task.</summary>
        public Task? Gate { get; init; }

        public byte[] Encode(FrameBuffer frame)
        {
            (Width, Height) = (frame.Width, frame.Height);
            IsOpaque = frame.Rgba.Where((_, index) => index % 4 == 3).All(alpha => alpha == 255);
            Reached?.TrySetResult();
            Gate?.GetAwaiter().GetResult();
            return Png;
        }
    }

    private sealed class PictureWindow : IWindowServices
    {
        public PickedBinaryFile? File { get; set; }

        public Exception? Failure { get; init; }

        /// <summary>Holds the answer of the picker until the test completes the task.</summary>
        public Task? Gate { get; init; }

        public Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string fileTypeName, string text) => Task.FromResult<string?>(null);

        public Task<string?> SaveFileAsync(string title, string suggestedFileName, string extension, string fileTypeName, Func<Stream, Task> write) => Task.FromResult<string?>(null);

        public Task<PickedTextFile?> OpenTextFileAsync(string title, string fileTypeName) => Task.FromResult<PickedTextFile?>(null);

        public async Task<PickedBinaryFile?> OpenImageFileAsync(string title, string fileTypeName, long maxBytes)
        {
            if (Gate is { } gate)
                await gate;

            return Failure is { } failure ? throw failure : File;
        }

        public Task CopyTextAsync(string text) => Task.CompletedTask;
    }

    private sealed record ListingChange(string Type, string Id, ShareListingChange Fields);

    private sealed record TakeDown(string Type, string Id, string Version);

    private sealed class FakeHost : IShareHostClient
    {
        private bool _signedIn;

        public int SignIns { get; private set; }

        public ConcurrentQueue<Upload> Uploads { get; } = new();

        public ConcurrentQueue<Publish> Publishes { get; } = new();

        public ConcurrentQueue<ImageUpload> ImageUploads { get; } = new();

        /// <summary>The answer to each picture in turn, where null takes it, and the host takes every picture after the queue.</summary>
        public Queue<ShareHostException?> ImageFailures { get; } = new();

        public TaskCompletionSource ImageStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Holds each picture until the test lets it through, for what the player does during the send.</summary>
        public TaskCompletionSource? ImageGate { get; set; }

        public ShareHostException? UploadFailure { get; set; }

        public ShareHostException? PublishFailure { get; init; }

        public ShareUpload Upload { get; set; } = Checked;

        public GitHubDeviceCode Code { get; init; } = new("ROVR-1234", "https://ksamods.gg/device");

        public Uri? PullRequest { get; init; } = new("https://github.com/KSAModding/content-index/pull/250");

        public string? PublishedId { get; init; } = "ExampleRover";

        public Uri? Page { get; init; }

        /// <summary>How many of the next requests the host answers with a refused token.</summary>
        public int TokenRefusals { get; set; }

        /// <summary>Runs after the code shows and before the sign-in ends, for what the player does in that time.</summary>
        public Func<Task>? DuringSignIn { get; set; }

        /// <summary>Runs after a request went out and before its answer.</summary>
        public Func<Task>? DuringSend { get; init; }

        public int SignOuts { get; private set; }

        public string? SignedOutOf { get; private set; }

        public bool IsSignedIn(ShareHost host) => _signedIn;

        public async Task SignInAsync(ShareHost host, IProgress<GitHubDeviceCode>? progress = null, bool keepSignedIn = false, CancellationToken cancellationToken = default)
        {
            SignIns++;
            progress?.Report(Code);
            if (DuringSignIn is { } during)
                await during();

            _signedIn = true;
        }

        public void SignOut(ShareHost host)
        {
            SignOuts++;
            SignedOutOf = host.Key;
            _signedIn = false;
        }

        public async Task<ShareUpload> UploadAsync(ShareHost host, Stream archive, string fileName, IReadOnlyList<ListingDependency> dependencies, string? listingId, CancellationToken cancellationToken = default)
        {
            Uploads.Enqueue(new Upload(fileName, archive.Length, dependencies, listingId));

            // The real client disposes the stream with its request, so a second send needs a new one.
            archive.Dispose();
            if (DuringSend is { } during)
                await during();

            return (RefuseToken() ?? UploadFailure) is { } failure ? throw failure : Upload;
        }

        public async Task<SharePublished> PublishAsync(ShareHost host, string uploadId, string version, string status, ListingDraft? listing, CancellationToken cancellationToken = default)
        {
            Publishes.Enqueue(new Publish(uploadId, version, status, listing));
            if (DuringSend is { } during)
                await during();

            return (RefuseToken() ?? PublishFailure) is { } failure ? throw failure : new SharePublished(PublishedId, version, PullRequest, Page);
        }

        public async Task UploadImageAsync(ShareHost host, string listingType, string listingId, ReadOnlyMemory<byte> image, CancellationToken cancellationToken = default)
        {
            ImageUploads.Enqueue(new ImageUpload(listingType, listingId, image.Length));
            ImageStarted.TrySetResult();
            if (ImageGate is { } gate)
                await gate.Task.WaitAsync(cancellationToken);

            if (RefuseToken() is { } refused)
                throw refused;

            if (ImageFailures.TryDequeue(out var failure) && failure is not null)
                throw failure;
        }

        public ConcurrentQueue<ListingChange> Changes { get; } = new();

        public ConcurrentQueue<TakeDown> TakeDowns { get; } = new();

        public ShareHostException? ManageFailure { get; set; }

        public async Task<ShareListingChanged> ChangeListingAsync(ShareHost host, string type, string id, ShareListingChange change, CancellationToken cancellationToken = default)
        {
            Changes.Enqueue(new ListingChange(type, id, change));
            if (DuringSend is { } during)
                await during();

            return (RefuseToken() ?? ManageFailure) is { } failure ? throw failure : new ShareListingChanged(PullRequest, Page);
        }

        public async Task TakeDownReleaseAsync(ShareHost host, string type, string id, string version, CancellationToken cancellationToken = default)
        {
            TakeDowns.Enqueue(new TakeDown(type, id, version));
            if (DuringSend is { } during)
                await during();

            if ((RefuseToken() ?? ManageFailure) is { } failure)
                throw failure;
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
