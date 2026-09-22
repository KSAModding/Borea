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
        Assert.False(row.CanFix);
    }

    [Fact]
    public async Task OpenInstance_InstalledVersionIsTooOld_SaysSoAndLeadsToTheSettings()
    {
        using var harness = await CreateAsync(starMap: "0.4.5");
        await OpenWithAsync(harness, "KSArmory");

        var row = Assert.Single(harness.ViewModel.InstanceLoader!.Rows);
        Assert.Equal(InstanceLoaderState.WrongVersion, row.State);
        Assert.Equal(harness.Localization.FormatInstanceLoaderWrongVersion("0.4.5"), row.StatusText);
        Assert.True(row.CanFix);
    }

    [Fact]
    public async Task OpenInstance_LoaderNotInstalled_SaysSo()
    {
        using var harness = await CreateAsync(starMap: null);
        await OpenWithAsync(harness, "AdvancedFlightComputer");

        var row = Assert.Single(harness.ViewModel.InstanceLoader!.Rows);
        Assert.Equal(InstanceLoaderState.NotInstalled, row.State);
        Assert.Equal(harness.Localization.InstanceLoaderNotInstalled, row.StatusText);
        Assert.True(row.CanFix);
    }

    [Fact]
    public async Task OpenInstance_NoModNeedsALoader_SaysItStillStartsThroughOne()
    {
        using var harness = await CreateAsync(starMap: "0.4.7");
        await harness.Services.Instances.CreateAsync("Main", Borea.Core.Instances.InstanceSource.Custom.Value);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.Instances.Single().OpenCommand.ExecuteAsync(null);

        var section = harness.ViewModel.InstanceLoader!;
        Assert.Empty(section.Rows);
        Assert.Equal(harness.Localization.InstanceLoaderNone, section.NoneText);
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
    public async Task OpenLoaderSettings_OpensTheGameTabOfTheSettings()
    {
        using var harness = await CreateAsync(starMap: null);
        await OpenWithAsync(harness, "AdvancedFlightComputer");

        await harness.ViewModel.OpenLoaderSettingsCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.IsSettingsOpen);
        Assert.True(harness.ViewModel.IsGameTab);
    }

    [Fact]
    public async Task Row_ModsAskForVersionsThatDoNotOverlap_NamesEachModsBoundsAndKeepsTheChipShort()
    {
        using var harness = await CreateAsync(starMap: "0.4.7");
        var need = Assert.Single(LoaderNeed.For(InstanceWith(
            NeedsLoader("old-mod", "StarMap", "0.3.0", max: "0.3.9"),
            NeedsLoader("new-mod", "StarMap", "0.4.5"))).Loaders);
        var names = new Dictionary<string, string> { ["old-mod"] = "Old Mod", ["new-mod"] = "New Mod" };

        var row = new InstanceLoaderRow(harness.ViewModel, need, listing: null, installation: null, id => names[id]);

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

        var row = new InstanceLoaderRow(harness.ViewModel, need, listing: null, installation: null, id => id);

        Assert.False(row.CanOpen);
        Assert.Equal("OtherLoader", row.Name);
        Assert.Equal(harness.Localization.InstanceLoaderNotInIndex, row.NoPageText);
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

    private static async Task OpenWithAsync(ViewModelHarness harness, params string[] modIds)
    {
        foreach (var modId in modIds)
            await InstalledContent.AddAsync(harness, modId, activate: true, ownership: ModInstallOwnership.Borea);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
    }
}
