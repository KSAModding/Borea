using System.Globalization;
using System.Text.Json;
using Borea.App.Formatting;
using Borea.App.Localization;
using Borea.Core.Game;

namespace Borea.App.Tests.Formatting;

public sealed class RegionalFormatServiceTests : IDisposable
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    [Fact]
    public void Constructor_NoSavedCulture_UsesTheSystemFormat()
    {
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("en"));
        var service = new RegionalFormatService(
            localization,
            CultureInfo.GetCultureInfo("fr-FR"),
            selectedCultureName: null);

        Assert.Null(service.SelectedCultureName);
        Assert.Equal("fr-FR", CultureInfo.CurrentCulture.Name);
        Assert.StartsWith("System default", service.SelectedFormat.DisplayName, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_SavedSpecificCulture_RestoresTheFormat()
    {
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("en"));
        var service = new RegionalFormatService(
            localization,
            CultureInfo.GetCultureInfo("fr-FR"),
            "de-DE");

        Assert.Equal("de-DE", service.SelectedCultureName);
        Assert.Equal("de-DE", CultureInfo.CurrentCulture.Name);
    }

    [Theory]
    [InlineData("not-a-culture")]
    [InlineData("de")]
    [InlineData("")]
    public void Constructor_InvalidOrNeutralCulture_FallsBackToTheSystemFormat(string cultureName)
    {
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("en"));
        var service = new RegionalFormatService(
            localization,
            CultureInfo.GetCultureInfo("fr-FR"),
            cultureName);

        Assert.Null(service.SelectedCultureName);
        Assert.Equal("fr-FR", CultureInfo.CurrentCulture.Name);
    }

    [Fact]
    public void SelectedFormat_ChangesRegionalCultureWithoutChangingUiCulture()
    {
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("en"));
        var service = new RegionalFormatService(
            localization,
            CultureInfo.GetCultureInfo("fr-FR"),
            selectedCultureName: null);

        service.SelectedFormat = Assert.Single(
            service.SupportedFormats,
            format => format.CultureName == "de-DE");

        Assert.Equal("de-DE", CultureInfo.CurrentCulture.Name);
        Assert.Equal("en", CultureInfo.CurrentUICulture.Name);
    }

    [Fact]
    public void SelectedFormat_ChangesDisplayValuesWithoutChangingMachineValues()
    {
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("en"));
        var service = new RegionalFormatService(
            localization,
            CultureInfo.GetCultureInfo("en-US"),
            selectedCultureName: null);

        service.SelectedFormat = Assert.Single(
            service.SupportedFormats,
            format => format.CultureName == "de-DE");

        Assert.Equal("1.234,5", 1234.5m.ToString("N1"));
        Assert.Equal("31.12.2026", new DateOnly(2026, 12, 31).ToString("d"));
        Assert.Equal("2026.9.7.5402", new GameVersion(2026, 9, 7, 5402).ToString());
        Assert.Equal("{\"Value\":1234.5}", JsonSerializer.Serialize(new { Value = 1234.5m }));
    }

    [Fact]
    public void SelectedFormat_SystemDefault_RestoresTheCapturedSystemCulture()
    {
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("en"));
        var service = new RegionalFormatService(
            localization,
            CultureInfo.GetCultureInfo("fr-FR"),
            "de-DE");

        service.SelectedFormat = service.SupportedFormats[0];

        Assert.Null(service.SelectedCultureName);
        Assert.Equal("fr-FR", CultureInfo.CurrentCulture.Name);
    }

    [Fact]
    public void LanguageChange_UpdatesTheSystemOptionWithoutChangingTheFormat()
    {
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("en"));
        var service = new RegionalFormatService(
            localization,
            CultureInfo.GetCultureInfo("fr-FR"),
            selectedCultureName: null);

        localization.TrySetCulture("de");

        Assert.StartsWith("Systemstandard", service.SelectedFormat.DisplayName, StringComparison.Ordinal);
        Assert.Null(service.SelectedCultureName);
        Assert.Equal("fr-FR", CultureInfo.CurrentCulture.Name);
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
        Resources.Culture = _originalUiCulture;
    }
}
