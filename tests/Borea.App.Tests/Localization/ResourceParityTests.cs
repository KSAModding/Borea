using System.Xml.Linq;

namespace Borea.App.Tests.Localization;

public sealed class ResourceParityTests
{
    [Fact]
    public void GermanResources_HaveTheSameKeysAsNeutralResources()
    {
        var localizationDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Borea.App", "Localization", "Resources"));

        var neutralKeys = ReadKeys(Path.Combine(localizationDirectory, "Resources.resx"));
        var germanKeys = ReadKeys(Path.Combine(localizationDirectory, "Resources.de.resx"));

        Assert.Equal(neutralKeys, germanKeys);
    }

    private static string[] ReadKeys(string path)
        => XDocument.Load(path)
            .Root!
            .Elements("data")
            .Select(element => (string)element.Attribute("name")!)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
