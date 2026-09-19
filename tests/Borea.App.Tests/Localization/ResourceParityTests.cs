using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Borea.App.Tests.Localization;

public sealed partial class ResourceParityTests
{
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

    [Fact]
    public void GermanResources_HaveTheSameKeysAsNeutralResources()
    {
        var neutral = ReadValues("Resources.resx");
        var german = ReadValues("Resources.de.resx");

        Assert.Equal(neutral.Keys.Order(StringComparer.Ordinal), german.Keys.Order(StringComparer.Ordinal));
    }

    // Other translations may miss a key, which then shows in English.
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
