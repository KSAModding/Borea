using System.Text.Json;
using Borea.Core.Preferences;
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
    public async Task SaveThenGet_SavedBundledTheme_RestoresTheSelection()
    {
        await _repository.SaveAsync(new AppPreferences("Dark"), BundledThemeNames);

        var result = await _repository.GetAsync(BundledThemeNames);

        Assert.Equal(AppPreferencesLoadStatus.Loaded, result.Status);
        Assert.Equal("Dark", result.Preferences.ResolveSelectedThemeName(BundledThemeNames, "Borealis"));
        Assert.Empty(result.Preferences.CustomThemes);
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
        Assert.Equal("Borealis", result.Preferences.ResolveSelectedThemeName(BundledThemeNames, "Borealis"));
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
        await _repository.SaveAsync(new AppPreferences("Mission", [customTheme]), BundledThemeNames);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(_pathProvider.GetAppPreferencesPath()));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("Mission", root.GetProperty("selectedTheme").GetString());
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
