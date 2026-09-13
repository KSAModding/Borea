using System.Globalization;
using Borea.App.Localization;

namespace Borea.App.Tests.Localization;

public sealed class LocalizationSurfaceTests : IDisposable
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    public static TheoryData<string> Cultures => new() { "en", "de" };

    [Theory]
    [MemberData(nameof(Cultures))]
    public void EveryTextProperty_HasAValue(string culture)
    {
        var service = new LocalizationService(CultureInfo.GetCultureInfo(culture));
        var properties = typeof(LocalizationService).GetProperties()
            .Where(property => property.PropertyType == typeof(string) && property.Name != nameof(LocalizationService.SelectedCultureName));

        Assert.All(properties, property =>
            Assert.False(string.IsNullOrWhiteSpace((string?)property.GetValue(service)), property.Name));
    }

    [Fact]
    public void FormatMethods_FillTheirPlaceholders()
    {
        var service = new LocalizationService(CultureInfo.GetCultureInfo("en"));

        Assert.Equal("by Maxi, Renan", service.FormatContentByAuthor("Maxi, Renan"));
        Assert.Equal("Listed on SpaceDock", service.FormatContentSource("SpaceDock"));
        Assert.Equal("Installed StarMap 0.4.6 to loaders.", service.FormatSetupLoaderInstalled("StarMap", "0.4.6", "loaders"));
        Assert.Throws<ArgumentException>(() => service.FormatContentByAuthor(" "));
        Assert.Throws<ArgumentException>(() => service.FormatContentSource(""));
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
        Resources.Culture = _originalUiCulture;
    }
}
