using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Borea.App.ViewModels;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Storage.Listings;

namespace Borea.App.Tests.ViewModels;

/// <summary>Share on a vehicle or a save of the instance page, which opens the listing editor with what Borea read (RFC 0085).</summary>
public sealed class GameSaveShareTests
{
    private const string SchemaUrl = "https://raw.githubusercontent.com/KSAModding/content-index/main/schemas/authored.schema.json";

    private const string Vehicle = """
        <?xml version="1.0" encoding="utf-8"?>
        <VehicleSaveData Id="Example Rover">
          <RootPartRef InstanceOf="RoverWheel">
            <PartRef InstanceOf="UnknownAntenna" />
          </RootPartRef>
        </VehicleSaveData>
        """;

    [Fact]
    public async Task GatedSchema_OffersNoShare()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: Gated);
        await CreateInstanceAsync(harness, "Example Rover");

        await OpenAsync(harness);

        Assert.False(harness.ViewModel.VehiclesSection.CanShare);
        Assert.False(harness.ViewModel.SavesSection.CanShare);
        var item = Assert.Single(harness.ViewModel.VehiclesSection.Items);
        Assert.False(item.CanShare);
        Assert.False(item.ShareCommand.CanExecute(null));
    }

    [Fact]
    public async Task SchemaWithoutTheGate_OffersShareOnVehiclesAndSaves()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await CreateInstanceAsync(harness, "Example Rover");

        await OpenAsync(harness);

        Assert.True(harness.ViewModel.VehiclesSection.CanShare);
        Assert.True(harness.ViewModel.SavesSection.CanShare);
        Assert.True(Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.CanExecute(null));
    }

    [Fact]
    public async Task NameWithTwoSpaces_AsksForARenameFirst_AndACancelContinuesNothing()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await CreateInstanceAsync(harness, "Example  Rover");
        await OpenAsync(harness);
        var viewModel = harness.ViewModel;
        var item = Assert.Single(viewModel.VehiclesSection.Items);

        await item.ShareCommand.ExecuteAsync(null);

        Assert.Same(item, viewModel.RenamingGameSave);
        Assert.Equal("Example Rover", viewModel.ModalInstanceName);
        Assert.Equal(harness.Localization.FormatGameSaveShareRename("Example  Rover"), viewModel.GameSaveRenameReason);
        Assert.Null(viewModel.InstanceError);
        Assert.False(viewModel.CurrentWindowListing);
        Assert.False(viewModel.ListingEditor.HasShare);
        Assert.Equal(harness.Localization.GameSaveRenameContinue, viewModel.NameModalConfirmText);

        viewModel.CancelNameModalCommand.Execute(null);

        Assert.Null(viewModel.GameSaveRenameReason);

        item.BeginRenameCommand.Execute(null);
        Assert.Equal(harness.Localization.LibrarySave, viewModel.NameModalConfirmText);
        viewModel.ModalInstanceName = "Other Rover";
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);

        Assert.Null(viewModel.RenamingGameSave);
        Assert.False(viewModel.ListingEditor.HasShare);
    }

    [Theory]
    [InlineData("vehicle.xml", "<VehicleSaveData Id=", "vehicle.xml is damaged, so Borea cannot read it. Save it again in the game, then try again.")]
    [InlineData("meta.toml", "name = \"Example Rover\"\n", "The file does not say which game build wrote it, so the sharing site would refuse it. Load it in the game and save it again.")]
    public async Task FileThatStopsTheShare_ShowsUnderTheListInPlainWords(string file, string content, string expected)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await CreateInstanceAsync(harness, "Example Rover");
        await OpenAsync(harness);
        var item = Assert.Single(harness.ViewModel.VehiclesSection.Items);
        File.WriteAllText(Path.Combine(item.Entry.Path, file), content);

        await item.ShareCommand.ExecuteAsync(null);

        var editor = harness.ViewModel.ListingEditor;
        Assert.Equal(ShareFlowStep.Choose, editor.ShareFlowStep);
        Assert.Equal(expected, editor.ShareChooseError);
        Assert.Equal("Example Rover", editor.SelectedShareRow?.Item.Name);
        Assert.False(editor.HasShare);
        Assert.Empty(harness.ViewModel.Toasts.Items);
    }

    [Fact]
    public async Task Choose_ListsEveryInstance_WithTheActiveOneFirst_AndNextStartsTheShareOfTheRow()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var main = await CreateInstanceAsync(harness, "Example Rover");
        WriteItem(harness.Services.Paths.GetInstanceVehiclesFolder(main), "Second Rover", "vehicle.xml", Vehicle);
        var other = (await harness.Services.Instances.CreateAsync("Other", InstanceSource.Custom.Value)).Instance.InstanceId;
        WriteItem(harness.Services.Paths.GetInstanceVehiclesFolder(other), "Other Rover", "vehicle.xml", Vehicle);
        var later = (await harness.Services.Instances.CreateAsync("Later", InstanceSource.Custom.Value)).Instance.InstanceId;
        WriteItem(harness.Services.Paths.GetInstanceVehiclesFolder(later), "Later Rover", "vehicle.xml", Vehicle);
        await RecordPlayedAsync(harness, main, DateTimeOffset.UtcNow.AddDays(-6));
        await RecordPlayedAsync(harness, later, DateTimeOffset.UtcNow.AddDays(-1));
        await harness.Services.Instances.SetActiveInstanceAsync(other);
        await harness.ViewModel.LoadAsync();

        await harness.ViewModel.OpenShareFlowAsync(GameSaveKind.Vehicle);

        var editor = harness.ViewModel.ListingEditor;
        Assert.True(harness.ViewModel.CurrentWindowListing);
        Assert.Equal(["Other", "Later", "Main", null], editor.ShareChooseRows.Select(row => row.InstanceName));
        Assert.Equal([true, false, false, false], editor.ShareChooseRows.Select(row => row.IsFirstOfActiveInstance));
        Assert.Equal("Other Rover", editor.ShareChooseRows[0].Item.Name);
        Assert.Null(editor.SelectedShareRow);
        Assert.False(editor.CanContinueShareChoose);
        Assert.True(editor.ShowsShareChooseFirst);
        Assert.False(editor.ShowsPreviewCard);
        Assert.Equal([ListingStepState.Current, ListingStepState.ToDo, ListingStepState.ToDo, ListingStepState.ToDo], editor.ShareSteps.Select(step => step.State));

        var chosen = editor.ShareChooseRows[3];
        editor.SelectedShareRow = chosen;
        var load = editor.ShareChooseLoad;
        await editor.ContinueShareChooseCommand.ExecuteAsync(null);

        Assert.Equal(ShareFlowStep.Describe, editor.ShareFlowStep);
        Assert.Equal(harness.Localization.FormatShareItemIn(chosen.Item.Name, "Main"), editor.ShareItemText);
        Assert.Same(load, editor.ShareChooseLoad);
    }

    [Fact]
    public async Task RowClickedWhileMoreRowsLoad_StaysSelected()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = await CreateInstanceAsync(harness, "Example Rover");
        WriteItem(harness.Services.Paths.GetInstanceVehiclesFolder(instance), "Second Rover", "vehicle.xml", Vehicle);
        await harness.ViewModel.LoadAsync();
        var editor = harness.ViewModel.ListingEditor;
        editor.ShareChooseRows.CollectionChanged += (_, change) =>
        {
            if (change.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && editor.SelectedShareRow is null)
                editor.SelectedShareRow = (ShareChooseRow)change.NewItems![0]!;
        };

        await harness.ViewModel.OpenShareFlowAsync(GameSaveKind.Vehicle);

        Assert.Same(editor.ShareChooseRows[0], editor.SelectedShareRow);
    }

    [Fact]
    public async Task Choose_SelectsTheOnlyCandidate_AndAnInvalidNameShowsItsChip()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await CreateInstanceAsync(harness, "Example  Rover");
        await harness.ViewModel.LoadAsync();

        await harness.ViewModel.OpenShareFlowAsync(GameSaveKind.Vehicle);

        var editor = harness.ViewModel.ListingEditor;
        var row = Assert.Single(editor.ShareChooseRows);
        Assert.Same(row, editor.SelectedShareRow);
        Assert.True(row.NeedsNewName);
        Assert.Equal(harness.Localization.FormatGameSaveShareRename("Example  Rover"), row.NeedsNewNameTip);
        Assert.True(editor.CanContinueShareChoose);
        Assert.False(editor.ShowsShareChooseFirst);
    }

    [Theory]
    [InlineData(false, false, GameSaveKind.Vehicle, "Vehicles and saves live in an instance, and you have none yet.")]
    [InlineData(true, false, GameSaveKind.Save, "You have no saves to share yet. Start a game and save it, then come back.")]
    [InlineData(true, true, GameSaveKind.Vehicle, "Your game profile has vehicles from playing without Borea. Copy them into an instance first.")]
    public async Task Choose_WithNothingToList_SaysWhatToDo_AndTheProfileLeadsToTheInstance(bool hasInstance, bool hasProfileItems, GameSaveKind kind, string expected)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = hasInstance ? (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance.InstanceId : Guid.Empty;
        if (hasProfileItems)
            WriteItem(Path.Combine(harness.Services.Paths.GetSharedProfileRoot(), "vehicles"), "Profile Rover", "vehicle.xml", Vehicle);
        await harness.ViewModel.LoadAsync();

        await harness.ViewModel.OpenShareFlowAsync(kind);

        var editor = harness.ViewModel.ListingEditor;
        Assert.Empty(editor.ShareChooseRows);
        Assert.Equal(expected, editor.ShareChooseEmptyText);
        Assert.Equal(hasProfileItems ? instance : null, editor.ShareProfileInstance?.InstanceId);
        Assert.Equal(!hasInstance, editor.ShowsShareCreateInstance);
    }

    [Fact]
    public async Task Choose_WithAnErrorAndNoRows_ShowsOnlyTheError()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.OpenListingAsync();
        var editor = harness.ViewModel.ListingEditor;

        await editor.OpenShareChooseAsync(GameSaveKind.Vehicle, problem: "Could not read.");

        Assert.Equal("Could not read.", editor.ShareChooseError);
        Assert.Null(editor.ShareChooseEmptyText);
    }

    [Fact]
    public async Task EntryThatStopsOnTheList_KeepsTheWayBackToAFormDraft_UntilAShareStarts()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await CreateInstanceAsync(harness, "Example Rover");
        await OpenAsync(harness);
        var viewModel = harness.ViewModel;
        var editor = viewModel.ListingEditor;
        var item = Assert.Single(viewModel.VehiclesSection.Items);
        await viewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);
        editor.Name = "My Mod";
        harness.IsGameProcessRunning = () => true;

        await item.ShareCommand.ExecuteAsync(null);

        Assert.True(editor.IsShareStep);
        Assert.True(editor.HasFormBehindShareChoose);
        editor.BackToShareFormCommand.Execute(null);
        Assert.True(editor.IsFormStep);
        Assert.Equal("My Mod", editor.Name);
        Assert.False(editor.HasFormBehindShareChoose);

        await item.ShareCommand.ExecuteAsync(null);
        harness.IsGameProcessRunning = () => false;
        editor.CheckShareGameRunningCommand.Execute(null);
        await editor.ContinueShareChooseCommand.ExecuteAsync(null);

        Assert.Equal(ShareFlowStep.Describe, editor.ShareFlowStep);
        Assert.False(editor.HasFormBehindShareChoose);
    }

    [Fact]
    public async Task Choose_TheTypeSwitch_ShowsTheOtherType()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = await CreateInstanceAsync(harness, "Example Rover");
        WriteItem(harness.Services.Paths.GetInstanceSavesFolder(instance), "Moon Base", "universe.xml", "<UniverseData />");
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.OpenShareFlowAsync(GameSaveKind.Vehicle);
        var editor = harness.ViewModel.ListingEditor;

        editor.ShowShareChooseSavesCommand.Execute(null);
        await editor.ShareChooseLoad;

        Assert.True(editor.IsShareChooseSave);
        Assert.Equal("Moon Base", Assert.Single(editor.ShareChooseRows).Item.Name);
    }

    [Theory]
    [InlineData(false, false, "ExampleRover")]
    [InlineData(true, true, "Example Rover")]
    public async Task RowWithARecord_OpensTheList_WhereAnUnchangedCopyStopsNextAndAChangedOneNamesItsSource(bool isChanged, bool isListed, string source)
    {
        using var harness = isListed ? await ShareHostShareTests.ListedRoverHarnessAsync() : await ViewModelHarness.CreateAsync();
        var instance = await CreateInstanceAsync(harness, "Example Rover");
        var folder = Path.Combine(harness.Services.Paths.GetInstanceVehiclesFolder(instance), "Example Rover");
        await RecordAsync(harness, instance, folder);
        if (isChanged)
            File.WriteAllText(Path.Combine(folder, "vehicle.xml"), "<VehicleSaveData Id=\"Example Rover\" />");
        await OpenAsync(harness);

        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);

        var editor = harness.ViewModel.ListingEditor;
        await editor.ShareRowCompare;
        var text = harness.Localization;
        Assert.Equal(ShareFlowStep.Choose, editor.ShareFlowStep);
        var row = editor.SelectedShareRow;
        Assert.Equal(text.FormatSharePickFrom(source), row?.FromText);
        Assert.Equal(isChanged ? text.FormatShareChangedVehicle(source) : text.FormatShareUnchangedCopy(source), row?.NoteText);
        Assert.Equal(isChanged, editor.CanContinueShareChoose);
        Assert.False(editor.IsComparingShareRow);

        row!.IsUnchangedCopy = null;
        Assert.False(editor.CanContinueShareChoose);
        Assert.True(editor.IsComparingShareRow);
    }

    [Fact]
    public async Task RowBeforeTheIndexAnswered_StillFindsItsRecord()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = await CreateInstanceAsync(harness, "Example Rover");
        await RecordAsync(harness, instance, Path.Combine(harness.Services.Paths.GetInstanceVehiclesFolder(instance), "Example Rover"));
        await OpenAsync(harness);
        var viewModel = harness.ViewModel;
        var entry = Assert.Single(viewModel.VehiclesSection.Items).Entry;

        await new GameSaveItem(viewModel, viewModel.VehiclesSection, instance, entry, installed: null).ShareCommand.ExecuteAsync(null);

        Assert.True(viewModel.ListingEditor.IsShareStep);
        Assert.Equal(ShareFlowStep.Choose, viewModel.ListingEditor.ShareFlowStep);
        Assert.False(viewModel.ListingEditor.HasShare);
    }

    [Fact]
    public async Task RenameOfAChangedCopyFromTheList_GoesOnToDescribe()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = await CreateInstanceAsync(harness, "Example Rover");
        var folder = Path.Combine(harness.Services.Paths.GetInstanceVehiclesFolder(instance), "Example Rover");
        await RecordAsync(harness, instance, folder);
        File.WriteAllText(Path.Combine(folder, "meta.toml"), "name = \"Example  Rover\"\nversion = \"v2026.10.19.5638\"\n");
        await OpenAsync(harness);
        var viewModel = harness.ViewModel;
        var editor = viewModel.ListingEditor;

        await Assert.Single(viewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);
        await editor.ShareRowCompare;
        await editor.ContinueShareChooseCommand.ExecuteAsync(null);
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);

        Assert.True(editor.HasShare);
        Assert.Equal(ShareFlowStep.Describe, editor.ShareFlowStep);
    }

    [Fact]
    public async Task RunningGame_ShowsTheBannerAndStopsNext_UntilTheGameIsClosed()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await CreateInstanceAsync(harness, "Example Rover");
        await OpenAsync(harness);
        harness.IsGameProcessRunning = () => true;

        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);

        var editor = harness.ViewModel.ListingEditor;
        Assert.Equal(ShareFlowStep.Choose, editor.ShareFlowStep);
        Assert.True(editor.IsShareGameRunning);
        Assert.False(editor.CanContinueShareChoose);
        Assert.Empty(harness.ViewModel.Toasts.Items);

        harness.IsGameProcessRunning = () => false;
        editor.CheckShareGameRunningCommand.Execute(null);
        Assert.True(editor.CanContinueShareChoose);

        harness.IsGameProcessRunning = () => true;
        await editor.ContinueShareChooseCommand.ExecuteAsync(null);

        Assert.True(editor.IsShareGameRunning);
        Assert.Equal(ShareFlowStep.Choose, editor.ShareFlowStep);
    }

    [Fact]
    public async Task BackToChoose_KeepsTheDraftForTheSameItem_AndAnotherItemStartsANewShare()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = await CreateInstanceAsync(harness, "Example Rover");
        WriteItem(harness.Services.Paths.GetInstanceVehiclesFolder(instance), "Second Rover", "vehicle.xml", Vehicle);
        await OpenAsync(harness);
        await harness.ViewModel.VehiclesSection.Items.Single(item => item.Name == "Example Rover").ShareCommand.ExecuteAsync(null);
        var editor = harness.ViewModel.ListingEditor;
        editor.Authors = "Maxi";

        editor.BackToShareChooseCommand.Execute(null);
        await editor.ShareChooseLoad;

        Assert.Equal("Example Rover", editor.SelectedShareRow?.Item.Name);
        await editor.ContinueShareChooseCommand.ExecuteAsync(null);
        Assert.Equal(ShareFlowStep.Describe, editor.ShareFlowStep);
        Assert.Equal("Maxi", editor.Authors);

        editor.OpenShareStep("choose");
        await editor.ShareChooseLoad;
        editor.SelectedShareRow = editor.ShareChooseRows.Single(row => row.Item.Name == "Second Rover");
        await editor.ContinueShareChooseCommand.ExecuteAsync(null);

        Assert.Equal(("Second Rover", string.Empty), (editor.Name, editor.Authors));
    }

    [Fact]
    public async Task EntryWithNoItem_LeavesAShareOnTheWayAlone()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await CreateInstanceAsync(harness, "Example Rover");
        await OpenAsync(harness);
        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);
        var editor = harness.ViewModel.ListingEditor;
        editor.Authors = "Maxi";

        await harness.ViewModel.OpenShareFlowAsync(GameSaveKind.Save);

        Assert.Equal(ShareFlowStep.Describe, editor.ShareFlowStep);
        Assert.Equal("Maxi", editor.Authors);
    }

    [Fact]
    public async Task UnreadableModFile_IsNamedRelativeToTheInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = await CreateInstanceAsync(harness, "Example Rover");
        var mods = harness.Services.Paths.GetInstanceModsFolder(instance);
        File.WriteAllText(Path.Combine(mods, "ExamplePartsMod", "Parts.xml"), "<Assets><Part");
        await OpenAsync(harness);

        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);

        var relative = Path.GetRelativePath(harness.Services.Paths.GetInstanceRoot(instance), Path.Combine(mods, "ExamplePartsMod", "Parts.xml")).Replace('\\', '/');
        Assert.Contains(harness.Localization.FormatListingShareUnreadableFile(relative), harness.ViewModel.ListingEditor.ShareNotes);
        Assert.DoesNotContain(':', relative);
    }

    [Fact]
    public async Task NameChangedToAnInvalidNameAfterTheListLoaded_AsksForARenameFirst_AndTheShareGoesOnAfterIt()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await CreateInstanceAsync(harness, "Example Rover");
        await OpenAsync(harness);
        var viewModel = harness.ViewModel;
        var item = Assert.Single(viewModel.VehiclesSection.Items);
        File.WriteAllText(Path.Combine(item.Entry.Path, "meta.toml"), "name = \"Example  Rover\"\nversion = \"v2026.10.19.5638\"\n");

        await item.ShareCommand.ExecuteAsync(null);

        Assert.Same(item, viewModel.RenamingGameSave);
        Assert.Equal("Example Rover", viewModel.ModalInstanceName);
        Assert.Equal(harness.Localization.FormatGameSaveShareRename("Example  Rover"), viewModel.GameSaveRenameReason);
        Assert.False(viewModel.ListingEditor.HasShare);

        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);

        Assert.Null(viewModel.RenamingGameSave);
        Assert.Contains("name = \"Example Rover\"", File.ReadAllText(Path.Combine(item.Entry.Path, "meta.toml")), StringComparison.Ordinal);
        Assert.True(viewModel.CurrentWindowListing);
        Assert.True(viewModel.ListingEditor.HasShare);
        Assert.Equal(("Example Rover", "ExampleRover"), (viewModel.ListingEditor.Name, viewModel.ListingEditor.Id));
    }

    [Fact]
    public async Task Share_OpensTheEditorWithTheProposalAndKeepsAKindTheAuthorChanged()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await CreateInstanceAsync(harness, "Example Rover");
        await OpenAsync(harness);
        var viewModel = harness.ViewModel;

        await Assert.Single(viewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);

        var editor = viewModel.ListingEditor;
        Assert.True(viewModel.CurrentWindowListing);
        Assert.True(editor.HasShare);
        Assert.Equal(("vehicle", "ExampleRover", "Example Rover"), (editor.Type, editor.Id, editor.Name));
        Assert.Equal(harness.Localization.ListingShareVehicleTitle, editor.NewTitle);
        var row = Assert.Single(editor.Dependencies);
        Assert.Equal(("ExamplePartsMod", "required", "1.4.0"), (row.Id, row.Kind, row.Min));
        Assert.Equal(harness.Localization.ListingShareDependencyRequired, row.KindText);
        Assert.Equal(
            (harness.Localization.ListingShareIdHint, harness.Localization.ListingShareDependenciesHint, harness.Localization.ListingShareGameMaxHint),
            (editor.IdHint, editor.DependenciesHint, editor.GameMaxHint));
        Assert.True(editor.IsVehicle);
        Assert.Contains(
            harness.Localization.FormatListingShareNotProvided(harness.Localization.FormatListingShareContentId(harness.Localization.ListingShareKindPart, "UnknownAntenna")),
            editor.ShareNotes);

        row.Kind = "recommends";
        Assert.Equal(harness.Localization.ShareModChangedKind, row.ShareKindText);
        editor.Abstract = "A six-wheel rover for the Moon.";

        Assert.Equal("recommends", Assert.Single(editor.Draft.Dependencies).Kind);
        Assert.Contains("kind = \"recommends\"", editor.DocumentText, StringComparison.Ordinal);
        Assert.Equal("vehicles/ExampleRover.toml", editor.Draft.Path);
        Assert.DoesNotContain("[compatibility]", editor.DocumentText, StringComparison.Ordinal);
        Assert.Equal(harness.Localization.FormatListingStillMissing(string.Join(", ", harness.Localization.ListingAuthors, harness.Localization.ListingLicense)), editor.MissingText);
    }

    [Fact]
    public async Task ProposedMod_ShowsItsVersion_AndRemoveTakesItOffTheList()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await CreateInstanceAsync(harness, "Example Rover");
        await OpenAsync(harness);
        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);
        var editor = harness.ViewModel.ListingEditor;

        var row = Assert.Single(editor.Dependencies);

        Assert.Equal("ExamplePartsMod", row.ModName);
        Assert.Equal("Version 1.4.0 or newer", row.ShareVersionText);
        Assert.Null(row.ShareKindText);
        Assert.Equal("ExamplePartsMod is not in Discover, so players cannot install it.", row.NoteText);
        Assert.Equal(
            "Borea could not find a mod that gives part UnknownAntenna. Players without that mod may not be able to load it. Add the mod if you know it.",
            Assert.Single(editor.ShareNotes));

        row.RemoveCommand.Execute(null);

        Assert.Empty(editor.Draft.Dependencies);
    }

    [Fact]
    public async Task RemovingTheOnlyProposedMod_DoesNotSayThatTheItemNeedsNoMods()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await CreateInstanceAsync(harness, "Example Rover");
        await OpenAsync(harness);
        var item = Assert.Single(harness.ViewModel.VehiclesSection.Items);
        File.WriteAllText(Path.Combine(item.Entry.Path, "vehicle.xml"), "<VehicleSaveData Id=\"Example Rover\"><RootPartRef InstanceOf=\"RoverWheel\" /></VehicleSaveData>");
        await item.ShareCommand.ExecuteAsync(null);
        var editor = harness.ViewModel.ListingEditor;
        Assert.Empty(editor.ShareNotes);

        Assert.Single(editor.Dependencies).RemoveCommand.Execute(null);

        Assert.Empty(editor.Dependencies);
        Assert.False(editor.HasNoShareMods);
    }

    [Theory]
    [InlineData("")]
    [InlineData("advancedflightcomputer")]
    public async Task RowThatNeedsAFix_SaysSo_AndRemoveLetsTheShareGoOn(string id)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await CreateInstanceAsync(harness, "Example Rover");
        await OpenAsync(harness);
        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);
        var editor = harness.ViewModel.ListingEditor;
        editor.Authors = "Maxi";
        editor.License = "CC-BY-4.0";
        editor.Abstract = "A six-wheel rover for the Moon.";
        var row = Assert.Single(editor.Dependencies);
        Assert.False(row.HasShareProblem);
        Assert.True(editor.CanContinueShare);

        row.Id = id;

        Assert.True(row.HasShareProblem);
        Assert.Equal(id.Length > 0, row.HasModName);
        Assert.False(editor.CanContinueShare);

        row.RemoveCommand.Execute(null);

        Assert.True(editor.CanContinueShare);
    }

    [Fact]
    public async Task ModFromTheSearch_JoinsAsRequiredUnderItsListedName()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await CreateInstanceAsync(harness, "Example Rover");
        await OpenAsync(harness);
        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);
        var editor = harness.ViewModel.ListingEditor;

        editor.DependencyQuery = "flight computer";
        editor.AddListedDependencyCommand.Execute(null);

        var row = editor.Dependencies[^1];
        Assert.Equal(("AdvancedFlightComputer", "required", "Advanced Flight Computer"), (row.Id, row.Kind, row.ModName));
        Assert.Null(row.ShareVersionText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VehicleWithOnlyGameParts_SaysThatItNeedsNoMods_UnlessAModWroteAFile(bool hasExtraFile)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await CreateInstanceAsync(harness, "Example Rover");
        await OpenAsync(harness);
        var item = Assert.Single(harness.ViewModel.VehiclesSection.Items);
        File.WriteAllText(Path.Combine(item.Entry.Path, "vehicle.xml"), "<VehicleSaveData Id=\"Example Rover\" />");
        if (hasExtraFile)
            File.WriteAllText(Path.Combine(item.Entry.Path, "unscience.json"), "{}");

        await item.ShareCommand.ExecuteAsync(null);

        Assert.Empty(harness.ViewModel.ListingEditor.Dependencies);
        Assert.Equal(!hasExtraFile, harness.ViewModel.ListingEditor.HasNoShareMods);
    }

    [Fact]
    public async Task ShareOfASave_NamesTheFilesTheGameDoesNotWrite()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = await CreateInstanceAsync(harness, "Example Rover");
        var save = WriteItem(harness.Services.Paths.GetInstanceSavesFolder(instance), "Moon Base", "universe.xml", "<UniverseData><System><Id Id=\"Sol\" /></System></UniverseData>");
        File.WriteAllText(Path.Combine(save, "unscience.json"), "{}");
        await OpenAsync(harness);

        await Assert.Single(harness.ViewModel.SavesSection.Items).ShareCommand.ExecuteAsync(null);

        var editor = harness.ViewModel.ListingEditor;
        Assert.Equal("save", editor.Type);
        Assert.Equal(["unscience.json"], editor.ShareExtraFiles);
        Assert.True(editor.HasShareExtraFiles);
        Assert.Equal(harness.Localization.ShareModsSaveHint, editor.ShareModsHint);
    }

    [Fact]
    public async Task SaveArchive_WritesTheZipAndShowsItsHash()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await CreateInstanceAsync(harness, "Example Rover");
        await OpenAsync(harness);
        var window = new ArchiveWindow();
        harness.ViewModel.WindowServices = window;
        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);

        await harness.ViewModel.ListingEditor.SaveShareArchiveCommand.ExecuteAsync(null);

        var bytes = window.Written.ToArray();
        Assert.Equal("Example Rover.zip", window.SuggestedFileName);
        Assert.Equal(harness.Localization.FormatListingShareArchiveSaved("Example Rover.zip", Convert.ToHexStringLower(SHA256.HashData(bytes)), MainViewModel.SizeText(bytes.Length)), harness.ViewModel.ListingEditor.ShareArchiveText);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.Equal(["Example Rover/meta.toml", "Example Rover/vehicle.xml"], zip.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ListedVehicle_IsReadFromTheVehiclesFolderToChangeIt()
    {
        const string listed = "spec_version = 1\nid = \"ExampleRover\"\ntype = \"vehicle\"\nname = \"Example Rover\"\nauthors = [\"Maxi\"]\nabstract = \"A rover.\"\nlicense = \"CC-BY-4.0\"\n";
        using var harness = await ViewModelHarness.CreateAsync(respond: request =>
            request.RequestUri?.AbsoluteUri == "https://raw.githubusercontent.com/KSAModding/content-index/main/vehicles/ExampleRover.toml"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(listed) }
                : null);
        await harness.ViewModel.OpenListingAsync();
        var editor = harness.ViewModel.ListingEditor;
        editor.SelectedListed = new ListedListing("ExampleRover", "Example Rover", string.Empty, IsOwn: false, Type: "vehicle");

        await editor.LoadListedCommand.ExecuteAsync(null);

        Assert.Null(editor.ListedError);
        Assert.True(editor.IsEdit);
        Assert.Equal(("vehicle", "ExampleRover", "vehicles/ExampleRover.toml"), (editor.Type, editor.Id, editor.Draft.Path));
        Assert.Empty(editor.Errors);
    }

    [Fact]
    public async Task ListedVehicleOnAShareHost_KeepsTheHostWhenItIsChanged()
    {
        const string listed = "spec_version = 1\nid = \"ExampleRover\"\ntype = \"vehicle\"\nname = \"Example Rover\"\nauthors = [\"Maxi\"]\nabstract = \"A rover.\"\nlicense = \"CC-BY-4.0\"\n\n[releases]\ngithub = \"maxi/ksa-shares\"\nksamods = \"ExampleRover\"\nauthority = \"ksamods\"\n";
        using var harness = await ViewModelHarness.CreateAsync(respond: request =>
            request.RequestUri?.AbsoluteUri == "https://raw.githubusercontent.com/KSAModding/content-index/main/vehicles/ExampleRover.toml"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(listed) }
                : null);
        await harness.ViewModel.OpenListingAsync();
        var editor = harness.ViewModel.ListingEditor;
        editor.SelectedListed = new ListedListing("ExampleRover", "Example Rover", string.Empty, IsOwn: false, Type: "vehicle");
        await editor.LoadListedCommand.ExecuteAsync(null);

        editor.Abstract = "A six-wheel rover.";

        Assert.Equal(harness.Localization.FormatListingReleasesShareHost("ksamods", "ExampleRover"), editor.ShareHostText);
        Assert.True(editor.NeedsAuthority);
        Assert.Equal(["github", "spacedock", "ksamods"], editor.AuthorityChoices);
        Assert.EndsWith("[releases]\ngithub = \"maxi/ksa-shares\"\nauthority = \"ksamods\"\nksamods = \"ExampleRover\"\n", editor.DocumentText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListedVehicleOnARegisteredShareHost_NamesTheHostAsHostsTomlDoes()
    {
        const string listed = "spec_version = 1\nid = \"ExampleRover\"\ntype = \"vehicle\"\nname = \"Example Rover\"\nauthors = [\"Maxi\"]\nabstract = \"A rover.\"\nlicense = \"CC-BY-4.0\"\n\n[releases]\nksamods = \"ExampleRover\"\n";
        const string hosts = """{ "ksamods": { "kind": "share", "name": "ksamods.gg", "api": "https://ksamods.gg/api/share/v1/", "types": ["vehicle"] } }""";
        using var harness = await ViewModelHarness.CreateAsync(
            editSnapshot: ViewModelHarness.ShareHosts(hosts),
            respond: request => request.RequestUri?.AbsoluteUri switch
            {
                "https://raw.githubusercontent.com/KSAModding/content-index/main/vehicles/ExampleRover.toml" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(listed) },
                _ => null,
            });
        await harness.ViewModel.OpenListingAsync();
        var editor = harness.ViewModel.ListingEditor;
        editor.SelectedListed = new ListedListing("ExampleRover", "Example Rover", string.Empty, IsOwn: false, Type: "vehicle");
        await editor.LoadListedCommand.ExecuteAsync(null);
        await editor.ShareHostsLoad;

        Assert.Equal(harness.Localization.FormatListingReleasesShareHost("ksamods.gg", "ExampleRover"), editor.ShareHostText);
    }

    private static Task RecordPlayedAsync(ViewModelHarness harness, Guid instanceId, DateTimeOffset playedAt) =>
        harness.Services.Instances.UpdateAsync(instanceId, instance =>
        {
            instance.RecordPlayed(playedAt);
            return true;
        });

    /// <summary>Records the files of the folder as Borea installed them from the listing ExampleRover.</summary>
    internal static async Task RecordAsync(ViewModelHarness harness, Guid instance, string folder)
    {
        var name = Path.GetFileName(folder);
        var release = new ModVersionMetadata(1, "ExampleRover", ModVersion.Parse("1.0.0"), ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, "2026.9.7.5402", 5402,
            new DownloadInfo("https://example.invalid/rover.zip", new string('A', 64), null, "application/zip"), null, [], type: ContentType.Vehicle, install: new InstallInfo(name, derived: true, folder: name));
        var files = Directory.GetFiles(folder).ToDictionary(file => Path.GetFileName(file), file => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))));
        await harness.Services.InstalledContent.AddAsync(instance, new InstalledContentRecord(release, name, DateTimeOffset.UnixEpoch, files));
    }

    /// <summary>An instance with a vehicle that names a part of ExamplePartsMod, which Borea installed at 1.4.0, and a part no mod defines.</summary>
    internal static async Task<Guid> CreateInstanceAsync(ViewModelHarness harness, string vehicleName)
    {
        var services = harness.Services;
        var instance = (await services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var mod = Directory.CreateDirectory(Path.Combine(services.Paths.GetInstanceModsFolder(instance.InstanceId), "ExamplePartsMod")).FullName;
        File.WriteAllText(Path.Combine(mod, "mod.toml"), "name = \"ExamplePartsMod\"\nassets = [\"Parts.xml\"]\n");
        File.WriteAllText(Path.Combine(mod, "Parts.xml"), "<Assets><Part Id=\"RoverWheel\" /></Assets>");
        File.WriteAllText(services.Paths.GetInstanceManifestPath(instance.InstanceId), "[[mods]]\nid = \"ExamplePartsMod\"\nenabled = true\n");
        var release = new ModVersionMetadata(1, "ExamplePartsMod", ModVersion.Parse("1.4.0"), ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, "2026.9.7.5402", 5402,
            new DownloadInfo("https://example.invalid/parts.zip", new string('A', 64), null, "application/zip"), null, []);
        instance.AddMod(new InstalledMod("ExamplePartsMod", release.Version, InstallReason.Manual, DateTimeOffset.UtcNow, release));
        await services.Instances.SaveAsync(instance);
        WriteItem(services.Paths.GetInstanceVehiclesFolder(instance.InstanceId), vehicleName, "vehicle.xml", Vehicle);
        return instance.InstanceId;
    }

    internal static async Task OpenAsync(ViewModelHarness harness)
    {
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.Instances.Single(instance => instance.Name == "Main").OpenCommand.ExecuteAsync(null);
        await harness.ViewModel.ShareSchemaLoad!;
    }

    internal static string WriteItem(string kindFolder, string name, string gameFile, string xml)
    {
        var folder = Directory.CreateDirectory(Path.Combine(kindFolder, name)).FullName;
        File.WriteAllText(Path.Combine(folder, "meta.toml"), $"name = \"{name}\"\nversion = \"v2026.10.19.5638\"\n");
        File.WriteAllText(Path.Combine(folder, gameFile), xml);
        return folder;
    }

    /// <summary>Serves the schema of content-index with a rollout gate that holds vehicles and saves.</summary>
    internal static HttpResponseMessage? Gated(HttpRequestMessage request)
    {
        if (request.RequestUri?.AbsoluteUri != SchemaUrl)
            return null;

        var schema = JsonNode.Parse(ListingSchemaStore.EmbeddedText)!;
        schema["properties"]!["type"]!["not"] = new JsonObject { ["title"] = "a type that the index does not accept yet", ["enum"] = new JsonArray("vehicle", "save") };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(schema.ToJsonString()) };
    }

    internal sealed class ArchiveWindow : IWindowServices
    {
        public MemoryStream Written { get; } = new();

        public string? SuggestedFileName { get; private set; }

        public Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string fileTypeName, string text) => Task.FromResult<string?>(null);

        public async Task<string?> SaveFileAsync(string title, string suggestedFileName, string extension, string fileTypeName, Func<Stream, Task> write)
        {
            SuggestedFileName = suggestedFileName;
            await write(Written);
            return suggestedFileName;
        }

        public Task<PickedTextFile?> OpenTextFileAsync(string title, string fileTypeName) => Task.FromResult<PickedTextFile?>(null);

        public Task<PickedBinaryFile?> OpenImageFileAsync(string title, string fileTypeName, long maxBytes) => Task.FromResult<PickedBinaryFile?>(null);

        public Task CopyTextAsync(string text) => Task.CompletedTask;
    }
}
