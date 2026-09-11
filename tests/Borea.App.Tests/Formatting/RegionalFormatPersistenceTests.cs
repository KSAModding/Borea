using System.Globalization;
using Borea.App.Formatting;
using Borea.App.Localization;
using Borea.App.ViewModels;
using Borea.Core.Preferences;

namespace Borea.App.Tests.Formatting;

public sealed class RegionalFormatPersistenceTests : IDisposable
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    [Fact]
    public void SelectedRegionalFormat_PreservesOtherPreferencesWhenItSaves()
    {
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("en"));
        var formatService = new RegionalFormatService(
            localization,
            CultureInfo.GetCultureInfo("fr-FR"),
            "en-US");
        var customTheme = new CustomThemePreference("Mission", "#102030", "#405060", "#708090", "#abcdef");
        var preferences = new AppPreferences("Mission", [customTheme], "en-US");
        var repository = new RecordingAppPreferencesRepository();
        var viewModel = new MainViewModel(localization, formatService, repository, preferences);
        viewModel.SelectedRegionalFormat = Assert.Single(
            formatService.SupportedFormats,
            format => format.CultureName == "de-DE");

        Assert.NotNull(repository.SavedPreferences);
        Assert.Equal("de-DE", repository.SavedPreferences.RegionalCultureName);
        Assert.Equal("Mission", repository.SavedPreferences.SelectedThemeName);
        Assert.Single(repository.SavedPreferences.CustomThemes);
        Assert.Equal("en", CultureInfo.CurrentUICulture.Name);
        Assert.Null(viewModel.PreferenceSaveError);
    }

    [Fact]
    public void SelectedRegionalFormat_WriteFailure_ReportsTheError()
    {
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("en"));
        var formatService = new RegionalFormatService(
            localization,
            CultureInfo.GetCultureInfo("fr-FR"),
            selectedCultureName: null);
        var repository = new RecordingAppPreferencesRepository
        {
            SaveException = new IOException("Disk unavailable."),
        };
        var viewModel = new MainViewModel(localization, formatService, repository, AppPreferences.Empty);
        viewModel.SelectedRegionalFormat = Assert.Single(
            formatService.SupportedFormats,
            format => format.CultureName == "de-DE");

        Assert.Equal("The preference could not be saved: Disk unavailable.", viewModel.PreferenceSaveError);

        repository.SaveException = null;
        viewModel.SelectedRegionalFormat = formatService.SupportedFormats[0];

        Assert.Null(viewModel.PreferenceSaveError);
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
        Resources.Culture = _originalUiCulture;
    }

    private sealed class RecordingAppPreferencesRepository : IAppPreferencesRepository
    {
        public AppPreferences? SavedPreferences { get; private set; }

        public Exception? SaveException { get; set; }

        public Task<AppPreferencesLoadResult> GetAsync(
            IReadOnlyCollection<string> bundledThemeNames,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AppPreferencesLoadResult(AppPreferencesLoadStatus.NotFound, AppPreferences.Empty));

        public Task SaveAsync(
            AppPreferences preferences,
            IReadOnlyCollection<string> bundledThemeNames,
            CancellationToken cancellationToken = default)
        {
            if (SaveException is not null)
                throw SaveException;

            SavedPreferences = preferences;
            return Task.CompletedTask;
        }
    }
}
