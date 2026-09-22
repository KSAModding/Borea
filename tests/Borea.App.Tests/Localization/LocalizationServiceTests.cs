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
    public void FormatGameShapeBroken_NoBuildFound_NamesTheInstallation()
    {
        var service = new LocalizationService(CultureInfo.GetCultureInfo("en"));

        Assert.StartsWith("This KSA installation looks different", service.FormatGameShapeBroken(null, "x"));
        Assert.StartsWith("KSA 2026.9.10.5438 looks different", service.FormatGameShapeBroken("2026.9.10.5438", "x"));
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
    public void SupportedCultures_ListsEnglishGermanAndPirate()
    {
        var service = new LocalizationService(CultureInfo.GetCultureInfo("en"));

        Assert.Collection(
            service.SupportedCultures,
            culture => Assert.Equal("en", culture.Name),
            culture => Assert.Equal("de", culture.Name),
            culture => Assert.Equal(("en-QP", "Pirate speak"), (culture.Name, culture.DisplayName)));
    }

    [Fact]
    public void TrySetCulture_Pirate_UsesPirateResourcesAndKeepsTheRegionalCulture()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        var service = new LocalizationService(CultureInfo.GetCultureInfo("en"));

        var changed = service.TrySetCulture("en-QP");

        Assert.True(changed);
        Assert.Equal("en-QP", service.SelectedCultureName);
        Assert.Equal("en-QP", CultureInfo.CurrentUICulture.Name);
        Assert.Equal("fr-FR", CultureInfo.CurrentCulture.Name);
        Assert.Equal("Port", service.NavigationHome);
        Assert.Equal("Ain't able to take a gander at: Borea.App.Views.UnknownView", service.FormatViewNotFound("Borea.App.Views.UnknownView"));
    }

    [Fact]
    public void Constructor_EnglishSystemCulture_DoesNotChoosePirate()
    {
        var service = new LocalizationService(CultureInfo.GetCultureInfo("en-GB"));

        Assert.Equal("en", service.SelectedCultureName);
    }

    [Theory]
    [InlineData("en", "Alpha is not copied, because Borea did not install this folder.", "These mods are not copied, because Borea did not install their folders: Alpha, Beta, Gamma")]
    [InlineData("de", "Alpha wird nicht kopiert, weil Borea diesen Ordner nicht installiert hat.", "Diese Mods werden nicht kopiert, weil Borea ihre Ordner nicht installiert hat: Alpha, Beta, Gamma")]
    public void FormatModListNotCopied_NamesOneFolderInASentenceAndSeveralInOneLine(string culture, string one, string several)
    {
        var service = new LocalizationService(CultureInfo.GetCultureInfo(culture));

        Assert.Equal(one, service.FormatModListNotCopied(["Alpha"]));
        Assert.Equal(several, service.FormatModListNotCopied(["Alpha", "Beta", "Gamma"]));
    }

    [Fact]
    public void FormatModListNotCopied_WithoutFolders_IsEmpty()
    {
        var service = new LocalizationService(CultureInfo.GetCultureInfo("en"));

        Assert.Equal(string.Empty, service.FormatModListNotCopied([]));
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
