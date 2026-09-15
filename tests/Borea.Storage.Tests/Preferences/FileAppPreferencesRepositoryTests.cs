using System.Text.Json;
using Borea.Core.Preferences;
using Borea.Core.Updates;
using Borea.Storage.Preferences;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Preferences;

public sealed class FileAppPreferencesRepositoryTests : IDisposable
{
    private static readonly string[] BundledThemeNames = ["Borealis", "Light", "Dark"];

    private readonly string _tempRoot;
    private readonly TestGamePathProvider _pathProvider;
    private readonly FileAppPreferencesRepository _repository;

    public FileAppPreferencesRepositoryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
        _pathProvider = new TestGamePathProvider(_tempRoot);
        _repository = new FileAppPreferencesRepository(_pathProvider);
    }

    [Fact]
    public async Task GetAsync_NoSavedPreferences_ReturnsTheDefaultThemeSafely()
    {
        var result = await _repository.GetAsync(BundledThemeNames);

        Assert.Equal(AppPreferencesLoadStatus.NotFound, result.Status);
        Assert.Equal("Borealis", result.Preferences.ResolveSelectedThemeName(BundledThemeNames, "Borealis"));
    }

    [Fact]
    public async Task GetAsync_NoSavedPreferences_ChecksForUpdatesAtStart()
    {
        var result = await _repository.GetAsync(BundledThemeNames);

        Assert.True(result.Preferences.CheckForUpdatesAtStart);
        Assert.True(AppPreferences.Empty.CheckForUpdatesAtStart);
    }

    [Fact]
    public async Task SaveThenGet_UpdateCheckTurnedOff_RestoresTheChoice()
    {
        await _repository.SaveAsync(new AppPreferences("Dark", checkForUpdatesAtStart: false), BundledThemeNames);

        var result = await _repository.GetAsync(BundledThemeNames);

        Assert.Equal(AppPreferencesLoadStatus.Loaded, result.Status);
        Assert.False(result.Preferences.CheckForUpdatesAtStart);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(_pathProvider.GetAppPreferencesPath()));
        Assert.Equal(1, document.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.False(document.RootElement.GetProperty("checkForUpdatesAtStart").GetBoolean());
    }

    [Fact]
    public async Task GetAsync_FileWrittenBeforeTheUpdateCheckField_LoadsWithTheCheckOn()
    {
        await WriteAsync("""
            {
              "formatVersion": 1,
              "selectedTheme": "Light",
              "regionalCulture": "de-DE",
              "uiCulture": "de",
              "customThemes": []
            }
            """);

        var result = await _repository.GetAsync(BundledThemeNames);

        Assert.Equal(AppPreferencesLoadStatus.Loaded, result.Status);
        Assert.Equal("Light", result.Preferences.SelectedThemeName);
        Assert.True(result.Preferences.CheckForUpdatesAtStart);
    }

    [Fact]
    public async Task SaveThenGet_ForeignFolderDeletionConfirmed_RestoresTheChoice()
    {
        Assert.False(AppPreferences.Empty.ForeignFolderDeletionConfirmed);
        var preferences = AppPreferences.Empty.WithForeignFolderDeletionConfirmed(true).WithSelectedThemeName("Dark");

        await _repository.SaveAsync(preferences, BundledThemeNames);
        var result = await _repository.GetAsync(BundledThemeNames);

        Assert.True(result.Preferences.ForeignFolderDeletionConfirmed);
        Assert.Equal("Dark", result.Preferences.SelectedThemeName);
    }

    [Fact]
    public async Task SaveThenGet_ImagesFromAuthorHostsTurnedOff_RestoresTheChoice()
    {
        Assert.True(AppPreferences.Empty.LoadImagesFromAuthorHosts);
        var preferences = AppPreferences.Empty.WithLoadImagesFromAuthorHosts(false).WithSelectedThemeName("Dark");

        await _repository.SaveAsync(preferences, BundledThemeNames);
        var result = await _repository.GetAsync(BundledThemeNames);

        Assert.False(result.Preferences.LoadImagesFromAuthorHosts);
        Assert.Equal("Dark", result.Preferences.SelectedThemeName);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(_pathProvider.GetAppPreferencesPath()));
        Assert.False(document.RootElement.GetProperty("loadImagesFromAuthorHosts").GetBoolean());
    }

    [Fact]
    public async Task GetAsync_FileWrittenBeforeTheImageField_LoadsWithImagesFromAuthorHostsOn()
    {
        await WriteAsync("""
            { "formatVersion": 1, "selectedTheme": "Light" }
            """);

        var result = await _repository.GetAsync(BundledThemeNames);

        Assert.Equal(AppPreferencesLoadStatus.Loaded, result.Status);
        Assert.True(result.Preferences.LoadImagesFromAuthorHosts);
    }

    [Fact]
    public async Task SaveThenGet_HomeLaunchWithoutModLoader_RestoresTheChoice()
    {
        Assert.Equal(HomeLaunchOption.ActiveInstance, AppPreferences.Empty.HomeLaunch);

        await _repository.SaveAsync(AppPreferences.Empty.WithHomeLaunch(HomeLaunchOption.WithoutModLoader), BundledThemeNames);
        var result = await _repository.GetAsync(BundledThemeNames);

        Assert.Equal(HomeLaunchOption.WithoutModLoader, result.Preferences.HomeLaunch);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(_pathProvider.GetAppPreferencesPath()));
        Assert.Equal("without-mod-loader", document.RootElement.GetProperty("homeLaunch").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(""", "homeLaunch": "somewhere-else" """)]
    public async Task GetAsync_NoOrUnknownHomeLaunch_LoadsAsTheActiveInstance(string homeLaunch)
    {
        await WriteAsync($$"""
            { "formatVersion": 1, "selectedTheme": "Light"{{homeLaunch}} }
            """);

        var result = await _repository.GetAsync(BundledThemeNames);

        Assert.Equal(AppPreferencesLoadStatus.Loaded, result.Status);
        Assert.Equal(HomeLaunchOption.ActiveInstance, result.Preferences.HomeLaunch);
    }

    [Fact]
    public void With_OtherPreferenceChanges_KeepTheUpdateCheckChoice()
    {
        var preferences = new AppPreferences("Dark", checkForUpdatesAtStart: false)
            .WithSelectedThemeName("Light")
            .WithRegionalCultureName("de-DE")
            .WithUiCultureName("de");

        Assert.False(preferences.CheckForUpdatesAtStart);
        Assert.True(preferences.WithCheckForUpdatesAtStart(true).CheckForUpdatesAtStart);
    }

    [Theory]
    [InlineData(BoreaUpdateChannel.Testing, "testing")]
    [InlineData(BoreaUpdateChannel.Dev, "dev")]
    public async Task SaveThenGet_UpdateChannel_RestoresTheChoice(BoreaUpdateChannel channel, string name)
    {
        await _repository.SaveAsync(AppPreferences.Empty.WithUpdateChannel(channel), BundledThemeNames);

        var result = await _repository.GetAsync(BundledThemeNames);

        Assert.Equal(AppPreferencesLoadStatus.Loaded, result.Status);
        Assert.Equal(channel, result.Preferences.UpdateChannel);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(_pathProvider.GetAppPreferencesPath()));
        Assert.Equal(name, document.RootElement.GetProperty("updateChannel").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(""", "updateChannel": null""")]
    [InlineData(""", "updateChannel": "nightly" """)]
    public async Task GetAsync_NoOrUnknownUpdateChannel_LoadsAsStable(string updateChannel)
    {
        await WriteAsync($$"""
            { "formatVersion": 1, "selectedTheme": "Light"{{updateChannel}} }
            """);

        var result = await _repository.GetAsync(BundledThemeNames);

        Assert.Equal(AppPreferencesLoadStatus.Loaded, result.Status);
        Assert.Equal("Light", result.Preferences.SelectedThemeName);
        Assert.Equal(BoreaUpdateChannel.Stable, result.Preferences.UpdateChannel);
    }

    [Fact]
    public void With_OtherPreferenceChanges_KeepTheUpdateChannel()
    {
        var preferences = AppPreferences.Empty.WithUpdateChannel(BoreaUpdateChannel.Dev)
            .WithSelectedThemeName("Light")
            .WithRegionalCultureName("de-DE")
            .WithUiCultureName("de")
            .WithCheckForUpdatesAtStart(false);

        Assert.Equal(BoreaUpdateChannel.Dev, preferences.UpdateChannel);
        Assert.Equal(BoreaUpdateChannel.Stable, AppPreferences.Empty.UpdateChannel);
    }

    [Fact]
    public void With_OtherPreferenceChanges_KeepImagesFromAuthorHostsOff()
    {
        var preferences = AppPreferences.Empty.WithLoadImagesFromAuthorHosts(false)
            .WithSelectedThemeName("Light")
            .WithRegionalCultureName("de-DE")
            .WithUiCultureName("de")
            .WithCheckForUpdatesAtStart(false)
            .WithUpdateChannel(BoreaUpdateChannel.Dev)
            .WithForeignFolderDeletionConfirmed(true);

        Assert.False(preferences.LoadImagesFromAuthorHosts);
    }

    [Fact]
    public void With_OtherPreferenceChanges_KeepTheHomeLaunch()
    {
        var preferences = AppPreferences.Empty.WithHomeLaunch(HomeLaunchOption.WithoutModLoader)
            .WithSelectedThemeName("Light")
            .WithRegionalCultureName("de-DE")
            .WithUiCultureName("de")
            .WithCheckForUpdatesAtStart(false)
            .WithUpdateChannel(BoreaUpdateChannel.Dev)
            .WithForeignFolderDeletionConfirmed(true)
            .WithLoadImagesFromAuthorHosts(false);

        Assert.Equal(HomeLaunchOption.WithoutModLoader, preferences.HomeLaunch);
    }

    [Fact]
    public async Task SaveThenGet_SavedBundledTheme_RestoresTheSelection()
    {
        await _repository.SaveAsync(new AppPreferences("Dark"), BundledThemeNames);

        var result = await _repository.GetAsync(BundledThemeNames);

        Assert.Equal(AppPreferencesLoadStatus.Loaded, result.Status);
        Assert.Equal("Dark", result.Preferences.ResolveSelectedThemeName(BundledThemeNames, "Borealis"));
        Assert.Empty(result.Preferences.CustomThemes);
    }

    [Fact]
    public async Task SaveThenGet_SavedUiCulture_RestoresTheSelection()
    {
        await _repository.SaveAsync(new AppPreferences("Borealis", uiCultureName: "de"), BundledThemeNames);

        var result = await _repository.GetAsync(BundledThemeNames);

        Assert.Equal(AppPreferencesLoadStatus.Loaded, result.Status);
        Assert.Equal("de", result.Preferences.UiCultureName);
    }

    [Fact]
    public async Task GetAsync_UnknownUiCulture_KeepsOtherPreferences()
    {
        await WriteAsync("""
            { "formatVersion": 1, "selectedTheme": "Light", "uiCulture": "not-a-culture-xx" }
            """);

        var result = await _repository.GetAsync(BundledThemeNames);

        Assert.Equal(AppPreferencesLoadStatus.Loaded, result.Status);
        Assert.Equal("Light", result.Preferences.SelectedThemeName);
        Assert.Null(result.Preferences.UiCultureName);
    }

    [Fact]
    public async Task SaveThenGet_SavedCustomTheme_RestoresTheThemeAndSelection()
    {
        var customTheme = new CustomThemePreference("Mission", "#102030", "#405060", "#708090", "#abcdef");
        await _repository.SaveAsync(new AppPreferences("Mission", [customTheme]), BundledThemeNames);

        var result = await _repository.GetAsync(BundledThemeNames);

        Assert.Equal(AppPreferencesLoadStatus.Loaded, result.Status);
        Assert.Equal("Mission", result.Preferences.ResolveSelectedThemeName(BundledThemeNames, "Borealis"));
        var restored = Assert.Single(result.Preferences.CustomThemes);
        Assert.Equal("Mission", restored.Name);
        Assert.Equal("#102030", restored.MainColor);
        Assert.Equal("#405060", restored.SecondaryColor);
        Assert.Equal("#708090", restored.GlobalPanelsColor);
        Assert.Equal("#abcdef", restored.TextColor);
    }

    [Fact]
    public async Task SaveThenGet_SavedRegionalCulture_RestoresTheSelection()
    {
        await _repository.SaveAsync(
            new AppPreferences("Dark", regionalCultureName: "de-DE"),
            BundledThemeNames);

        var result = await _repository.GetAsync(BundledThemeNames);

        Assert.Equal(AppPreferencesLoadStatus.Loaded, result.Status);
        Assert.Equal("de-DE", result.Preferences.RegionalCultureName);
        Assert.Equal("Dark", result.Preferences.SelectedThemeName);
    }

    [Fact]
    public async Task GetAsync_InvalidJson_ReturnsInvalidAndTheDefaultThemeSafely()
    {
        await WriteAsync("{ not-json }");

        var result = await _repository.GetAsync(BundledThemeNames);

        Assert.Equal(AppPreferencesLoadStatus.Invalid, result.Status);
        Assert.NotNull(result.Error);
        Assert.Equal("Borealis", result.Preferences.ResolveSelectedThemeName(BundledThemeNames, "Borealis"));
    }

    [Fact]
    public async Task GetAsync_UnknownSelectedTheme_LoadsAndUsesTheDefaultTheme()
    {
        await WriteAsync("""
            {
              "formatVersion": 1,
              "selectedTheme": "Removed Theme",
              "customThemes": []
            }
            """);

        var result = await _repository.GetAsync(BundledThemeNames);

        Assert.Equal(AppPreferencesLoadStatus.Loaded, result.Status);
        Assert.Equal("Removed Theme", result.Preferences.SelectedThemeName);
        Assert.Null(result.Preferences.RegionalCultureName);
        Assert.Equal("Borealis", result.Preferences.ResolveSelectedThemeName(BundledThemeNames, "Borealis"));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("de")]
    [InlineData("not-a-culture")]
    public async Task GetAsync_InvalidRegionalCulture_KeepsOtherPreferences(string regionalCulture)
    {
        await WriteAsync($$"""
            {
              "formatVersion": 1,
              "selectedTheme": "Mission",
              "regionalCulture": "{{regionalCulture}}",
              "customThemes": [
                { "name": "Mission", "mainColor": "#102030", "secondaryColor": "#405060", "globalPanelsColor": "#708090", "textColor": "#abcdef" }
              ]
            }
            """);

        var result = await _repository.GetAsync(BundledThemeNames);

        Assert.Equal(AppPreferencesLoadStatus.Loaded, result.Status);
        Assert.Null(result.Preferences.RegionalCultureName);
        Assert.Equal("Mission", result.Preferences.SelectedThemeName);
        Assert.Equal("Mission", Assert.Single(result.Preferences.CustomThemes).Name);
    }

    [Fact]
    public async Task GetAsync_PreferencePathIsADirectory_ReturnsUnavailable()
    {
        Directory.CreateDirectory(_pathProvider.GetAppPreferencesPath());

        var result = await _repository.GetAsync(BundledThemeNames);

        Assert.Equal(AppPreferencesLoadStatus.Unavailable, result.Status);
        Assert.NotNull(result.Error);
        Assert.Equal("Borealis", result.Preferences.ResolveSelectedThemeName(BundledThemeNames, "Borealis"));
    }

    [Theory]
    [MemberData(nameof(InvalidDocuments))]
    public async Task GetAsync_InvalidPreferenceDocument_ReturnsInvalid(string document)
    {
        await WriteAsync(document);

        var result = await _repository.GetAsync(BundledThemeNames);

        Assert.Equal(AppPreferencesLoadStatus.Invalid, result.Status);
        Assert.Empty(result.Preferences.CustomThemes);
    }

    [Fact]
    public async Task SaveAsync_WritesTheVersionedExplicitFormat()
    {
        var customTheme = new CustomThemePreference("Mission", "#102030", "#405060", "#708090", "#abcdef");
        await _repository.SaveAsync(
            new AppPreferences("Mission", [customTheme], regionalCultureName: "en-GB"),
            BundledThemeNames);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(_pathProvider.GetAppPreferencesPath()));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("Mission", root.GetProperty("selectedTheme").GetString());
        Assert.Equal("en-GB", root.GetProperty("regionalCulture").GetString());
        var savedTheme = Assert.Single(root.GetProperty("customThemes").EnumerateArray());
        Assert.Equal("Mission", savedTheme.GetProperty("name").GetString());
        Assert.Equal("#102030", savedTheme.GetProperty("mainColor").GetString());
        Assert.False(root.TryGetProperty("bundledThemes", out _));
    }

    [Fact]
    public async Task SaveAsync_CustomThemeConflictsWithBundledTheme_ThrowsWithoutWriting()
    {
        var customTheme = new CustomThemePreference("Dark", "#102030", "#405060", "#708090", "#abcdef");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repository.SaveAsync(new AppPreferences("Dark", [customTheme]), BundledThemeNames));

        Assert.False(File.Exists(_pathProvider.GetAppPreferencesPath()));
    }

    public static TheoryData<string> InvalidDocuments => new()
    {
        """
        {
          "formatVersion": 2,
          "selectedTheme": "Dark",
          "customThemes": []
        }
        """,
        """
        {
          "formatVersion": 1,
          "formatVersion": 1,
          "customThemes": []
        }
        """,
        """
        {
          "formatVersion": 1,
          "customThemes": [
            { "name": "Mission", "mainColor": "#102030", "secondaryColor": "#405060", "globalPanelsColor": "#708090", "textColor": "#abcdef" },
            { "name": "Mission", "mainColor": "#102030", "secondaryColor": "#405060", "globalPanelsColor": "#708090", "textColor": "#abcdef" }
          ]
        }
        """,
        """
        {
          "formatVersion": 1,
          "customThemes": [
            { "name": "Dark", "mainColor": "#102030", "secondaryColor": "#405060", "globalPanelsColor": "#708090", "textColor": "#abcdef" }
          ]
        }
        """,
        """
        {
          "formatVersion": 1,
          "customThemes": [
            { "name": "Mission", "mainColor": "red", "secondaryColor": "#405060", "globalPanelsColor": "#708090", "textColor": "#abcdef" }
          ]
        }
        """,
        """
        {
          "formatVersion": 1,
          "customThemes": [null]
        }
        """,
        """
        {
          "formatVersion": 1,
          "customThemes": [],
          "unknown": true
        }
        """,
    };

    private async Task WriteAsync(string document)
    {
        Directory.CreateDirectory(_tempRoot);
        await File.WriteAllTextAsync(_pathProvider.GetAppPreferencesPath(), document);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
