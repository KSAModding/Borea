using Borea.App.ViewModels;
using Borea.Composition;
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
        Assert.Contains("KSArmory", row.NeededByText);
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
    public async Task OpenLoaderSettings_OpensTheGameTabOfTheSettings()
    {
        using var harness = await CreateAsync(starMap: null);
        await OpenWithAsync(harness, "AdvancedFlightComputer");

        await harness.ViewModel.OpenLoaderSettingsCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.IsSettingsOpen);
        Assert.True(harness.ViewModel.IsGameTab);
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
