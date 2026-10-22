using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Borea.App.ViewModels;
using Borea.Composition;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

/// <summary>The Mod loader section of the instance page (#445).</summary>
public sealed class InstanceLoaderTests
{
    [Fact]
    public async Task OpenInstance_ModsNeedStarMapAndItIsInstalled_ShowsTheRangeAndWhoNeedsIt()
    {
        using var harness = await CreateAsync(starMap: "0.4.7");
        await OpenWithAsync(harness, "AdvancedFlightComputer", "KSArmory");

        var section = harness.ViewModel.InstanceLoader!;
        var row = Assert.Single(section.Rows);
        Assert.Null(section.NoneText);
        Assert.Null(section.DifferentText);
        Assert.Equal(InstanceLoaderState.Installed, row.State);
        // KSArmory asks for 0.4.6, which is the newer of the two minimums
        Assert.Equal(harness.Localization.FormatInstanceLoaderMinimum("0.4.6"), row.RangeText);
        Assert.Equal(harness.Localization.FormatInstanceLoaderInstalled("0.4.7"), row.StatusText);
        // the display name of the listing, not the id, so the row reads like the mod rows
        Assert.Contains("Advanced Flight Computer", row.NeededByText);
        Assert.DoesNotContain("AdvancedFlightComputer", row.NeededByText);
        Assert.Null(row.StatusToolTip);
        Assert.False(row.CanInstall);
    }

    [Fact]
    public async Task OpenInstance_InstalledVersionIsTooOld_SaysSoAndOffersTheReleaseTheModsAccept()
    {
        using var harness = await CreateAsync(starMap: "0.4.5");
        await OpenWithAsync(harness, "KSArmory");

        var row = Assert.Single(harness.ViewModel.InstanceLoader!.Rows);
        Assert.Equal(InstanceLoaderState.WrongVersion, row.State);
        Assert.Equal(harness.Localization.FormatInstanceLoaderWrongVersion("0.4.5"), row.StatusText);
        Assert.Equal(harness.Localization.FormatSetupUpdateLoader("0.4.6"), row.UpdateText);
    }

    [Fact]
    public async Task OpenInstance_LoaderNotInstalled_SaysSo()
    {
        using var harness = await CreateAsync(starMap: null);
        await OpenWithAsync(harness, "AdvancedFlightComputer");

        var row = Assert.Single(harness.ViewModel.InstanceLoader!.Rows);
        Assert.Equal(InstanceLoaderState.NotInstalled, row.State);
        Assert.Equal(harness.Localization.InstanceLoaderNotInstalled, row.StatusText);
        Assert.Equal(harness.Localization.FormatInstanceLoaderInstall("0.4.6"), row.InstallText);
    }

    [Fact]
    public async Task OpenInstance_NoModNeedsALoader_SaysItStillStartsThroughOne()
    {
        using var harness = await CreateAsync(starMap: "0.4.7");
        await harness.Services.Instances.CreateAsync("Main", Borea.Core.Instances.InstanceSource.Custom.Value);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.Instances.Single().OpenCommand.ExecuteAsync(null);

        // the row of the loader that starts the instance says it, so the section has no line of its own
        var section = harness.ViewModel.InstanceLoader!;
        var row = Assert.Single(section.Rows);
        Assert.Null(section.NoneText);
        Assert.Equal(harness.Localization.InstanceLoaderNone, row.NoneText);
        Assert.Equal(harness.Localization.FormatInstanceLoaderAutomatic("0.4.7"), row.ModeText);
        Assert.Null(row.RangeText);
    }

    [Fact]
    public async Task OpenLoaderRow_OpensTheLoaderPageAndLeadsBackToTheInstance()
    {
        using var harness = await CreateAsync(starMap: "0.4.7");
        await OpenWithAsync(harness, "AdvancedFlightComputer");
        var viewModel = harness.ViewModel;
        var instance = viewModel.SelectedInstance!;
        var row = Assert.Single(viewModel.InstanceLoader!.Rows);
        Assert.True(row.CanOpen);

        await row.OpenCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowContent);
        Assert.Equal("StarMap", viewModel.SelectedContent?.ModId);
        Assert.True(viewModel.IsContentFromInstance);
        Assert.Equal(instance.InstanceId, viewModel.ContentReturnInstance?.InstanceId);
    }

    [Fact]
    public async Task Row_ModsAskForVersionsThatDoNotOverlap_NamesEachModsBoundsAndKeepsTheChipShort()
    {
        using var harness = await CreateAsync(starMap: "0.4.7");
        var need = Assert.Single(LoaderNeed.For(InstanceWith(
            NeedsLoader("old-mod", "StarMap", "0.3.0", max: "0.3.9"),
            NeedsLoader("new-mod", "StarMap", "0.4.5"))).Loaders);
        var names = new Dictionary<string, string> { ["old-mod"] = "Old Mod", ["new-mod"] = "New Mod" };

        var row = new InstanceLoaderRow(harness.ViewModel, need, listing: null, installation: null, update: null, id => names[id]);

        var localization = harness.Localization;
        Assert.Equal(InstanceLoaderState.Conflict, row.State);
        Assert.Equal(
            localization.FormatInstanceLoaderModNeeds("New Mod", localization.FormatInstanceLoaderBoundsMinimum("0.4.5"))
            + "; "
            + localization.FormatInstanceLoaderModNeeds("Old Mod", localization.FormatInstanceLoaderBoundsRange("0.3.0", "0.3.9")),
            row.RangeText);
        Assert.DoesNotContain("0.4.5 to 0.3.9", row.RangeText);
        Assert.Null(row.NeededByText);
        Assert.Equal(localization.InstanceLoaderConflictShort, row.StatusText);
        Assert.Equal(localization.InstanceLoaderConflict, row.StatusToolTip);
    }

    [Fact]
    public async Task Row_LoaderTheIndexDoesNotList_SaysWhyItHasNoPage()
    {
        using var harness = await CreateAsync(starMap: null);
        var need = Assert.Single(LoaderNeed.For(InstanceWith(NeedsLoader("other-mod", "OtherLoader", "1.0.0"))).Loaders);

        var row = new InstanceLoaderRow(harness.ViewModel, need, listing: null, installation: null, update: null, id => id);

        Assert.False(row.CanOpen);
        Assert.Equal("OtherLoader", row.Name);
        Assert.Equal(harness.Localization.InstanceLoaderNotInIndex, row.NoPageText);
    }

    [Fact]
    public async Task OpenInstance_NoModsAndNoLoaderInstalled_ShowsTheLoaderThatWouldStartItWithAnInstallLink()
    {
        using var harness = await CreateWithReleasesAsync(starMap: null);
        await OpenEmptyAsync(harness);

        var row = Assert.Single(harness.ViewModel.InstanceLoader!.Rows);
        Assert.Equal("StarMap", row.Name);
        Assert.Equal(harness.Localization.FormatInstanceLoaderAutomatic("0.4.8"), row.ModeText);
        Assert.False(row.IsPinned);
        Assert.Equal(InstanceLoaderState.NotInstalled, row.State);
        Assert.True(row.CanInstall);
        Assert.Equal(harness.Localization.FormatInstanceLoaderInstall("0.4.8"), row.InstallText);
    }

    [Fact]
    public async Task SaveLoaderSetting_PinnedToAnInstalledVersion_SavesThePinAndTheRowShowsIt()
    {
        using var harness = await CreateWithReleasesAsync(starMap: "0.4.7");
        await OpenEmptyAsync(harness);
        var viewModel = harness.ViewModel;

        await viewModel.SelectedInstance!.BeginEditLoaderSettingCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsLoaderSettingModalOpen);
        Assert.False(viewModel.ShowLoaderSettingLoaders);
        Assert.True(viewModel.IsLoaderSettingAutomatic);
        Assert.Equal(["0.4.8", "0.4.7", "0.4.6"], viewModel.LoaderSettingReleases.Select(choice => choice.VersionText));
        viewModel.IsLoaderSettingPinned = true;
        viewModel.SelectedLoaderSettingRelease = viewModel.LoaderSettingReleases.Single(choice => choice.VersionText == "0.4.7");
        Assert.Equal(harness.Localization.LibrarySave, viewModel.LoaderSettingActionText);

        await viewModel.SaveLoaderSettingCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsLoaderSettingModalOpen);
        var saved = await harness.Services.Instances.GetByIdAsync(viewModel.SelectedInstance!.InstanceId);
        Assert.Equal(LoaderSetting.Pinned("StarMap", ModVersion.Parse("0.4.7")), saved!.Loader);
        var row = Assert.Single(viewModel.InstanceLoader!.Rows);
        Assert.True(row.IsPinned);
        Assert.Equal(harness.Localization.FormatInstanceLoaderPinned("0.4.7"), row.ModeText);
        Assert.False(row.CanUpdate);
    }

    [Fact]
    public async Task SaveLoaderSetting_PinnedToAMissingVersion_InstallsItBeforeItSaves()
    {
        using var harness = await CreateWithReleasesAsync(starMap: null);
        await OpenEmptyAsync(harness);
        var viewModel = harness.ViewModel;
        await viewModel.SelectedInstance!.BeginEditLoaderSettingCommand.ExecuteAsync(null);
        viewModel.IsLoaderSettingPinned = true;
        viewModel.SelectedLoaderSettingRelease = viewModel.LoaderSettingReleases.Single(choice => choice.VersionText == "0.4.7");
        Assert.Equal(harness.Localization.InstanceLoaderSaveAndInstall, viewModel.LoaderSettingActionText);

        await viewModel.SaveLoaderSettingCommand.ExecuteAsync(null);

        Assert.Null(viewModel.SetupError);
        Assert.Single(harness.Requests, uri => uri.AbsoluteUri == DownloadUrl("0.4.7"));
        Assert.NotNull(InstalledLoaderVersion.Find(await harness.Services.LoaderVersions.ListAsync(), "StarMap", ModVersion.Parse("0.4.7")));
        var saved = await harness.Services.Instances.GetByIdAsync(viewModel.SelectedInstance!.InstanceId);
        Assert.Equal(LoaderSetting.Pinned("StarMap", ModVersion.Parse("0.4.7")), saved!.Loader);
        var row = Assert.Single(viewModel.InstanceLoader!.Rows);
        Assert.Equal(InstanceLoaderState.Installed, row.State);
        Assert.Equal(harness.Localization.FormatInstanceLoaderPinned("0.4.7"), row.ModeText);
    }

    [Fact]
    public async Task OpenLoaderSetting_PinnedToAMissingVersionOutsideTheChannel_SelectsThatPinAndSaveKeepsIt()
    {
        using var harness = await CreateWithReleasesAsync(starMap: null, testing: "0.4.7");
        var created = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.UpdateAsync(created.InstanceId, instance =>
        {
            instance.SetLoader(LoaderSetting.Pinned("StarMap", ModVersion.Parse("0.4.7")));
            return true;
        });
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.Instances.Single().OpenCommand.ExecuteAsync(null);
        var viewModel = harness.ViewModel;

        await viewModel.SelectedInstance!.BeginEditLoaderSettingCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLoaderSettingPinned);
        Assert.Equal("0.4.7", viewModel.SelectedLoaderSettingRelease?.VersionText);
        Assert.Equal(harness.Localization.InstanceLoaderSaveAndInstall, viewModel.LoaderSettingActionText);

        await viewModel.SaveLoaderSettingCommand.ExecuteAsync(null);

        Assert.Null(viewModel.SetupError);
        var saved = await harness.Services.Instances.GetByIdAsync(created.InstanceId);
        Assert.Equal(LoaderSetting.Pinned("StarMap", ModVersion.Parse("0.4.7")), saved!.Loader);
    }

    private static Instance InstanceWith(params InstalledMod[] mods) =>
        Instance.FromExisting(Guid.NewGuid(), "Main", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, mods, [], isFavorite: false);

    private static InstalledMod NeedsLoader(string modId, string loaderId, string min, string? max = null)
    {
        var release = new ModVersionMetadata(
            specVersion: 1,
            modId: modId,
            version: ModVersion.Parse("1.0.0"),
            releaseStatus: ReleaseStatus.Stable,
            releaseDate: DateTimeOffset.UtcNow,
            gameMin: "2026.7.4.2131",
            gameMinRevision: 2131,
            download: new DownloadInfo($"https://example.invalid/{modId}.zip", sha256: null, sizeBytes: null, contentType: "application/zip"),
            installSizeBytes: null,
            dependencies: [],
            loader: new LoaderRequirement(loaderId, ModVersion.Parse(min), max is null ? null : ModVersion.Parse(max)));
        return new InstalledMod(modId, release.Version, InstallReason.Manual, DateTimeOffset.UtcNow, release);
    }

    private static Task<ViewModelHarness> CreateAsync(string? starMap) =>
        ViewModelHarness.CreateAsync(services => starMap is null
            ? Task.CompletedTask
            : services.SettingsRepository.SaveAsync(services.Settings.WithLoaderInstallation(
                "StarMap",
                new LoaderInstallation(Loader(services), ModVersion.Parse(starMap), rawVersion: null, isAdopted: false))));

    private static string Loader(BoreaServices services) =>
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Loaders", "StarMap")).FullName;

    private static readonly byte[] StarMapArchive = CreateStarMapArchive();

    private static string DownloadUrl(string version) => $"https://github.com/StarMapLoader/StarMap/releases/download/{version}/StarMap-{version}.zip";

    /// <summary>
    /// A game folder, StarMap 0.4.6 to 0.4.8 in the index with their downloads, and <paramref name="starMap"/> recorded
    /// with its launch file. The release <paramref name="testing"/>, when given, is a testing release.
    /// </summary>
    private static Task<ViewModelHarness> CreateWithReleasesAsync(string? starMap, string? testing = null) =>
        ViewModelHarness.CreateAsync(
            async services =>
            {
                var root = Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!;
                var settings = services.Settings.WithGameDirectory(Directory.CreateDirectory(Path.Combine(root, "Game")).FullName);
                if (starMap is not null)
                {
                    var loader = Loader(services);
                    File.WriteAllText(Path.Combine(loader, "StarMap.exe"), string.Empty);
                    settings = settings.WithLoaderInstallation("StarMap", new LoaderInstallation(loader, ModVersion.Parse(starMap), rawVersion: null, isAdopted: false));
                }

                await services.SettingsRepository.SaveAsync(settings);
            },
            respond: request => request.RequestUri?.AbsoluteUri is { } uri && (uri == DownloadUrl("0.4.7") || uri == DownloadUrl("0.4.8"))
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(StarMapArchive) }
                : null,
            editSnapshot: json => WithStarMapReleases(json, testing));

    /// <summary>Adds StarMap 0.4.7 and 0.4.8 beside 0.4.6, for the same game builds.</summary>
    private static string WithStarMapReleases(string json, string? testing)
    {
        var root = JsonNode.Parse(json)!;
        var releases = root["listings"]!.AsArray().Single(node => (string?)node!["id"] == "StarMap")!["releases"]!.AsArray();
        var first = releases.Single()!;
        foreach (var version in new[] { "0.4.7", "0.4.8" })
        {
            var release = first.DeepClone();
            release["version"] = version;
            release["download"]!["url"] = DownloadUrl(version);
            release["download"]!["sha256"] = Convert.ToHexString(SHA256.HashData(StarMapArchive));
            release["download"]!["size"] = StarMapArchive.Length;
            if (version == testing)
                release["release_status"] = "testing";
            releases.Add(release);
        }

        return root.ToJsonString();
    }

    /// <summary>The launch file and the assembly of a StarMap release.</summary>
    private static byte[] CreateStarMapArchive()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("StarMap.exe");
            archive.CreateEntry("StarMap.dll");
        }

        return buffer.ToArray();
    }

    private static async Task OpenEmptyAsync(ViewModelHarness harness)
    {
        await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.Instances.Single().OpenCommand.ExecuteAsync(null);
    }

    private static async Task OpenWithAsync(ViewModelHarness harness, params string[] modIds)
    {
        foreach (var modId in modIds)
            await InstalledContent.AddAsync(harness, modId, activate: true, ownership: ModInstallOwnership.Borea);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
    }
}
