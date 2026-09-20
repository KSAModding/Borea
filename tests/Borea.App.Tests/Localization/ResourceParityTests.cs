using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Borea.App.Localization;

namespace Borea.App.Tests.Localization;

public sealed partial class ResourceParityTests : IDisposable
{
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    private static readonly string LocalizationDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Borea.App", "Localization", "Resources"));

    public static TheoryData<string> Translations
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var path in Directory.GetFiles(LocalizationDirectory, "Resources.*.resx").Order(StringComparer.Ordinal))
                data.Add(Path.GetFileName(path));
            return data;
        }
    }

    // A translation may miss a key, which then shows in English.
    [Theory]
    [MemberData(nameof(Translations))]
    public void Translation_HasNoKeyThatNeutralResourcesLack(string fileName)
    {
        var neutral = ReadValues("Resources.resx");
        var translation = ReadValues(fileName);

        Assert.All(translation.Keys, key => Assert.True(neutral.ContainsKey(key), $"{fileName} has the key {key}, which Resources.resx does not have."));
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void Translation_HasTheSamePlaceholdersAsNeutralResources(string fileName)
    {
        var neutral = ReadValues("Resources.resx");
        var translation = ReadValues(fileName);

        Assert.All(neutral.Keys.Where(translation.ContainsKey), key =>
            Assert.True(
                Placeholders(neutral[key]).SequenceEqual(Placeholders(translation[key]))
                    && Braces(neutral[key]) == Braces(translation[key]),
                $"{key}: \"{translation[key]}\" does not have the placeholders of \"{neutral[key]}\""));
    }

    // A key the file has shows its own text, and a key it misses shows the English one,
    // which is what lets a translation stay incomplete.
    [Fact]
    public void Translation_ShowsItsOwnTextOrTheNeutralEnglishOne()
    {
        var neutral = ReadValues("Resources.resx");
        var pirate = ReadValues("Resources.en-QP.resx");

        _ = new LocalizationService(CultureInfo.GetCultureInfo("en-QP"));

        Assert.All(neutral.Keys, key => Assert.Equal(
            pirate.TryGetValue(key, out var translated) ? translated : neutral[key],
            Resources.ResourceManager.GetString(key, CultureInfo.CurrentUICulture)));
    }

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalUiCulture;
        Resources.Culture = _originalUiCulture;
    }

    private static Dictionary<string, string> ReadValues(string fileName)
        => XDocument.Load(Path.Combine(LocalizationDirectory, fileName))
            .Root!
            .Elements("data")
            .ToDictionary(element => (string)element.Attribute("name")!, element => (string?)element.Element("value") ?? string.Empty);

    private static string[] Placeholders(string text)
        => PlaceholderPattern().Matches(text).Select(match => match.Value).Order(StringComparer.Ordinal).ToArray();

    private static (int Open, int Close) Braces(string text)
        => (text.Count(character => character == '{'), text.Count(character => character == '}'));

    [GeneratedRegex(@"\{\d+(?:,-?\d+)?(?::[^}]*)?\}")]
    private static partial Regex PlaceholderPattern();
}
