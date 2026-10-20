using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using Borea.App.Localization;
using Borea.App.ViewModels;
using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

public sealed class MissingContentTests
{
    private const string ArchiveHost = "archives.test";

    [Fact]
    public async Task OpenInstance_FolderIsGone_MarksTheRowAndCountsIt()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        var instance = await SeedAsync(harness);
        DeleteFolder(harness, instance.InstanceId, "HudCore");
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        Assert.True(Row(viewModel, "HudCore").IsMissing);
        Assert.False(Row(viewModel, "HudExtras").IsMissing);
        Assert.Equal(harness.Localization.FormatInstanceModsMissing(1), viewModel.MissingContent?.NoticeText);
    }

    [Fact]
    public async Task Enable_FolderIsGone_WritesNothingIntoTheManifest()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        var instance = await SeedAsync(harness);
        await harness.Services.ModState.SetInactiveAsync(instance.InstanceId, "HudCore");
        DeleteFolder(harness, instance.InstanceId, "HudCore");
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = Row(viewModel, "HudCore");

        row.IsEnabled = true;
        await row.ToggleEnabledCommand.ExecuteAsync(null);

        Assert.False(await harness.Services.ModState.IsActiveAsync(instance.InstanceId, "HudCore"));
        Assert.Equal(harness.Localization.FormatToastEnableFailed(row.Name), viewModel.Toasts.Items[^1].Message);
    }

    [Fact]
    public async Task InstallAgain_BringsTheFolderBackAndClearsTheMark()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        var instance = await SeedAsync(harness);
        DeleteFolder(harness, instance.InstanceId, "HudCore");
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        await Row(viewModel, "HudCore").InstallAgainCommand.ExecuteAsync(null);
        // no game is installed, so the planner warns that the compatibility of the release is unknown
        Assert.True(Row(viewModel, "HudCore").IsConfirmingUpdate);
        await Row(viewModel, "HudCore").ConfirmUpdateCommand.ExecuteAsync(null);

        Assert.Null(Row(viewModel, "HudCore").InstallError);
        Assert.False(Row(viewModel, "HudCore").IsMissing);
        Assert.Null(viewModel.MissingContent);
        Assert.True(File.Exists(Path.Combine(ModFolder(harness, instance.InstanceId, "HudCore"), "mod.toml")));
        var saved = await harness.Services.Instances.GetByIdAsync(instance.InstanceId);
        Assert.Equal("1.0.0", saved!.Mods.Single(mod => mod.ModId == "HudCore").Version.ToString());
    }

    [Fact]
    public async Task InstallAgain_PinnedMod_KeepsThePin()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        var instance = await SeedAsync(harness);
        await harness.Services.Instances.UpdateAsync(instance.InstanceId, saved => saved.SetPinned("HudCore", pinned: true));
        DeleteFolder(harness, instance.InstanceId, "HudCore");
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        await Row(viewModel, "HudCore").InstallAgainCommand.ExecuteAsync(null);
        await Row(viewModel, "HudCore").ConfirmUpdateCommand.ExecuteAsync(null);

        Assert.Null(Row(viewModel, "HudCore").InstallError);
        Assert.False(Row(viewModel, "HudCore").IsMissing);
        var saved = await harness.Services.Instances.GetByIdAsync(instance.InstanceId);
        Assert.True(saved!.Mods.Single(mod => mod.ModId == "HudCore").IsPinned);
        Assert.False(saved.Mods.Single(mod => mod.ModId == "HudExtras").IsPinned);
        Assert.True(Row(viewModel, "HudCore").IsPinned);
    }

    [Fact]
    public async Task InstallAgain_ReleaseWhoseDownloadIsGone_WarnsWithTheDate()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.MarkGone("MeasureTools", "1.1.10"));
        var instance = await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, ownership: ModInstallOwnership.Borea, version: "1.1.10");
        DeleteFolder(harness, instance.InstanceId, "MeasureTools");
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        await Row(viewModel, "MeasureTools").InstallAgainCommand.ExecuteAsync(null);

        var row = Row(viewModel, "MeasureTools");
        Assert.True(row.IsConfirmingUpdate);
        Assert.Contains($"MeasureTools: {string.Format(CultureInfo.CurrentCulture, Resources.InstallMessageUnavailableFormat, "1.1.10", MainViewModel.DateText(ViewModelHarness.GoneSince))}", row.InstallWarning);
        Assert.True(row.IsMissing);
    }

    [Fact]
    public async Task Disable_FolderIsGone_ClearsTheManifestEntry()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        var instance = await SeedAsync(harness);
        DeleteFolder(harness, instance.InstanceId, "HudCore");
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = Row(viewModel, "HudCore");
        Assert.True(row.IsEnabled);

        row.IsEnabled = false;
        await row.ToggleEnabledCommand.ExecuteAsync(null);

        Assert.False(await harness.Services.ModState.IsActiveAsync(instance.InstanceId, "HudCore"));
        Assert.Empty(viewModel.Toasts.Items);
    }

    [Fact]
    public async Task InstallAgain_ModWasDisabled_ComesBackDisabled()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        var instance = await SeedAsync(harness);
        await harness.Services.ModState.SetInactiveAsync(instance.InstanceId, "HudCore");
        DeleteFolder(harness, instance.InstanceId, "HudCore");
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        await Row(viewModel, "HudCore").InstallAgainCommand.ExecuteAsync(null);
        await Row(viewModel, "HudCore").ConfirmUpdateCommand.ExecuteAsync(null);

        Assert.False(Row(viewModel, "HudCore").IsMissing);
        Assert.False(await harness.Services.ModState.IsActiveAsync(instance.InstanceId, "HudCore"));
    }

    [Fact]
    public async Task InstallAgain_FolderWithoutAModToml_KeepsTheRecordAndNamesTheFolder()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        var instance = await SeedAsync(harness);
        File.Delete(Path.Combine(ModFolder(harness, instance.InstanceId, "HudCore"), "mod.toml"));
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = Row(viewModel, "HudCore");
        Assert.True(row.IsMissing);

        await row.InstallAgainCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatContentFolderInTheWay("HudCore"), row.InstallError);
        var saved = await harness.Services.Instances.GetByIdAsync(instance.InstanceId);
        Assert.Contains(saved!.Mods, mod => mod.ModId == "HudCore");
    }

    [Fact]
    public async Task InstallAgain_OneFolderCameBack_KeepsEveryRecord()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        var instance = await SeedAsync(harness);
        DeleteFolder(harness, instance.InstanceId, "HudCore");
        DeleteFolder(harness, instance.InstanceId, "HudExtras");
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var notice = viewModel.MissingContent!;

        await notice.InstallAgainCommand.ExecuteAsync(null);
        Assert.True(notice.IsConfirmingInstall);
        WriteFolder(harness, instance.InstanceId, "HudExtras");
        await notice.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatContentBackOnDisk("HudExtras"), notice.InstallError);
        var saved = await harness.Services.Instances.GetByIdAsync(instance.InstanceId);
        Assert.Equal(new[] { "HudCore", "HudExtras" }, saved!.Mods.Select(mod => mod.ModId).Order());
    }

    [Fact]
    public async Task RemoveFromTheList_DropsOnlyThatRecord()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        var instance = await SeedAsync(harness);
        DeleteFolder(harness, instance.InstanceId, "HudCore");
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var row = Row(viewModel, "HudCore");

        Assert.True(row.CanRemove);
        row.BeginRemoveCommand.Execute(null);
        await row.ConfirmRemoveCommand.ExecuteAsync(null);

        Assert.Null(viewModel.MissingContent);
        Assert.Equal(new[] { "HudExtras" }, Rows(viewModel).Select(item => item.ModId));
        var saved = await harness.Services.Instances.GetByIdAsync(instance.InstanceId);
        Assert.Equal(new[] { "HudExtras" }, saved!.Mods.Select(mod => mod.ModId));
        Assert.True(File.Exists(Path.Combine(ModFolder(harness, instance.InstanceId, "HudExtras"), "mod.toml")));
    }

    [Fact]
    public async Task RemoveFromTheListOnTheNotice_DropsEveryMissingRecord()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        var instance = await SeedAsync(harness);
        DeleteFolder(harness, instance.InstanceId, "HudCore");
        DeleteFolder(harness, instance.InstanceId, "HudExtras");
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        viewModel.MissingContent!.BeginDropCommand.Execute(null);
        await viewModel.MissingContent!.ConfirmDropCommand.ExecuteAsync(null);

        Assert.Null(viewModel.MissingContent);
        Assert.Empty(Rows(viewModel));
        var saved = await harness.Services.Instances.GetByIdAsync(instance.InstanceId);
        Assert.Empty(saved!.Mods);
    }

    [Fact]
    public async Task OpenInstance_RecordOfAVehicleWhoseFolderIsGone_IsDropped()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        var instance = await SeedAsync(harness);
        Directory.CreateDirectory(Path.Combine(harness.Services.Paths.GetInstanceVehiclesFolder(instance.InstanceId), "Kept Rover"));
        await harness.Services.InstalledContent.AddAsync(instance.InstanceId, VehicleRecord("Kept Rover"));
        await harness.Services.InstalledContent.AddAsync(instance.InstanceId, VehicleRecord("Gone Rover"));
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        Assert.Equal("Kept Rover", Assert.Single(await harness.Services.InstalledContent.ListAsync(instance.InstanceId)).FolderName);
    }

    [Fact]
    public async Task OpenInstance_UnreadableInstallRecords_StillOpensAndLogs()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        var instance = await SeedAsync(harness);
        DeleteFolder(harness, instance.InstanceId, "HudCore");
        await File.WriteAllTextAsync(harness.Services.Paths.GetInstanceInstalledContentPath(instance.InstanceId), "[[Items]\nType = ");
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        Assert.True(Row(viewModel, "HudCore").IsMissing);
        Assert.Contains(harness.Services.Log.ReadRecentLines(50), line => line.Contains("install records", StringComparison.Ordinal));
    }

    private static InstalledContentRecord VehicleRecord(string folder) => new(
        new ModVersionMetadata(
            specVersion: 1,
            modId: folder.Replace(" ", "", StringComparison.Ordinal),
            version: ModVersion.Parse("1.0.0"),
            releaseStatus: ReleaseStatus.Stable,
            releaseDate: DateTimeOffset.UnixEpoch,
            gameMin: "2026.10.19.5638",
            gameMinRevision: 5638,
            download: new DownloadInfo("https://archives.test/rover.zip", new string('A', 64), 1, "application/zip"),
            installSizeBytes: 1,
            dependencies: [],
            type: ContentType.Vehicle,
            install: new InstallInfo(null, true, folder: folder)),
        folder,
        DateTimeOffset.UnixEpoch,
        new Dictionary<string, string>());

    private static async Task<Instance> SeedAsync(ViewModelHarness harness)
    {
        harness.SpaceDock.Releases.AddRange(
        [
            Release("HudCore", "1.0.0"),
            Release("HudExtras", "2.0.0", [new ModDependency("HudCore", ModDependencyKind.Required, ModVersion.Parse("1.0.0"))]),
        ]);
        var instance = await InstalledContent.AddAsync(harness, "HudCore", activate: true, InstallReason.Dependency, ModInstallOwnership.Borea, version: "1.0.0");
        await InstalledContent.AddAsync(harness, "HudExtras", activate: true, ownership: ModInstallOwnership.Borea, version: "2.0.0");
        return instance;
    }

    private static IReadOnlyList<ContentItem> Rows(MainViewModel viewModel)
        => viewModel.ContentGroups.SelectMany(group => group.Items).ToList();

    private static ContentItem Row(MainViewModel viewModel, string modId)
        => Rows(viewModel).Single(item => item.ModId == modId);

    private static string ModFolder(ViewModelHarness harness, Guid instanceId, string modId)
        => Path.Combine(harness.Services.Paths.GetInstanceModsFolder(instanceId), modId);

    private static void DeleteFolder(ViewModelHarness harness, Guid instanceId, string modId)
        => Directory.Delete(ModFolder(harness, instanceId, modId), recursive: true);

    /// <summary>A folder the game loads a mod from, as if the user put it back by hand.</summary>
    private static void WriteFolder(ViewModelHarness harness, Guid instanceId, string modId)
    {
        var folder = ModFolder(harness, instanceId, modId);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "mod.toml"), $"name = \"{modId}\"");
    }

    private static ModVersionMetadata Release(string modId, string version, IReadOnlyList<ModDependency>? dependencies = null) => new(
        specVersion: 1,
        modId: modId,
        version: ModVersion.Parse(version),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: DateTimeOffset.UnixEpoch,
        gameMin: "2026.1.1.1",
        gameMinRevision: 1,
        download: new DownloadInfo($"https://{ArchiveHost}/{modId}/{version}.zip", sha256: null, sizeBytes: null, contentType: "application/zip"),
        installSizeBytes: null,
        dependencies: dependencies ?? []);

    /// <summary>Serves a zip with a mod.toml at its root for every archive URL.</summary>
    private static HttpResponseMessage? ServeArchive(HttpRequestMessage request)
    {
        if (request.RequestUri?.Host != ArchiveHost)
            return null;

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("mod.toml").Open(), Encoding.UTF8);
            writer.Write($"name = \"{request.RequestUri.Segments[1].TrimEnd('/')}\"");
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(buffer.ToArray()) };
    }
}
