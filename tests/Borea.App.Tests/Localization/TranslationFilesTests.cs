using System.ComponentModel;
using System.Globalization;
using System.Xml.Linq;
using Borea.App.Localization;

namespace Borea.App.Tests.Localization;

public sealed class TranslationFilesTests : IDisposable
{
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "BoreaTranslationFiles_" + Guid.NewGuid());

    private readonly List<string> _log = [];

    private static readonly string LocalizationDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Borea.App", "Localization", "Resources"));

    [Fact]
    public void OverrideFile_ChangesATextOfItsCultureAndKeepsTheOthers()
    {
        Write("Resources.de.resx", Entry("NavigationHome", "Heimathafen"));

        var service = new LocalizationService(CultureInfo.GetCultureInfo("de"), Load());

        Assert.Equal("Heimathafen", service.NavigationHome);
        Assert.Equal("Sprache", service.SettingsLanguageLabel);
        Assert.Equal([Path.Combine(_folder, "Resources.de.resx")], service.TranslationFiles.Paths);
        Assert.True(service.TrySetCulture("en"));
        Assert.Equal("Home", service.NavigationHome);
    }

    [Fact]
    public void CultureThatBoreaDoesNotShip_JoinsTheLanguageListWithItsFile()
    {
        Write("Resources.fr.resx", Entry("NavigationHome", "Accueil"));
        var service = new LocalizationService(CultureInfo.GetCultureInfo("en"), Load());

        var changed = service.TrySetCulture("fr");

        Assert.True(changed);
        Assert.Equal(["en", "de", "en-QP", "zh-Hans", "fr"], service.SupportedCultures.Select(culture => culture.Name));
        Assert.Equal("Fran" + (char)0xE7 + "ais", service.SupportedCultures[^1].DisplayName);
        Assert.Equal("Accueil", service.NavigationHome);
        Assert.Equal("Language", service.SettingsLanguageLabel);
    }

    [Fact]
    public void SavedCultureOfAFile_ShowsWhileTheFileIsThere()
    {
        Write("Resources.fr.resx", Entry("NavigationHome", "Accueil"));
        var service = new LocalizationService(CultureInfo.GetCultureInfo("de"), Load());

        service.ApplySavedCulture("fr");

        Assert.Equal("fr", service.SelectedCultureName);
        Assert.Equal("Accueil", service.NavigationHome);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("not-a-culture-name")]
    [InlineData(null)]
    public void SavedCultureThatBoreaDoesNotHave_KeepsTheSystemLanguage(string? savedCulture)
    {
        var service = new LocalizationService(CultureInfo.GetCultureInfo("de"), TranslationFiles.None);

        service.ApplySavedCulture(savedCulture);

        Assert.Equal("de", service.SelectedCultureName);
        Assert.Equal("Startseite", service.NavigationHome);
    }

    [Fact]
    public void RefusedTexts_ShowInEnglishAndAreLogged()
    {
        Write(
            "Resources.fr.resx",
            Entry("NavigationHome", "Accueil")
                + Entry("ViewNotFoundFormat", "Vue introuvable : {1}")
                + Entry("NoSuchKey", "Rien"));

        var service = new LocalizationService(CultureInfo.GetCultureInfo("fr"), Load());

        Assert.Equal("Accueil", service.NavigationHome);
        Assert.Equal("View not found: Main", service.FormatViewNotFound("Main"));
        Assert.Single(_log, line => line.Contains("ViewNotFoundFormat", StringComparison.Ordinal) && line.Contains("placeholders", StringComparison.Ordinal));
        Assert.Single(_log, line => line.Contains("NoSuchKey", StringComparison.Ordinal));
    }

    [Fact]
    public void RefusedText_OfAShippedCulture_ShowsTheBuiltTranslation()
    {
        Write("Resources.de.resx", Entry("ViewNotFoundFormat", "Nicht da"));

        var service = new LocalizationService(CultureInfo.GetCultureInfo("de"), Load());

        Assert.Equal("Ansicht nicht gefunden: Main", service.FormatViewNotFound("Main"));
        Assert.Single(_log, line => line.Contains("ViewNotFoundFormat", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateKey_KeepsTheFirstTextAndIsLogged()
    {
        Write("Resources.fr.resx", Entry("NavigationHome", "Accueil") + Entry("NavigationHome", "Maison"));

        var service = new LocalizationService(CultureInfo.GetCultureInfo("fr"), Load());

        Assert.Equal("Accueil", service.NavigationHome);
        Assert.Single(_log, line => line.Contains("NavigationHome", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("<root><data name=\"NavigationHome\"><value>Heimathafen</value></root>")]
    [InlineData("<!DOCTYPE root [<!ENTITY home \"Heimathafen\">]><root><data name=\"NavigationHome\"><value>&home;</value></data></root>")]
    [InlineData("<resources><data name=\"NavigationHome\"><value>Heimathafen</value></data></resources>")]
    public void BrokenFile_IsRefusedWithoutACrash(string content)
    {
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(_folder).FullName, "Resources.de.resx"), content);

        var files = Load();
        var service = new LocalizationService(CultureInfo.GetCultureInfo("de"), files);

        Assert.Empty(files.Paths);
        Assert.Equal("Startseite", service.NavigationHome);
        Assert.Single(_log, line => line.Contains("Resources.de.resx", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("type=\"Borea.App.Tests.Localization.TranslationFilesTests+Tripwire, Borea.App.Tests\"")]
    [InlineData("mimetype=\"application/x-microsoft.net.object.binary.base64\"")]
    public void TypedEntry_RefusesTheFileAndDeserializesNothing(string attribute)
    {
        Write(
            "Resources.de.resx",
            Entry("NavigationHome", "Heimathafen")
                + $"<data name=\"Payload\" {attribute}><value>AAEAAAD/////AQAAAAAAAAAGAQAAAARib29tCw==</value></data>");

        var files = Load();
        var service = new LocalizationService(CultureInfo.GetCultureInfo("de"), files);

        Assert.Empty(files.Paths);
        Assert.Equal("Startseite", service.NavigationHome);
        Assert.False(TripwireConverter.Converted);
        Assert.Single(_log, line => line.Contains("Payload", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_IgnoresFilesWhoseNameHasNoCulture()
    {
        Write("Resources.resx", Entry("NavigationHome", "Home port"));
        Write("Resources.not-a-culture-name.resx", Entry("NavigationHome", "Home port"));
        Write("Strings.fr.resx", Entry("NavigationHome", "Accueil"));

        var files = Load();

        Assert.Empty(files.Paths);
        Assert.Equal(3, _log.Count);
    }

    [Fact]
    public void Load_NoFolder_LoadsNothingAndLogsNothing()
    {
        var files = Load();

        Assert.Same(TranslationFiles.None, files);
        Assert.Empty(_log);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("en-QP")]
    public void MarkMode_MarksExactlyTheTextsThatFallBackToEnglish(string culture)
    {
        var neutral = ReadValues(Path.Combine(LocalizationDirectory, "Resources.resx"));
        var translationPath = Path.Combine(LocalizationDirectory, $"Resources.{culture}.resx");
        var translation = File.Exists(translationPath) ? ReadValues(translationPath) : [];
        var service = new LocalizationService(CultureInfo.GetCultureInfo(culture));

        service.MarkUntranslatedTexts = true;

        Assert.All(neutral.Keys, key => Assert.Equal(
            translation.TryGetValue(key, out var translated) ? translated
                : culture == "en" ? neutral[key]
                : $"** {neutral[key]} **",
            Resources.ResourceManager.GetString(key, CultureInfo.CurrentUICulture)));
    }

    [Fact]
    public void MarkMode_MarksTheTextsThatATranslationFileMissesOrHasRefused()
    {
        Write("Resources.fr.resx", Entry("NavigationHome", "Accueil") + Entry("ViewNotFoundFormat", "Vue introuvable"));
        var service = new LocalizationService(CultureInfo.GetCultureInfo("fr"), Load());

        service.MarkUntranslatedTexts = true;

        Assert.Equal("Accueil", service.NavigationHome);
        Assert.Equal("** Language **", service.SettingsLanguageLabel);
        Assert.Equal("** View not found: Main **", service.FormatViewNotFound("Main"));
    }

    [Fact]
    public void MarkUntranslatedTexts_Change_RefreshesEveryTextAndCanBeTurnedOff()
    {
        Write("Resources.fr.resx", Entry("NavigationHome", "Accueil"));
        var service = new LocalizationService(CultureInfo.GetCultureInfo("fr"), Load());
        var notifications = new List<string?>();
        service.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        service.MarkUntranslatedTexts = true;
        var marked = service.SettingsLanguageLabel;
        service.MarkUntranslatedTexts = false;

        Assert.Equal("** Language **", marked);
        Assert.Equal("Language", service.SettingsLanguageLabel);
        Assert.Equal([string.Empty, string.Empty], notifications);
    }

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalUiCulture;
        Resources.Culture = _originalUiCulture;
        TranslationResourceManager.Current.Files = TranslationFiles.None;
        TranslationResourceManager.Current.MarkUntranslatedTexts = false;
        TripwireConverter.Converted = false;
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private TranslationFiles Load() => TranslationFiles.Load(_folder, _log.Add);

    private void Write(string fileName, string entries)
        => File.WriteAllText(
            Path.Combine(Directory.CreateDirectory(_folder).FullName, fileName),
            $"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<root>\n{entries}</root>\n");

    private static string Entry(string key, string value)
        => new XElement("data", new XAttribute("name", key), new XElement("value", value)) + "\n";

    private static Dictionary<string, string> ReadValues(string path)
        => XDocument.Load(path)
            .Root!
            .Elements("data")
            .ToDictionary(element => (string)element.Attribute("name")!, element => (string?)element.Element("value") ?? string.Empty);

    /// <summary>A type whose converter a reader that deserializes would call for the typed entry.</summary>
    [TypeConverter(typeof(TripwireConverter))]
    public sealed class Tripwire;

    public sealed class TripwireConverter : TypeConverter
    {
        public static bool Converted { get; set; }

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string);

        public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            Converted = true;
            return new Tripwire();
        }
    }
}
