using System.Security.Cryptography;
using Borea.App.ViewModels;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using static Borea.App.Tests.ViewModels.PackViewModelTests;
using static Borea.App.Tests.ViewModels.UserContentFixtures;

namespace Borea.App.Tests.ViewModels;

/// <summary>The pinned vehicles and saves of a pack on the pack page and in a pack install or update (RFC 0085).</summary>
public sealed class PackContentViewModelTests
{
    private const string MeasureToolsUrl = "https://github.com/Maximilian-Nesslauer/KSA-MeasureTools/releases/download/v1.1.10/MeasureTools.zip";
    private const string MeasureToolsSha256 = "8718558358629EFC3753ACFF9052851EFEB142A9343A1794485C177651265F15";

    [Fact]
    public async Task PackPage_ListsThePinnedVehicle_WithItsNewerRelease_AndCountsIt()
    {
        var rover = Archive(("Example Rover/meta.toml", "name = \"Example Rover\"\n"), ("Example Rover/vehicle.xml", "<Vehicle />"));
        using var harness = await ViewModelHarness.CreateAsync(
            editSnapshot: WithPacks(PackWithContent("rover-pack", "Rover Pack", "1.0.0", Pin("KSArmory", "0.8.44"), vehicles: [Pin("ExampleRover", "1.0.0")])),
            typeFiles: TypeFiles(ContentIndexTypeFiles.Vehicles, Entry("ExampleRover", "vehicle", "Example Rover", RoverUrl, rover, "1.0.0", "1.1.0")));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);

        await viewModel.OpenPackAsync(pack);

        var row = Assert.Single(viewModel.PackVehicles);
        Assert.Empty(viewModel.PackSaves);
        Assert.Equal(("Example Rover", "1.0.0", false), (row.Name, row.Version, row.IsInstalled));
        Assert.Equal(harness.Localization.FormatPackMemberNewer("1.1.0"), row.NewerText);
        Assert.Equal(harness.Localization.FormatPackNewerItems(1, 2), pack.NewerReleasesText);
    }

    [Fact]
    public async Task PackInstall_PinnedVehicleWhoseNameIsTaken_AsksForANameAndInstallsUnderIt()
    {
        using var harness = await RoverPackHarnessAsync();
        var viewModel = harness.ViewModel;
        var instance = await ActivateAsync(harness);
        var mine = Directory.CreateDirectory(Path.Combine(harness.Services.Paths.GetInstanceVehiclesFolder(instance.InstanceId), "Example Rover")).FullName;
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);

        await pack.InstallCommand.ExecuteAsync(null);

        var taken = harness.Localization.FormatPackContentNameTaken("Example Rover", "Example Rover", harness.Localization.InstanceGroupVehicles);
        Assert.True(viewModel.IsNameModalOpen);
        Assert.True(viewModel.ShowsGameSaveNameRules);
        Assert.Equal(harness.Localization.FormatPackContentNameTitle("Example Rover"), viewModel.NameModalTitle);
        Assert.Equal(taken, viewModel.NameModalExplanation);
        Assert.Equal(harness.Localization.FormatPackContentSkip("Example Rover"), viewModel.NameModalSkipText);
        Assert.Equal(harness.Localization.PackContentNameConfirm, viewModel.NameModalConfirmText);
        Assert.Equal("Example Rover 2", viewModel.ModalInstanceName);
        Assert.Contains(taken, pack.InstallWarning);

        viewModel.ModalInstanceName = "Rover/2";
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsNameModalOpen);
        Assert.NotNull(viewModel.InstanceError);

        viewModel.ModalInstanceName = "Rover 2";
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsNameModalOpen);
        Assert.DoesNotContain(taken, pack.InstallWarning ?? string.Empty);
        await pack.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Null(pack.InstallError);
        Assert.All(pack.Results, result => Assert.Equal(ModPackMemberStatus.Installed, result.Status));
        Assert.Equal(["ExampleRover", "MeasureTools"], pack.Results.Select(result => result.ModId).Order());
        var installed = Path.Combine(harness.Services.Paths.GetInstanceVehiclesFolder(instance.InstanceId), "Rover 2");
        Assert.StartsWith("name = \"Rover 2\"", File.ReadAllText(Path.Combine(installed, "meta.toml")), StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(mine));
    }

    [Fact]
    public async Task PackInstall_GoingOnWithoutATakenItem_IsASkipAndNoError_AndTheNameCanBeChosenAgain()
    {
        using var harness = await RoverPackHarnessAsync();
        var viewModel = harness.ViewModel;
        var instance = await ActivateAsync(harness);
        Directory.CreateDirectory(Path.Combine(harness.Services.Paths.GetInstanceVehiclesFolder(instance.InstanceId), "Example Rover"));
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);

        await pack.InstallCommand.ExecuteAsync(null);
        viewModel.CancelNameModalCommand.Execute(null);

        Assert.False(viewModel.IsNameModalOpen);
        Assert.True(pack.IsConfirmingInstall);
        Assert.True(pack.CanChooseContentName);
        pack.ChooseContentNameCommand.Execute(null);
        Assert.True(viewModel.IsNameModalOpen);
        viewModel.CancelNameModalCommand.Execute(null);

        await pack.ConfirmInstallCommand.ExecuteAsync(null);

        var shortReason = harness.Localization.FormatPackContentNameTakenShort("Example Rover");
        Assert.Null(pack.InstallError);
        Assert.Equal(harness.Localization.FormatPackContentSkipped("Example Rover", "1.0.0", shortReason), pack.InstallNote);
        var skipped = Assert.Single(pack.Results, result => result.ModId == "ExampleRover");
        Assert.Equal((true, false, harness.Localization.PackResultSkipped, shortReason), (skipped.IsSkipped, skipped.IsProblem, skipped.StatusText, skipped.Message));
        Assert.Equal(ModPackMemberStatus.Installed, Assert.Single(pack.Results, result => result.ModId == "MeasureTools").Status);
    }

    [Theory]
    [InlineData("Campaign", "Campaign 2")]
    [InlineData("Campaign 2", "Campaign 3")]
    [InlineData("Apollo 11", "Apollo 12")]
    public void FreeNameSuggestion_CountsOn(string taken, string suggestion) =>
        Assert.Equal(suggestion, MainViewModel.FreeNameSuggestion(taken));

    [Fact]
    public async Task PackUpdate_ChangedPinnedSave_NamesTheFilesFirst_AndReplacesItOnlyAfterTheGo()
    {
        var first = SaveArchive("<Universe />");
        var second = SaveArchive("<Universe version=\"2\" />");
        var served = first;
        var packs = Pack("tools-pack", "Tools Pack", Version("1.0.0", Pin("KSArmory", "0.8.44")), Version("1.1.0", Pin("KSArmory", "0.8.44")));
        var last = packs.LastIndexOf("\"mods\": [", StringComparison.Ordinal);
        packs = packs.Insert(last, "\"saves\": [{ \"id\": \"CampaignSave\", \"version\": \"2.0.0\" }], ");
        using var harness = await ViewModelHarness.CreateAsync(
            respond: request => request.RequestUri?.AbsoluteUri == SaveUrl ? ArchiveResponse(served) : null,
            editSnapshot: WithPacks(packs),
            typeFiles: TypeFiles(ContentIndexTypeFiles.Saves, SaveEntry(("1.0.0", first), ("2.0.0", second))));
        var viewModel = harness.ViewModel;
        var instance = await OpenPackInstanceAsync(harness);
        var oldRelease = (await harness.Services.UserContent.GetReleaseAsync("CampaignSave", ModVersion.Parse("1.0.0")))!;
        Assert.Equal(Borea.Core.UserContent.UserContentOutcome.Installed, (await harness.Services.UserContentInstaller.InstallAsync(instance.InstanceId, oldRelease, name: null)).Outcome);
        var universe = Path.Combine(harness.Services.Paths.GetInstanceSavesFolder(instance.InstanceId), "Campaign", "universe.xml");
        File.WriteAllText(universe, "<Universe played=\"yes\" />");
        served = second;

        await viewModel.PackUpdate!.UpdateCommand.ExecuteAsync(null);

        var update = viewModel.PackUpdate!;
        Assert.Equal([harness.Localization.FormatPackContentChanged("Campaign", "universe.xml")], update.PendingQuestions);
        Assert.DoesNotContain(harness.Localization.FormatPackContentChanged("Campaign", "universe.xml"), update.InstallWarning ?? string.Empty);
        Assert.Equal([harness.Localization.FormatPackUpdateChange("Campaign", "1.0.0", "2.0.0")], update.ChangeTexts);
        Assert.StartsWith(harness.Localization.UpdateAnyway, update.ConfirmText, StringComparison.Ordinal);
        Assert.Equal("<Universe played=\"yes\" />", File.ReadAllText(universe));

        await update.ConfirmUpdateCommand.ExecuteAsync(null);

        Assert.Equal("<Universe version=\"2\" />", File.ReadAllText(universe));
        Assert.Equal(ModVersion.Parse("2.0.0"), Assert.Single(await harness.Services.InstalledContent.ListAsync(instance.InstanceId)).Version);
    }

    [Fact]
    public async Task UpdateCount_CountsARecordedVehicleWithANewerReleaseAndLeavesItAlone()
    {
        var rover = Archive(("Example Rover/meta.toml", "name = \"Example Rover\"\n"), ("Example Rover/vehicle.xml", "<Vehicle />"));
        using var harness = await ViewModelHarness.CreateAsync(
            respond: request => request.RequestUri?.AbsoluteUri == RoverUrl ? ArchiveResponse(rover) : null,
            typeFiles: TypeFiles(ContentIndexTypeFiles.Vehicles, Entry("ExampleRover", "vehicle", "Example Rover", RoverUrl, rover, "1.0.0", "1.1.0")));
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea, version: "0.7.4");
        var release = (await harness.Services.UserContent.GetReleaseAsync("ExampleRover", ModVersion.Parse("1.0.0")))!;
        await harness.Services.UserContentInstaller.InstallAsync(instance.InstanceId, release, name: null);

        await viewModel.LoadAsync();
        await viewModel.WhenContentUpdatesCheckedAsync();

        Assert.Equal(2, viewModel.ActiveInstanceUpdateCount);
        Assert.Equal(ModVersion.Parse("1.0.0"), Assert.Single(await harness.Services.InstalledContent.ListAsync(instance.InstanceId)).Version);
    }

    [Fact]
    public async Task MakePack_TakesAnUnchangedRecordedVehicle_AndNamesAChangedOne()
    {
        const string BuggyUrl = "https://example.test/buggy.zip";
        var rover = Archive(("Example Rover/meta.toml", "name = \"Example Rover\"\n"), ("Example Rover/vehicle.xml", "<Vehicle />"));
        var buggy = Archive(("Buggy/meta.toml", "name = \"Buggy\"\n"), ("Buggy/vehicle.xml", "<Vehicle />"));
        using var harness = await ViewModelHarness.CreateAsync(
            respond: request => request.RequestUri?.AbsoluteUri switch
            {
                RoverUrl => ArchiveResponse(rover),
                BuggyUrl => ArchiveResponse(buggy),
                _ => null,
            },
            typeFiles: TypeFiles(ContentIndexTypeFiles.Vehicles, Entry("ExampleRover", "vehicle", "Example Rover", RoverUrl, rover, "1.0.0") + ", " + Entry("Buggy", "vehicle", "Buggy", BuggyUrl, buggy, "1.0.0")));
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea);
        foreach (var id in new[] { "ExampleRover", "Buggy" })
            await harness.Services.UserContentInstaller.InstallAsync(instance.InstanceId, (await harness.Services.UserContent.GetReleaseAsync(id, ModVersion.Parse("1.0.0")))!, name: null);
        File.WriteAllText(Path.Combine(harness.Services.Paths.GetInstanceVehiclesFolder(instance.InstanceId), "Buggy", "vehicle.xml"), "<Vehicle edited=\"yes\" />");
        await viewModel.LoadAsync();

        await viewModel.Instances.Single().MakePackCommand.ExecuteAsync(null);

        var editor = viewModel.ListingEditor;
        Assert.Equal(["AdvancedFlightComputer"], editor.Draft.Mods.Select(member => member.Id));
        Assert.Equal([new Borea.Core.Listings.ListingPackMember("ExampleRover", "1.0.0") { Type = ContentType.Vehicle }], editor.Draft.Vehicles);
        Assert.Contains(harness.Localization.FormatListingLeftOutChanged("Buggy", "1.0.0"), editor.LeftOut);
        Assert.Equal(harness.Localization.ListingMemberVehicle, editor.Members.Single(row => row.Id == "ExampleRover").TypeText);
    }

    [Fact]
    public async Task PackEditor_VehicleType_OffersListedVehicles_AndPinsThemUnderVehicles()
    {
        var rover = Archive(("Example Rover/meta.toml", "name = \"Example Rover\"\n"), ("Example Rover/vehicle.xml", "<Vehicle />"));
        using var harness = await ViewModelHarness.CreateAsync(
            typeFiles: TypeFiles(ContentIndexTypeFiles.Vehicles, Entry("ExampleRover", "vehicle", "Example Rover", RoverUrl, rover, "1.0.0")));
        var viewModel = harness.ViewModel;
        await viewModel.OpenListingAsync();
        var editor = viewModel.ListingEditor;
        editor.StartPackCommand.Execute(null);

        editor.MemberQuery = "rover";
        Assert.Empty(editor.MemberMatches);
        editor.ShowMemberTypeCommand.Execute(ContentType.Vehicle);
        Assert.Equal(["ExampleRover"], editor.MemberMatches.Select(match => match.Id));
        Assert.Equal((harness.Localization.ListingSearchVehicles, harness.Localization.ListingNoListedVehicleMatch), (editor.MemberSearchText, editor.MemberNoMatchText));
        editor.AddMemberCommand.Execute(null);

        Assert.True(editor.IsMemberTypeVehicles);
        Assert.Empty(editor.Draft.Mods);
        Assert.Equal("ExampleRover", Assert.Single(editor.Draft.Vehicles).Id);
        Assert.Equal("ExampleRover", ((Borea.Core.Listings.AuthoredTable)editor.Draft.ToDocument().GetList("vehicles")![0]).GetString("id"));
    }

    /// <summary>A pack with one mod and the vehicle Example Rover, both served.</summary>
    private static Task<ViewModelHarness> RoverPackHarnessAsync()
    {
        var rover = Archive(("Example Rover/meta.toml", "name = \"Example Rover\"\n"), ("Example Rover/vehicle.xml", "<Vehicle />"));
        var tools = Archive(("MeasureTools/mod.toml", "name = \"MeasureTools\""));
        return ViewModelHarness.CreateAsync(
            respond: request => request.RequestUri?.AbsoluteUri switch
            {
                RoverUrl => ArchiveResponse(rover),
                MeasureToolsUrl => ArchiveResponse(tools),
                _ => null,
            },
            editSnapshot: snapshot => WithPacks(PackWithContent("rover-pack", "Rover Pack", "1.0.0", Pin("MeasureTools", "1.1.10"), vehicles: [Pin("ExampleRover", "1.0.0")]))(
                snapshot.Replace(MeasureToolsSha256, Convert.ToHexString(SHA256.HashData(tools)), StringComparison.Ordinal)
                    .Replace("\"size\": 41782", $"\"size\": {tools.Length}", StringComparison.Ordinal)),
            typeFiles: TypeFiles(ContentIndexTypeFiles.Vehicles, Entry("ExampleRover", "vehicle", "Example Rover", RoverUrl, rover, "1.0.0")));
    }

    private static async Task<Instance> ActivateAsync(ViewModelHarness harness)
    {
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        return instance;
    }

    private static async Task<Instance> OpenPackInstanceAsync(ViewModelHarness harness)
    {
        var release = (await harness.Services.Mods.GetReleaseAsync("KSArmory", ModVersion.Parse("0.8.44")))!;
        var installed = new InstalledMod("KSArmory", release.Version, InstallReason.ModPack, DateTimeOffset.UnixEpoch, release, ownershipToken: "token");
        var instance = Instance.FromExisting(Guid.NewGuid(), "Tools", new InstanceSource.FromModPack("tools-pack", ModVersion.Parse("1.0.0")), DateTimeOffset.UnixEpoch, [installed], false);
        await harness.Services.Instances.CreateAsync(instance);
        Directory.CreateDirectory(harness.Services.Paths.GetInstanceRoot(instance.InstanceId));
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        await harness.ViewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        return instance;
    }

    /// <param name="mod">The one mod pin, because a pack pins at least one mod.</param>
    private static string PackWithContent(string id, string name, string version, string mod, IReadOnlyList<string> vehicles) =>
        Pack(id, name, Version(version, mod))
            .Replace("\"mods\": [", $"\"vehicles\": [{string.Join(", ", vehicles)}], \"mods\": [", StringComparison.Ordinal);
}
