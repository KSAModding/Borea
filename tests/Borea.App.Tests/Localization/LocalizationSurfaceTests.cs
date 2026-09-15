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
        Assert.Equal("Published 2 days ago", service.FormatContentPublished("2 days ago"));
        Assert.Equal("Installed StarMap 0.4.6 to loaders.", service.FormatSetupLoaderInstalled("StarMap", "0.4.6", "loaders"));
        Assert.Equal("just now", service.FormatTimeAgo(TimeSpan.FromSeconds(-5)));
        Assert.Equal("1 minute ago", service.FormatTimeAgo(TimeSpan.FromSeconds(90)));
        Assert.Equal("5 hours ago", service.FormatTimeAgo(TimeSpan.FromHours(5.5)));
        Assert.Equal("2 days ago", service.FormatTimeAgo(TimeSpan.FromDays(2)));
        Assert.Equal("30 days ago", service.FormatTimeAgo(TimeSpan.FromDays(30)));
        Assert.Equal("1 month ago", service.FormatTimeAgo(TimeSpan.FromDays(45)));
        Assert.Equal("11 months ago", service.FormatTimeAgo(TimeSpan.FromDays(364)));
        Assert.Equal("2 years ago", service.FormatTimeAgo(TimeSpan.FromDays(800)));
        Assert.Throws<ArgumentException>(() => service.FormatContentByAuthor(" "));
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
        Resources.Culture = _originalUiCulture;
    }
}
