using System.Text.Json;
using Borea.Core.Instances;

namespace Borea.Core.Tests.Instances;

/// <summary>
/// Every case of the copy of schemas/save-names.json of content-index, the name rule of RFC 0085.
/// The csproj names where the copy came from.
/// </summary>
public sealed class GameSaveNameVectorTests
{
    private static readonly JsonElement[] Vectors = ReadVectors();

    public static TheoryData<string> Names
    {
        get
        {
            var names = new TheoryData<string>();
            foreach (var vector in Vectors)
                names.Add(vector.GetProperty("name").GetString()!);
            return names;
        }
    }

    [Fact]
    public void Vectors_HoldTheCasesACheckerGetsWrongMostEasily()
    {
        var values = Vectors.Select(vector => vector.GetProperty("value").GetString()!).ToList();

        Assert.Contains(values, value => value.Any(char.IsSurrogate));
        Assert.Contains(values, value => value.Length == GameSaveName.MaxLength);
        Assert.Contains(values, value => value.Length == GameSaveName.MaxLength + 1);
        Assert.Contains(values, value => value.Contains((char)0x00B2, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void IsValid_GivesTheVerdictOfTheVector(string name)
    {
        var vector = Vectors.Single(item => item.GetProperty("name").GetString() == name);

        Assert.Equal(vector.GetProperty("accepted").GetBoolean(), GameSaveName.IsValid(vector.GetProperty("value").GetString()));
    }

    private static JsonElement[] ReadVectors()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Instances", "Fixtures", "save-names.json")));
        return document.RootElement.GetProperty("vectors").EnumerateArray().Select(vector => vector.Clone()).ToArray();
    }
}
