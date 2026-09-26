using Borea.App.ViewModels;
using Borea.Core.Instances;

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

    /// <summary>A harness whose game folder holds the version fixture, so a preset can record a version.</summary>
    private static Task<ViewModelHarness> CreateAsync() =>
        ViewModelHarness.CreateAsync(services =>
        {
            var game = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Game")).FullName;
            File.Copy(Path.Combine(AppContext.BaseDirectory, GameBuild), Path.Combine(game, "KSA.dll"), overwrite: true);
            return services.SettingsRepository.SaveAsync(services.Settings.WithGameDirectory(game));
        });

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
