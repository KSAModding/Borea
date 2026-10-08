using System.Text.Json.Nodes;
using Borea.Core.Listings;
using Borea.Storage.Listings;

namespace Borea.Storage.Tests.Listings;

/// <summary>
/// The test documents of content-index that its checks and its listing page run too, so the editor gives the answers of
/// check_schema.py. Only the rules the editor checks in full without the snapshot run here.
/// </summary>
public sealed class ListingVectorTests
{
    private static readonly string[] Rules = ["bounds"];

    /// <summary>
    /// Vectors whose rejection Borea words in its own way. The page names the keys of a oneOf, where check_schema.py
    /// prints the raw message of jsonschema.
    /// </summary>
    private static readonly string[] OwnWords = ["a dependency names an id or alternatives"];

    private readonly ListingValidator _validator = new(new EmbeddedSchema());
    private readonly TomlListingFormat _format = new();

    public static TheoryData<string> Vectors
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var vector in Read().Where(vector => Rules.Contains((string?)vector["rule"])))
                data.Add((string)vector["name"]!);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Vector_GivesTheAnswerOfTheChecks(string name)
    {
        var vector = Read().Single(vector => (string?)vector["name"] == name);

        var issues = _validator.Validate(_format.Read((string)vector["toml"]!), new ListingCheckContext(null)).Issues;

        var lines = issues.Select(issue => $"{issue.Location}: {issue.Message}").ToList();
        Assert.True((bool)vector["accepted"]! == !issues.Any(issue => issue.Severity == ListingIssueSeverity.Error), string.Join("\n", lines));
        if ((string?)vector["says"] is { } says && !OwnWords.Contains(name))
            Assert.True(lines.Any(line => line.Contains(says, StringComparison.Ordinal)), $"'{says}' is not in:\n{string.Join("\n", lines)}");
    }

    private static List<JsonNode> Read() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Listings", "Fixtures", "vectors.json")))!["vectors"]!.AsArray().OfType<JsonNode>().ToList();

    private sealed class EmbeddedSchema : IListingSchemaSource
    {
        public Task<ListingSchema> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ListingSchema(ListingSchemaStore.EmbeddedText, ListingSchemaOrigin.Embedded));
    }
}
