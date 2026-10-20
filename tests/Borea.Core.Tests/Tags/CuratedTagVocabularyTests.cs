using Borea.Core.Mods;
using Borea.Core.Tags;

namespace Borea.Core.Tests.Tags;

public sealed class CuratedTagVocabularyTests
{
    private static readonly CuratedTagVocabulary Vocabulary = new(1,
    [
        new CuratedTag("parts", "Parts", "New parts."),
    ]);

    [Theory]
    [InlineData(ContentType.Mod)]
    [InlineData(ContentType.ModLoader)]
    [InlineData(ContentType.ModPack)]
    public void GetTags_GivesTheModListForModsLoadersAndPacks(ContentType type)
    {
        Assert.Equal("parts", Assert.Single(Vocabulary.GetTags(type)).Tag);
    }

    [Theory]
    [InlineData(ContentType.Vehicle)]
    [InlineData(ContentType.Save)]
    public void GetTags_IsEmptyForVehiclesAndSaves_BecauseTheirTagsAreFreeForm(ContentType type)
    {
        Assert.Empty(Vocabulary.GetTags(type));
    }
}
