using Borea.App.ViewModels;
using Borea.Core.Instances;
using Borea.Core.ModPacks;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

/// <summary>Saving a preset from an instance, picking one for a new instance, and removing one (#412).</summary>
public sealed class GameSettingsPresetTests
{
    private const string GameBuild = "GameVersionFixture.dll";
    private const string Settings = "[Graphics]\nQuality = \"high\"\n";

    [Fact]
    public async Task SavePreset_FromAnInstance_KeepsTheSettingsAndTheInstalledVersion()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await WithSettingsAsync(harness, Settings);

        instance.SaveSettingsPresetCommand.Execute(null);
        viewModel.NewGameSettingsPresetName = " Full screen ";
        await viewModel.ConfirmGameSettingsPresetModalCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsCreatingGameSettingsPreset);
        Assert.Null(viewModel.GameSettingsPresetError);
        var preset = Assert.Single(await harness.Services.GameSettingsPresets.ListAsync());
        Assert.Equal("Full screen", preset.Name);
        Assert.Equal("2026.8.3.5117", preset.Version.ToString());
        var saved = Assert.Single(viewModel.SavedGameSettingsPresets);
        Assert.Equal("Full screen", saved.Name);
        Assert.Equal("2026.8.3.5117", saved.VersionText);
    }

    [Fact]
    public async Task SavePreset_WithoutAName_KeepsTheModalOpenAndSaysWhy()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await WithSettingsAsync(harness, Settings);

        instance.SaveSettingsPresetCommand.Execute(null);
        viewModel.NewGameSettingsPresetName = "   ";
        await viewModel.ConfirmGameSettingsPresetModalCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsCreatingGameSettingsPreset);
        Assert.Equal(harness.Localization.ModalNameRequired, viewModel.GameSettingsPresetError);
        Assert.Empty(await harness.Services.GameSettingsPresets.ListAsync());
    }

    [Fact]
    public async Task SavePreset_InstanceWithoutSettings_SaysToStartTheGameOnce()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await WithSettingsAsync(harness, settings: null);

        instance.SaveSettingsPresetCommand.Execute(null);
        viewModel.NewGameSettingsPresetName = "Full screen";
        await viewModel.ConfirmGameSettingsPresetModalCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsCreatingGameSettingsPreset);
        Assert.Equal(harness.Localization.PresetModalNoSettings, viewModel.GameSettingsPresetError);
        Assert.Empty(await harness.Services.GameSettingsPresets.ListAsync());
    }

    [Fact]
    public async Task SavePreset_WithoutAnInstalledGame_ShowsAToastAndOpensNoModal()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await WithSettingsAsync(harness, Settings);

        instance.SaveSettingsPresetCommand.Execute(null);

        Assert.False(viewModel.IsCreatingGameSettingsPreset);
        Assert.Contains(viewModel.Toasts.Items, toast => toast.Message == harness.Localization.PresetModalTitle);
    }

    [Fact]
    public async Task NewInstance_WithAPreset_StartsFromItsSettings()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await WithSettingsAsync(harness, Settings);
        await harness.Services.GameSettingsPresets.SaveAsync("Full screen", InstalledVersion(harness), SourceFile(harness));

        viewModel.BeginCreateInstanceCommand.Execute(null);
        await viewModel.WhenGameSettingsPresetsLoadedAsync();
        viewModel.SelectedGameSettingsPreset = viewModel.GameSettingsPresets.Single(item => item.Id is not null);
        viewModel.ModalInstanceName = "From preset";
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);

        var created = (await harness.Services.Instances.GetAllAsync()).Single(item => item.Name == "From preset");
        Assert.Equal(Settings, await File.ReadAllTextAsync(harness.Services.Paths.GetInstanceSettingsPath(created.InstanceId)));
    }

    [Fact]
    public async Task NewInstance_WithoutAPreset_WritesNoSettings()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.GameSettingsPresets.SaveAsync("Full screen", InstalledVersion(harness), SourceFile(harness));

        viewModel.BeginCreateInstanceCommand.Execute(null);
        await viewModel.WhenGameSettingsPresetsLoadedAsync();
        // the picker starts at "No preset"
        Assert.Null(viewModel.SelectedGameSettingsPreset?.Id);
        viewModel.ModalInstanceName = "Plain";
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);

        var created = (await harness.Services.Instances.GetAllAsync()).Single(item => item.Name == "Plain");
        Assert.False(File.Exists(harness.Services.Paths.GetInstanceSettingsPath(created.InstanceId)));
    }

    [Fact]
    public async Task DeletePreset_AsksOnceAndThenRemovesIt()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.GameSettingsPresets.SaveAsync("Full screen", InstalledVersion(harness), SourceFile(harness));
        await viewModel.LoadGameSettingsPresetsAsync();
        var row = Assert.Single(viewModel.SavedGameSettingsPresets);

        await row.DeleteCommand.ExecuteAsync(null);

        Assert.True(row.IsConfirmingDelete);
        Assert.NotEmpty(await harness.Services.GameSettingsPresets.ListAsync());

        await row.DeleteCommand.ExecuteAsync(null);

        Assert.Empty(await harness.Services.GameSettingsPresets.ListAsync());
        Assert.Empty(viewModel.SavedGameSettingsPresets);
        Assert.Single(viewModel.GameSettingsPresets);
    }

    [Fact]
    public async Task Presets_AreReadAtStart_SoTheGameSettingsListThemWithoutTheModal()
    {
        using var harness = await CreateAsync(SeedPresetAsync);
        var viewModel = harness.ViewModel;

        await viewModel.WhenGameSettingsPresetsLoadedAsync();

        var saved = Assert.Single(viewModel.SavedGameSettingsPresets);
        Assert.Equal("Full screen", saved.Name);
        Assert.False(viewModel.IsCreatingInstance);
    }

    [Fact]
    public async Task NewInstanceFromAPack_OffersNoPicker()
    {
        using var harness = await CreateAsync(SeedPresetAsync);
        var viewModel = harness.ViewModel;
        var pack = new PackItem(viewModel, Pack());

        pack.NewInstanceCommand.Execute(null);

        Assert.True(viewModel.IsCreatingInstance);
        Assert.False(viewModel.CanPickGameSettingsPreset);
    }

    [Fact]
    public async Task ImportOfASharedProfile_OffersNoPicker()
    {
        using var harness = await CreateAsync(SeedPresetAsync);
        var viewModel = harness.ViewModel;

        viewModel.BeginImportSharedProfileCommand.Execute(null);

        Assert.True(viewModel.IsCreatingInstance);
        Assert.False(viewModel.CanPickGameSettingsPreset);
    }

    [Fact]
    public async Task NewInstance_WithAPresetBoreaCannotRead_IsCreatedAndSaysThePresetIsMissing()
    {
        using var harness = await CreateAsync(SeedPresetAsync);
        var viewModel = harness.ViewModel;
        var preset = Assert.Single(await harness.Services.GameSettingsPresets.ListAsync());
        File.Delete(Path.Combine(harness.Services.Paths.GetGameSettingsPresetsRoot(), preset.Id.ToString(), "settings.toml"));

        viewModel.BeginCreateInstanceCommand.Execute(null);
        await viewModel.WhenGameSettingsPresetsLoadedAsync();
        viewModel.SelectedGameSettingsPreset = viewModel.GameSettingsPresets.Single(item => item.Id is not null);
        viewModel.ModalInstanceName = "Without settings";
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsCreatingInstance);
        var created = (await harness.Services.Instances.GetAllAsync()).Single(item => item.Name == "Without settings");
        Assert.False(File.Exists(harness.Services.Paths.GetInstanceSettingsPath(created.InstanceId)));
        Assert.Contains(viewModel.Toasts.Items, toast => toast.Message == harness.Localization.FormatToastPresetNotApplied("Without settings"));
    }

    /// <summary>One pack of the index, for the flow that creates an instance from it.</summary>
    private static ModPackMetadata Pack() => new(
        specVersion: 1,
        modPackId: "NavigationStarterPack",
        source: "index",
        name: "Navigation Starter Pack",
        authors: ["Maxi"],
        abstractText: "Everything you need for maneuver planning.",
        license: "CC0-1.0",
        links: new Dictionary<string, string> { ["forums"] = "https://forums.example/pack" },
        gameMin: "2026.8.3.5117",
        version: ModVersion.Parse("1.0.0"),
        releasedAt: new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero),
        mods: [new ModPackEntry("AdvancedFlightComputer", ModVersion.Parse("0.7.5"))]);

    /// <summary>A harness whose game folder holds the version fixture, so a preset can record a version.</summary>
    /// <param name="seed">Runs after the game folder is in place, for a test that wants presets on disk.</param>
    private static Task<ViewModelHarness> CreateAsync(Func<Borea.Composition.BoreaServices, Task>? seed = null) =>
        ViewModelHarness.CreateAsync(async services =>
        {
            var game = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Game")).FullName;
            File.Copy(Path.Combine(AppContext.BaseDirectory, GameBuild), Path.Combine(game, "KSA.dll"), overwrite: true);
            await services.SettingsRepository.SaveAsync(services.Settings.WithGameDirectory(game));
            if (seed is not null)
                await seed(services);
        });

    /// <summary>One saved preset, written before the view model reads anything.</summary>
    private static async Task SeedPresetAsync(Borea.Composition.BoreaServices services)
    {
        var path = Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "seed-settings.toml");
        await File.WriteAllTextAsync(path, Settings);
        await services.GameSettingsPresets.SaveAsync("Full screen", Borea.Core.Game.GameVersion.Parse("2026.8.3.5117"), path);
    }

    private static Borea.Core.Game.GameVersion InstalledVersion(ViewModelHarness harness) =>
        harness.Services.InstalledVersion.GetInstalledVersion()!.Version!.Value;

    /// <summary>A settings.toml outside any instance, for seeding a preset directly.</summary>
    private static string SourceFile(ViewModelHarness harness)
    {
        var path = Path.Combine(harness.Root, "source-settings.toml");
        File.WriteAllText(path, Settings);
        return path;
    }

    /// <summary>An active instance, with or without a settings.toml of its own.</summary>
    private static async Task<InstanceItem> WithSettingsAsync(ViewModelHarness harness, string? settings)
    {
        var instance = await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        if (settings is not null)
        {
            var path = harness.Services.Paths.GetInstanceSettingsPath(instance.Instance.InstanceId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, settings);
        }

        await harness.ViewModel.LoadAsync();
        return harness.ViewModel.Instances.Single();
    }
}
