using System.IO.Compression;
using System.Net;
using System.Text;
using Borea.App.ViewModels;
using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

public sealed class ModListViewModelTests
{
    private const string ArchiveHost = "archives.test";

    [Fact]
    public async Task Duplicate_CreatesACopyWithTheSameModsVersionsAndEnabledFlags()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        var source = await SeedMainAsync(harness);
        var local = Directory.CreateDirectory(Path.Combine(harness.Services.Paths.GetInstanceModsFolder(source.InstanceId), "LocalOnly"));
        await File.WriteAllTextAsync(Path.Combine(local.FullName, "mod.toml"), "name = \"LocalOnly\"");
        await harness.Services.ForeignModAdopter.ScanAsync(source.InstanceId);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();

        await viewModel.Instances.Single().DuplicateCommand.ExecuteAsync(null);

        var review = viewModel.ModListImport;
        Assert.NotNull(review);
        Assert.True(review.IsDuplicate);
        Assert.Equal("Main (copy)", review.Name);
        Assert.Equal([harness.Localization.FormatModListNotCopied("LocalOnly")], review.Notes);
        Assert.Single(await harness.Services.Instances.GetAllAsync());

        await review.ConfirmCommand.ExecuteAsync(null);

        Assert.Null(review.InstallError);
        Assert.Null(viewModel.ModListImport);
        var copy = (await harness.Services.Instances.GetAllAsync()).Single(instance => instance.Name == "Main (copy)");
        Assert.Equal(["HudCore 1.0.0 Dependency", "HudExtras 2.0.0 Manual"], Describe(copy));
        Assert.Equal(await EntriesAsync(harness, source.InstanceId), await EntriesAsync(harness, copy.InstanceId));
        Assert.Empty(copy.ForeignMods);
        Assert.Contains(viewModel.Instances, row => row.Name == "Main (copy)");
    }

    [Fact]
    public async Task Duplicate_DownloadFails_KeepsTheModalAndRemovesTheNewInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: FailArchive);
        await SeedMainAsync(harness);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.Instances.Single().DuplicateCommand.ExecuteAsync(null);
        var review = viewModel.ModListImport!;

        await review.ConfirmCommand.ExecuteAsync(null);

        Assert.NotNull(review.InstallError);
        Assert.Same(review, viewModel.ModListImport);
        Assert.Equal("Main", Assert.Single(await harness.Services.Instances.GetAllAsync()).Name);
        Assert.False(Directory.Exists(harness.Services.Paths.GetInstanceRoot(review.Plan.InstanceId)));
    }

    [Fact]
    public async Task ExportThenImport_RoundTripsTheInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        var source = await SeedMainAsync(harness);
        var window = new FakeWindowServices();
        var viewModel = harness.ViewModel;
        viewModel.WindowServices = window;
        await viewModel.LoadAsync();

        await viewModel.Instances.Single().ExportModListCommand.ExecuteAsync(null);

        Assert.Equal("Main.toml", window.SavedFileName);
        Assert.Equal(harness.Localization.FormatModListExported("Main", "Main.toml"), viewModel.InstanceNotice);

        window.FileToOpen = new PickedTextFile("Main.toml", window.SavedText!);
        await viewModel.ImportModListCommand.ExecuteAsync(null);
        var review = viewModel.ModListImport!;
        Assert.False(review.IsDuplicate);
        Assert.Equal("Main (2)", review.Name);
        Assert.Empty(review.Notes);
        await review.ConfirmCommand.ExecuteAsync(null);

        var imported = (await harness.Services.Instances.GetAllAsync()).Single(instance => instance.Name == "Main (2)");
        Assert.Equal(InstanceSource.Custom.Value, imported.Source);
        Assert.Equal(["HudCore 1.0.0 Dependency", "HudExtras 2.0.0 Manual"], Describe(imported));
        Assert.Equal(await EntriesAsync(harness, source.InstanceId), await EntriesAsync(harness, imported.InstanceId));
    }

    [Fact]
    public async Task Import_UnknownEntry_IsListedBeforeAnythingInstalls()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        AddReleases(harness);
        var viewModel = harness.ViewModel;
        var text = harness.Services.ModListFormat.Write(new ModList("Shared", [Entry("HudCore", "1.0.0"), Entry("GhostMod", "3.0.0")]));

        await viewModel.BeginImportAsync("shared.toml", text);

        var review = viewModel.ModListImport!;
        Assert.Equal("Shared", review.Name);
        Assert.Equal([harness.Localization.FormatModListUnknown("GhostMod", "3.0.0")], review.Notes);
        Assert.True(review.CanConfirm);
        Assert.Empty(await harness.Services.Instances.GetAllAsync());
        Assert.DoesNotContain(harness.Requests, uri => uri.Host == ArchiveHost);

        await review.ConfirmCommand.ExecuteAsync(null);

        var imported = Assert.Single(await harness.Services.Instances.GetAllAsync());
        Assert.Equal("HudCore", Assert.Single(imported.Mods).ModId);
    }

    [Fact]
    public async Task Duplicate_KeepsTheLoadOrder()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        var source = await SeedMainAsync(harness);
        harness.SpaceDock.Releases.Add(Release("MapTools", "1.0.0"));
        await InstalledContent.AddAsync(harness, "MapTools", activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await harness.Services.ModState.ReorderAsync(source.InstanceId, ["MapTools", "HudCore", "HudExtras"]);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();

        await viewModel.Instances.Single().DuplicateCommand.ExecuteAsync(null);
        await viewModel.ModListImport!.ConfirmCommand.ExecuteAsync(null);

        var copy = (await harness.Services.Instances.GetAllAsync()).Single(instance => instance.Name == "Main (copy)");
        Assert.Equal(["MapTools True", "HudCore True", "HudExtras False"], await EntriesAsync(harness, copy.InstanceId));
    }

    [Fact]
    public async Task Import_YankedEntry_IsListedBeforeAnythingInstalls()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        AddReleases(harness);
        harness.SpaceDock.Releases.Add(Release("MapTools", "1.0.0", yankedReason: "Broken build."));
        var viewModel = harness.ViewModel;
        var text = harness.Services.ModListFormat.Write(new ModList("Shared", [Entry("HudCore", "1.0.0"), Entry("MapTools", "1.0.0")]));

        await viewModel.BeginImportAsync("shared.toml", text);

        var review = viewModel.ModListImport!;
        Assert.Equal([harness.Localization.FormatPackMemberYanked("MapTools", "1.0.0", "Broken build.")], review.Notes);
        Assert.DoesNotContain("Broken build.", review.InstallWarning ?? string.Empty);
        Assert.True(review.CanConfirm);
        Assert.Empty(await harness.Services.Instances.GetAllAsync());
        Assert.DoesNotContain(harness.Requests, uri => uri.Host == ArchiveHost);
    }

    [Fact]
    public async Task Import_ConflictingVersions_ShowsTheConflictAndCannotBeConfirmed()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        AddReleases(harness);
        var viewModel = harness.ViewModel;
        var text = harness.Services.ModListFormat.Write(new ModList("Shared", [Entry("HudCore", "0.5.0"), Entry("HudExtras", "2.0.0")]));

        await viewModel.BeginImportAsync("shared.toml", text);
        var review = viewModel.ModListImport!;
        await review.ConfirmCommand.ExecuteAsync(null);

        Assert.Contains("HudExtras", review.PlanError);
        Assert.False(review.CanConfirm);
        Assert.Empty(await harness.Services.Instances.GetAllAsync());
    }

    [Fact]
    public async Task Import_NewerFormat_TellsTheUserToUpdateBorea()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        await viewModel.BeginImportAsync("future.toml", "format = 2\n");

        Assert.Null(viewModel.ModListImport);
        Assert.Equal(harness.Localization.FormatModListNewerFormat("future.toml", 2), viewModel.InstanceError);
    }

    [Fact]
    public async Task CopyModList_PutsTheModlistOnTheClipboard()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        AddReleases(harness);
        var source = await InstalledContent.AddAsync(harness, "HudCore", activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        var local = Directory.CreateDirectory(Path.Combine(harness.Services.Paths.GetInstanceModsFolder(source.InstanceId), "LocalOnly"));
        await File.WriteAllTextAsync(Path.Combine(local.FullName, "mod.toml"), "name = \"LocalOnly\"");
        await harness.Services.ForeignModAdopter.ScanAsync(source.InstanceId);
        var window = new FakeWindowServices();
        var viewModel = harness.ViewModel;
        viewModel.WindowServices = window;
        await viewModel.LoadAsync();

        await viewModel.Instances.Single().CopyModListCommand.ExecuteAsync(null);

        var copied = harness.Services.ModListFormat.Read(window.CopiedText!);
        Assert.Equal(Entry("HudCore", "1.0.0"), Assert.Single(copied.Mods));
        Assert.Equal(
            $"{harness.Localization.FormatModListCopied("Main")} {harness.Localization.FormatModListNotExported("LocalOnly")}",
            viewModel.InstanceNotice);
    }

    /// <summary>An instance "Main" with HudCore as a dependency and HudExtras, which the game does not load.</summary>
    private static async Task<Instance> SeedMainAsync(ViewModelHarness harness)
    {
        AddReleases(harness);
        var source = await InstalledContent.AddAsync(harness, "HudCore", activate: true, InstallReason.Dependency, ModInstallOwnership.Borea, version: "1.0.0");
        await InstalledContent.AddAsync(harness, "HudExtras", activate: true, ownership: ModInstallOwnership.Borea, version: "2.0.0");
        await harness.Services.ModState.SetInactiveAsync(source.InstanceId, "HudExtras");
        return source;
    }

    private static void AddReleases(ViewModelHarness harness) => harness.SpaceDock.Releases.AddRange(
    [
        Release("HudCore", "0.5.0"),
        Release("HudCore", "1.0.0"),
        Release("HudExtras", "2.0.0", [new ModDependency("HudCore", ModDependencyKind.Required, ModVersion.Parse("1.0.0"))]),
    ]);

    private static ModListEntry Entry(string modId, string version) => new(modId, ModVersion.Parse(version), enabled: true);

    private static IReadOnlyList<string> Describe(Instance instance) =>
        instance.Mods.OrderBy(mod => mod.ModId, StringComparer.Ordinal).Select(mod => $"{mod.ModId} {mod.Version} {mod.Reason}").ToList();

    private static async Task<IReadOnlyList<string>> EntriesAsync(ViewModelHarness harness, Guid instanceId) =>
        (await harness.Services.ModState.GetEntriesAsync(instanceId)).Select(entry => $"{entry.ModId} {entry.Enabled}").ToList();

    private static ModVersionMetadata Release(string modId, string version, IReadOnlyList<ModDependency>? dependencies = null, string? yankedReason = null) => new(
        specVersion: 1,
        modId: modId,
        version: ModVersion.Parse(version),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: DateTimeOffset.UnixEpoch,
        gameMin: "2026.1.1.1",
        gameMinRevision: 1,
        download: new DownloadInfo($"https://{ArchiveHost}/{modId}/{version}.zip", sha256: null, sizeBytes: null, contentType: "application/zip"),
        installSizeBytes: null,
        dependencies: dependencies ?? [],
        yanked: yankedReason is not null,
        yankedReason: yankedReason);

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

    private static HttpResponseMessage? FailArchive(HttpRequestMessage request)
        => request.RequestUri?.Host == ArchiveHost ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : null;

    private sealed class FakeWindowServices : IWindowServices
    {
        public string? SavedFileName { get; private set; }

        public string? SavedText { get; private set; }

        public string? CopiedText { get; private set; }

        public PickedTextFile? FileToOpen { get; set; }

        public Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string fileTypeName, string text)
        {
            SavedFileName = suggestedFileName;
            SavedText = text;
            return Task.FromResult<string?>(suggestedFileName);
        }

        public Task<PickedTextFile?> OpenTextFileAsync(string title, string fileTypeName) => Task.FromResult(FileToOpen);

        public Task CopyTextAsync(string text)
        {
            CopiedText = text;
            return Task.CompletedTask;
        }
    }
}
