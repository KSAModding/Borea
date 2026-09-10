using System.ComponentModel;
using System.Globalization;
using Borea.App.Localization;

namespace Borea.App.Tests.Localization;

public sealed class LocalizationServiceTests : IDisposable
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    [Fact]
    public void Constructor_EnglishCulture_UsesNeutralEnglishResources()
    {
        var service = new LocalizationService(CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal("en", service.SelectedCultureName);
        Assert.Equal("Home", service.NavigationHome);
    }

    [Fact]
    public void Constructor_GermanRegionalCulture_UsesGermanResources()
    {
        var service = new LocalizationService(CultureInfo.GetCultureInfo("de-DE"));

        Assert.Equal("de", service.SelectedCultureName);
        Assert.Equal("Startseite", service.NavigationHome);
        Assert.Equal("Sprache", service.SettingsLanguageLabel);
    }

    [Fact]
    public void TrySetCulture_SupportedCulture_NotifiesAndChangesActiveValues()
    {
        var service = new LocalizationService(CultureInfo.GetCultureInfo("en"));
        var notifications = new List<PropertyChangedEventArgs>();
        service.PropertyChanged += (_, args) => notifications.Add(args);

        var changed = service.TrySetCulture("de");

        Assert.True(changed);
        Assert.Equal("Startseite", service.NavigationHome);
        Assert.Contains(notifications, notification => notification.PropertyName == string.Empty);
    }

    [Theory]
    [InlineData("not-a-culture")]
    [InlineData("fr-FR")]
    [InlineData("")]
    public void TrySetCulture_UnsupportedCulture_FallsBackToEnglish(string cultureName)
    {
        var service = new LocalizationService(CultureInfo.GetCultureInfo("de"));

        var changed = service.TrySetCulture(cultureName);

        Assert.False(changed);
        Assert.Equal("en", service.SelectedCultureName);
        Assert.Equal("Home", service.NavigationHome);
    }

    [Fact]
    public void TrySetCulture_ChangesUiCultureWithoutChangingRegionalCulture()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        var service = new LocalizationService(CultureInfo.GetCultureInfo("en"));

        service.TrySetCulture("de");

        Assert.Equal("de", CultureInfo.CurrentUICulture.Name);
        Assert.Equal("fr-FR", CultureInfo.CurrentCulture.Name);
    }

    [Fact]
    public void SupportedCultures_ListsEnglishAndGerman()
    {
        var service = new LocalizationService(CultureInfo.GetCultureInfo("en"));

        Assert.Collection(
            service.SupportedCultures,
            culture => Assert.Equal("en", culture.Name),
            culture => Assert.Equal("de", culture.Name));
    }

    [Fact]
    public void FormatViewNotFound_UsesOneFormattedResourceInTheSelectedLanguage()
    {
        var service = new LocalizationService(CultureInfo.GetCultureInfo("de"));

        var message = service.FormatViewNotFound("Borea.App.Views.UnknownView");

        Assert.Equal("Ansicht nicht gefunden: Borea.App.Views.UnknownView", message);
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
        Resources.Culture = _originalUiCulture;
    }
}
