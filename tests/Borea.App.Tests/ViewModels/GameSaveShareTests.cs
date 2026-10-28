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
        using var harness = await ViewModelHarness.CreateAsync();
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
        using var harness = await ViewModelHarness.CreateAsync(respond: Ungated);
        await CreateInstanceAsync(harness, "Example Rover");

        await OpenAsync(harness);

        Assert.True(harness.ViewModel.VehiclesSection.CanShare);
        Assert.True(harness.ViewModel.SavesSection.CanShare);
        Assert.True(Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.CanExecute(null));
    }

    [Fact]
    public async Task NameWithTwoSpaces_AsksForARenameFirst()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: Ungated);
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

        viewModel.CancelNameModalCommand.Execute(null);

        Assert.Null(viewModel.GameSaveRenameReason);
    }

    [Fact]
    public async Task CutOffVehicleFile_NamesTheFileFirstInTheToast()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: Ungated);
        await CreateInstanceAsync(harness, "Example Rover");
        await OpenAsync(harness);
        var item = Assert.Single(harness.ViewModel.VehiclesSection.Items);
        var gameFile = Path.Combine(item.Entry.Path, "vehicle.xml");
        File.WriteAllText(gameFile, Vehicle[..40]);

        await item.ShareCommand.ExecuteAsync(null);

        var toast = harness.ViewModel.Toasts.Items[^1];
        Assert.Equal(harness.Localization.FormatToastShareFailed("Example Rover"), toast.Message);
        Assert.StartsWith(harness.Localization.FormatShareFolderNotXml("vehicle.xml") + "\n", toast.Detail, StringComparison.Ordinal);
        Assert.Contains(gameFile, toast.Detail, StringComparison.Ordinal);
        Assert.False(harness.ViewModel.ListingEditor.HasShare);
    }

    [Fact]
    public async Task UnreadableModFile_IsNamedRelativeToTheInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: Ungated);
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
    public async Task NameChangedToAnInvalidNameAfterTheListLoaded_AsksForARenameFirst()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: Ungated);
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
    }

    [Fact]
    public async Task Share_OpensTheEditorWithTheProposalAndKeepsAKindTheAuthorChanged()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: Ungated);
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
        editor.Abstract = "A six-wheel rover for the Moon.";

        Assert.Equal("recommends", Assert.Single(editor.Draft.Dependencies).Kind);
        Assert.Contains("kind = \"recommends\"", editor.DocumentText, StringComparison.Ordinal);
        Assert.Equal("vehicles/ExampleRover.toml", editor.Draft.Path);
        Assert.DoesNotContain("[compatibility]", editor.DocumentText, StringComparison.Ordinal);
        Assert.Equal(harness.Localization.FormatListingStillMissing(string.Join(", ", harness.Localization.ListingAuthors, harness.Localization.ListingLicense)), editor.MissingText);
    }

    [Fact]
    public async Task ShareOfASave_NamesTheFilesTheGameDoesNotWrite()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: Ungated);
        var instance = await CreateInstanceAsync(harness, "Example Rover");
        var save = WriteItem(harness.Services.Paths.GetInstanceSavesFolder(instance), "Moon Base", "universe.xml", "<UniverseData><System><Id Id=\"Sol\" /></System></UniverseData>");
        File.WriteAllText(Path.Combine(save, "unscience.json"), "{}");
        await OpenAsync(harness);

        await Assert.Single(harness.ViewModel.SavesSection.Items).ShareCommand.ExecuteAsync(null);

        var editor = harness.ViewModel.ListingEditor;
        Assert.Equal("save", editor.Type);
        Assert.Equal(["unscience.json"], editor.ShareExtraFiles);
        Assert.True(editor.HasShareExtraFiles);
    }

    [Fact]
    public async Task SaveArchive_WritesTheZipAndShowsItsHash()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: Ungated);
        await CreateInstanceAsync(harness, "Example Rover");
        await OpenAsync(harness);
        var window = new ArchiveWindow();
        harness.ViewModel.WindowServices = window;
        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);

        await harness.ViewModel.ListingEditor.SaveShareArchiveCommand.ExecuteAsync(null);

        var bytes = window.Written.ToArray();
        Assert.Equal("Example Rover.zip", window.SuggestedFileName);
        Assert.Equal(harness.Localization.FormatListingShareArchiveSaved("Example Rover.zip", Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length), harness.ViewModel.ListingEditor.ShareArchiveText);
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
                : Ungated(request));
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
                : Ungated(request));
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
        const string hosts = "[ksamods]\nkind = \"share\"\nname = \"ksamods.gg\"\napi = \"https://ksamods.gg/api/share/v1/\"\ntypes = [\"vehicle\"]\n";
        using var harness = await ViewModelHarness.CreateAsync(respond: request => request.RequestUri?.AbsoluteUri switch
        {
            "https://raw.githubusercontent.com/KSAModding/content-index/main/vehicles/ExampleRover.toml" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(listed) },
            "https://raw.githubusercontent.com/KSAModding/content-index/main/hosts.toml" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(hosts) },
            _ => Ungated(request),
        });
        await harness.ViewModel.OpenListingAsync();
        var editor = harness.ViewModel.ListingEditor;
        editor.SelectedListed = new ListedListing("ExampleRover", "Example Rover", string.Empty, IsOwn: false, Type: "vehicle");
        await editor.LoadListedCommand.ExecuteAsync(null);
        await editor.ShareHostsLoad;

        Assert.Equal(harness.Localization.FormatListingReleasesShareHost("ksamods.gg", "ExampleRover"), editor.ShareHostText);
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

    private static string WriteItem(string kindFolder, string name, string gameFile, string xml)
    {
        var folder = Directory.CreateDirectory(Path.Combine(kindFolder, name)).FullName;
        File.WriteAllText(Path.Combine(folder, "meta.toml"), $"name = \"{name}\"\nversion = \"v2026.10.19.5638\"\n");
        File.WriteAllText(Path.Combine(folder, gameFile), xml);
        return folder;
    }

    /// <summary>Serves the schema of content-index without its rollout gate, as it reads once a steward lifts it.</summary>
    internal static HttpResponseMessage? Ungated(HttpRequestMessage request)
    {
        if (request.RequestUri?.AbsoluteUri != SchemaUrl)
            return null;

        var schema = JsonNode.Parse(ListingSchemaStore.EmbeddedText)!;
        schema["properties"]!["type"]!.AsObject().Remove("not");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(schema.ToJsonString()) };
    }

    private sealed class ArchiveWindow : IWindowServices
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
