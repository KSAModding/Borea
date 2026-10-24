using System.Globalization;
using System.Text;
using Borea.App.Localization;
using Borea.App.ViewModels;

namespace Borea.App.Tests.Localization;

public sealed class TranslatorCreditsTests
{
    [Fact]
    public void EmbeddedCredits_AppearWhenTheirLanguageShips()
    {
        var languages = new[] { new SupportedCulture("zh-Hans", "\u7b80\u4f53\u4e2d\u6587") };

        var group = Assert.Single(TranslatorCreditsCatalog.ForLanguages(languages));

        Assert.Same(languages[0], group.Language);
        var credit = Assert.Single(group.Translators);
        Assert.Equal("kuxiaowo", credit.Name);
        Assert.Equal("https://github.com/kuxiaowo", credit.Url);
        Assert.True(credit.HasUrl);
        Assert.False(group.IsOriginalLanguage);
        Assert.False(group.IsUncredited);
    }

    [Fact]
    public void Read_PreservesMultipleNamesUnicodeAndOptionalLinks()
    {
        using var stream = Json("""{"de":[{"name":"First","url":"https://example.com/first"},{"name":"\u4e2d\u6587\u8bd1\u8005"}]}""");

        var credits = TranslatorCreditsCatalog.Read(stream)["DE"];

        Assert.Equal(["First", "\u4e2d\u6587\u8bd1\u8005"], credits.Select(credit => credit.Name));
        Assert.True(credits[0].HasUrl);
        Assert.Null(credits[1].Url);
        Assert.False(credits[1].HasUrl);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""{"de":null}""")]
    [InlineData("""{" ":[]}""")]
    [InlineData("""{"de":[null]}""")]
    [InlineData("""{"de":[{"name":" "}]}""")]
    [InlineData("""{"de":[{"name":"First","url":"javascript:alert(1)"}]}""")]
    [InlineData("""{"de":[{"name":"First","url":"http://example.com"}]}""")]
    [InlineData("""{"de":[],"DE":[]}""")]
    [InlineData("""{"de":[],"de":[]}""")]
    public void Read_RejectsInvalidCreditMetadata(string json)
    {
        using var stream = Json(json);

        Assert.Throws<InvalidDataException>(() => TranslatorCreditsCatalog.Read(stream));
    }

    [Fact]
    public void EveryBuiltInLanguageHasARowEvenBeforeNamesAreRecorded()
    {
        var groups = TranslatorCreditsCatalog.ForLanguages(LocalizationService.BuiltInCultures);

        Assert.Equal(LocalizationService.BuiltInCultures, groups.Select(group => group.Language));
        Assert.True(groups.Single(group => group.Language.Name == "en").IsOriginalLanguage);
        Assert.All(groups.Where(group => group.Language.Name != "en" && group.Translators.Count == 0),
            group => Assert.True(group.IsUncredited));
    }

    [Fact]
    public void LocalPreviewLanguage_DoesNotBecomeABuiltInTranslatorRow()
    {
        var original = CultureInfo.CurrentUICulture;
        var folder = Path.Combine(Path.GetTempPath(), "BoreaCredits_" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "Resources.fr.resx"),
                """<root><data name="NavigationHome"><value>Accueil</value></data></root>""");
            var files = TranslationFiles.Load(folder, _ => { });
            var localization = new LocalizationService(CultureInfo.GetCultureInfo("fr"), files);

            var viewModel = new MainViewModel(localization);

            Assert.Contains(localization.SupportedCultures, language => language.Name == "fr");
            Assert.DoesNotContain(viewModel.TranslatorCredits, group => group.Language.Name == "fr");
            Assert.Equal(LocalizationService.BuiltInCultures.Select(language => language.Name),
                viewModel.TranslatorCredits.Select(group => group.Language.Name));
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
            Resources.Culture = original;
            TranslationResourceManager.Current.Files = TranslationFiles.None;
            Directory.Delete(folder, recursive: true);
        }
    }

    private static MemoryStream Json(string text) => new(Encoding.UTF8.GetBytes(text));
}
