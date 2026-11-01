using System.Text.Json.Nodes;
using Borea.App.ViewModels;
using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

public sealed class ConflictDialogTests
{
    private const string Afc = "AdvancedFlightComputer";
    private const string Armory = "KSArmory";
    private const string Tools = "MeasureTools";

    /// <summary>Gives every release of <paramref name="declarer"/> a conflict entry, so that no older release lets the planner go around it.</summary>
    internal static Func<string, string> Declares(string declarer, string named, string bounds = "") => Entry(declarer, $$"""{ "id": "{{named}}", "kind": "conflict", {{bounds}} "source": "authored" }""");

    private static Func<string, string> Requires(string owner, string needed) => Entry(owner, $$"""{ "id": "{{needed}}", "kind": "required", "source": "authored" }""");

    private static Func<string, string> Entry(string modId, string entry) => json =>
    {
        var root = JsonNode.Parse(json)!;
        var listing = root["listings"]!.AsArray().Single(node => (string?)node!["id"] == modId)!;
        foreach (var release in listing["releases"]!.AsArray())
        {
            var dependencies = release!["dependencies"]?.AsArray() ?? [];
            dependencies.Add(JsonNode.Parse(entry));
            release["dependencies"] = dependencies.DeepClone();
        }

        return root.ToJsonString();
    };

    private static async Task<DiscoverItem> AfcRowAsync(ViewModelHarness harness)
    {
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        return harness.ViewModel.DiscoverItems.Single(row => row.ModId == Afc);
    }

    private static async Task<ConflictRow> InstallAfcAsync(ViewModelHarness harness)
    {
        var row = await AfcRowAsync(harness);
        await row.InstallCommand.ExecuteAsync(null);
        return Assert.Single(harness.ViewModel.ConflictDialog!.Rows);
    }

    private static void AssertOnlyLinks(ViewModelHarness harness, ConflictRow row, string? reason)
    {
        Assert.Equal(reason, row.ReasonText);
        Assert.False(row.CanRemove);
        Assert.Equal(harness.Localization.FormatConflictOpenPage(harness.ViewModel.ContentName(Armory)), row.OpenPageText);
    }

    [Fact]
    public async Task Install_ConflictWithAModBoreaInstalled_NamesBothAndRemovingThatModPlansAgain()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: Declares(Afc, Armory));
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, Armory, activate: true, ownership: ModInstallOwnership.Borea);
        var row = await AfcRowAsync(harness);

        await row.InstallCommand.ExecuteAsync(null);

        Assert.NotNull(row.InstallError);
        Assert.True(viewModel.IsConflictOpen);
        var conflict = Assert.Single(viewModel.ConflictDialog!.Rows);
        Assert.Equal(harness.Localization.FormatConflictDeclared(viewModel.ContentName(Afc), viewModel.ContentName(Armory)), conflict.Text);
        Assert.Null(conflict.ReasonText);
        Assert.Equal(harness.Localization.FormatConflictRemove(viewModel.ContentName(Armory)), conflict.RemoveText);
        Assert.False(conflict.CanOpenPage);
        Assert.True(viewModel.ConflictDialog.ShowsAgainNote);
        Assert.Empty(viewModel.Toasts.Items);

        await conflict.RemoveCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsConflictOpen);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
        Assert.Null(row.InstallError);
        Assert.True(row.PendingPlan is { IsReady: true });
    }

    [Fact]
    public async Task Install_ConflictsWithTwoInstalledMods_MakesOnlyTheFirstRemovalThePrimaryButton()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => Declares(Afc, Armory)(Declares(Afc, Tools)(json)));
        var instance = await InstalledContent.AddAsync(harness, Armory, activate: true, ownership: ModInstallOwnership.Borea);
        await InstalledContent.AddAsync(harness, Tools, activate: false, ownership: ModInstallOwnership.Borea, into: instance);
        var row = await AfcRowAsync(harness);

        await row.InstallCommand.ExecuteAsync(null);

        var rows = harness.ViewModel.ConflictDialog!.Rows;
        Assert.Equal(2, rows.Count);
        Assert.Equal([(true, false), (false, true)], rows.Select(conflict => (conflict.ShowsPrimaryRemove, conflict.ShowsSecondaryRemove)));
    }

    [Fact]
    public async Task Install_AnInstalledModDeclaresTheConflict_NamesThatModAndOffersToRemoveIt()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: Declares(Armory, Afc));
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, Armory, activate: true, ownership: ModInstallOwnership.Borea);

        var conflict = await InstallAfcAsync(harness);

        Assert.Equal(harness.Localization.FormatConflictDeclared(viewModel.ContentName(Armory), viewModel.ContentName(Afc)), conflict.Text);
        Assert.Equal(harness.Localization.FormatConflictRemove(viewModel.ContentName(Armory)), conflict.RemoveText);
    }

    [Fact]
    public async Task Install_BothModsDeclareTheConflict_ShowsOneRow()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => Declares(Afc, Armory)(Declares(Armory, Afc)(json)));
        await InstalledContent.AddAsync(harness, Armory, activate: true, ownership: ModInstallOwnership.Borea);
        var row = await AfcRowAsync(harness);

        await row.InstallCommand.ExecuteAsync(null);

        Assert.Single(harness.ViewModel.ConflictDialog!.Rows);
    }

    [Fact]
    public async Task Install_AnotherInstalledModNeedsTheOtherMod_LinksToItsPage()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => Declares(Afc, Armory)(Requires(Tools, Armory)(json)));
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, Armory, activate: true, ownership: ModInstallOwnership.Borea);
        await InstalledContent.AddAsync(harness, Tools, activate: true, ownership: ModInstallOwnership.Borea);

        var conflict = await InstallAfcAsync(harness);

        AssertOnlyLinks(harness, conflict, harness.Localization.FormatContentRemoveRequired(viewModel.ContentName(Armory), viewModel.ContentName(Tools)));

        await conflict.OpenPageCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsConflictOpen);
        Assert.Equal(Armory, viewModel.SelectedContent!.ModId);
    }

    [Fact]
    public async Task Install_AModOfThePlanNeedsTheOtherMod_LinksToItsPage()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => Declares(Afc, Armory)(Requires(Afc, Tools)(Requires(Tools, Armory)(json))));
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, Armory, activate: true, ownership: ModInstallOwnership.Borea);

        var conflict = await InstallAfcAsync(harness);

        AssertOnlyLinks(harness, conflict, harness.Localization.FormatContentRemoveRequired(viewModel.ContentName(Armory), viewModel.ContentName(Tools)));
    }

    [Fact]
    public async Task Install_ARecordedVehicleNeedsTheOtherMod_LinksToItsPage()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: Declares(Afc, Armory));
        var instance = await InstalledContent.AddAsync(harness, Armory, activate: true, ownership: ModInstallOwnership.Borea);
        var vehicle = new ModVersionMetadata(1, "ExampleRover", ModVersion.Parse("1.0.0"), ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, "2026.9.7.5402", 5402,
            new DownloadInfo("https://example.invalid/rover.zip", new string('A', 64), null, "application/zip"), null, [new ModDependency(Armory, ModDependencyKind.Required)], type: ContentType.Vehicle, install: new InstallInfo("Rover", derived: true, folder: "Rover"));
        await harness.Services.InstalledContent.AddAsync(instance.InstanceId, new InstalledContentRecord(vehicle, "Rover", DateTimeOffset.UnixEpoch, new Dictionary<string, string>()));

        var conflict = await InstallAfcAsync(harness);

        AssertOnlyLinks(harness, conflict, harness.Localization.FormatContentRemoveRequired(harness.ViewModel.ContentName(Armory), "Rover"));
    }

    [Fact]
    public async Task Install_TheOtherModBelongsToThePack_LinksToItsPage()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: Declares(Afc, Armory));
        await InstalledContent.AddAsync(harness, Armory, activate: true, InstallReason.ModPack, ModInstallOwnership.Borea);

        var conflict = await InstallAfcAsync(harness);

        AssertOnlyLinks(harness, conflict, harness.Localization.FormatConflictPackMember(harness.ViewModel.ContentName(Armory)));
    }

    [Fact]
    public async Task Install_BoreaDidNotInstallTheOtherMod_LinksToItsPage()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: Declares(Afc, Armory));
        await InstalledContent.AddAsync(harness, Armory, activate: true);

        var conflict = await InstallAfcAsync(harness);

        AssertOnlyLinks(harness, conflict, harness.Localization.FormatContentRemoveNotOwned(harness.ViewModel.ContentName(Armory)));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(""" "max": "9.0.0", """, true)]
    public async Task Install_TheOtherModIsAFolderBoreaDoesNotKnow_SaysWhatItCannotRead(string bounds, bool versionUnknown)
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: Declares(Afc, Armory, bounds));
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        var folder = Directory.CreateDirectory(Path.Combine(harness.Services.Paths.GetInstanceModsFolder(instance.InstanceId), Armory));
        await File.WriteAllTextAsync(Path.Combine(folder.FullName, "mod.toml"), $"name = \"{Armory}\"");
        await harness.Services.ForeignModAdopter.ScanAsync(instance.InstanceId);

        var conflict = await InstallAfcAsync(harness);

        var name = harness.ViewModel.ContentName(Armory);
        Assert.Equal(harness.Localization.FormatConflictDeclared(harness.ViewModel.ContentName(Afc), name), conflict.Text);
        AssertOnlyLinks(harness, conflict, versionUnknown ? harness.Localization.FormatConflictVersionUnknown(name) : harness.Localization.FormatContentRemoveNotOwned(name));
    }

    [Fact]
    public async Task Import_ModsOfTheListConflict_LinksToThePageAndClosesThePlan()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: Declares(Tools, Armory));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var text = harness.Services.ModListFormat.Write(new ModList("Shared", [new ModListEntry(Armory, ModVersion.Parse("0.8.44"), enabled: true), new ModListEntry(Tools, ModVersion.Parse("1.1.10"), enabled: true)]));

        await viewModel.BeginImportAsync("shared.toml", text);

        var conflict = Assert.Single(viewModel.ConflictDialog!.Rows);
        Assert.NotNull(viewModel.ModListImport!.PlanError);
        AssertOnlyLinks(harness, conflict, reason: null);

        await conflict.OpenPageCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsConflictOpen);
        Assert.Null(viewModel.ModListImport);
        Assert.Equal(Armory, viewModel.SelectedContent!.ModId);
    }

    [Fact]
    public async Task InstallVehicle_ThatConflictsWithAnInstalledMod_ShowsTheModalAndPlansAgainAfterTheRemoval()
    {
        using var harness = await UserContentViewModelTests.VehicleHarnessAsync(dependencies: $$"""{ "id": "{{Armory}}", "kind": "conflict", "source": "authored" }""");
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, Armory, activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);
        var row = Assert.Single(viewModel.DiscoverItems);

        await row.InstallCommand.ExecuteAsync(null);

        Assert.NotNull(row.InstallError);
        var conflict = Assert.Single(viewModel.ConflictDialog!.Rows);
        Assert.Equal(harness.Localization.FormatConflictRemove(viewModel.ContentName(Armory)), conflict.RemoveText);

        await conflict.RemoveCommand.ExecuteAsync(null);

        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
        Assert.Null(row.InstallError);
        Assert.False(viewModel.IsConflictOpen);
    }

    [Fact]
    public async Task Update_ThatStopsOnAConflict_RunsAgainOnTheRowOfTheReloadedPage()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: Declares(Armory, Tools));
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, Armory, activate: true, ownership: ModInstallOwnership.Borea);
        await InstalledContent.AddAsync(harness, Tools, activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        await viewModel.Instances.Single().OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.SelectMany(group => group.Items).Single(item => item.ModId == Armory);

        await viewModel.UpdateContentAsync(row);
        await Assert.Single(viewModel.ConflictDialog!.Rows).RemoveCommand.ExecuteAsync(null);

        Assert.Equal([Armory], (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods.Select(mod => mod.ModId));
        var shown = viewModel.ContentGroups.SelectMany(group => group.Items).Single();
        Assert.NotSame(row, shown);
        Assert.Null(shown.InstallError);
        Assert.True(shown.PendingPlan is { IsReady: true });
    }

    [Fact]
    public async Task Update_WhileThePageShowsAnotherInstance_RunsAgainOnTheInstanceItStartedIn()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: Declares(Armory, Tools));
        var viewModel = harness.ViewModel;
        var first = await InstalledContent.AddAsync(harness, Armory, activate: true, ownership: ModInstallOwnership.Borea);
        await InstalledContent.AddAsync(harness, Tools, activate: true, ownership: ModInstallOwnership.Borea);
        var second = (await harness.Services.Instances.CreateAsync("Second", InstanceSource.Custom.Value)).Instance;
        await InstalledContent.AddAsync(harness, Armory, activate: false, ownership: ModInstallOwnership.Borea, into: second);
        await viewModel.LoadAsync();
        await viewModel.Instances.Single(item => item.InstanceId == first.InstanceId).OpenCommand.ExecuteAsync(null);
        var row = viewModel.ContentGroups.SelectMany(group => group.Items).Single(item => item.ModId == Armory);

        await viewModel.UpdateContentAsync(row);
        await viewModel.Instances.Single(item => item.InstanceId == second.InstanceId).OpenCommand.ExecuteAsync(null);
        await Assert.Single(viewModel.ConflictDialog!.Rows).RemoveCommand.ExecuteAsync(null);

        Assert.Equal([Armory], (await harness.Services.Instances.GetByIdAsync(first.InstanceId))!.Mods.Select(mod => mod.ModId));
        Assert.True(row.PendingPlan is { IsReady: true });
        Assert.Null(viewModel.ContentGroups.SelectMany(group => group.Items).Single().PendingPlan);
    }

    [Fact]
    public async Task UpdateRecordedVehicle_ThatStopsOnAConflict_PlansAgainOnTheRowOfTheReloadedPage()
    {
        using var harness = await UserContentViewModelTests.VehicleHarnessAsync(dependencies: $$"""{ "id": "{{Armory}}", "kind": "conflict", "source": "authored" }""");
        var viewModel = harness.ViewModel;
        await UserContentViewModelTests.ActiveInstanceWithRoverAsync(harness);
        await InstalledContent.AddAsync(harness, Armory, activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = Assert.Single(viewModel.VehiclesSection.Items);

        await row.UpdateCommand.ExecuteAsync(null);
        await Assert.Single(viewModel.ConflictDialog!.Rows).RemoveCommand.ExecuteAsync(null);

        var shown = Assert.Single(viewModel.VehiclesSection.Items);
        Assert.NotSame(row, shown);
        Assert.True(shown.IsConfirmingInstall);
    }
}
