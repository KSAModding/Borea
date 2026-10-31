using System.Text.Json.Nodes;
using Borea.Core.Listings;
using Borea.Storage.Listings;

namespace Borea.Storage.Tests.Listings;

/// <summary>
/// The test documents of content-index that its checks and its listing page run too, so the editor gives the answers of
/// check_schema.py. The rules the editor checks in full without the snapshot run here, and the vectors of
/// <see cref="Named"/> of the schema and license rules, which the editor also says in the words of the checks.
/// </summary>
public sealed class ListingVectorTests
{
    private static readonly string[] Rules = ["bounds"];

    private static readonly string[] Named =
    [
        "a mod does not name the mods folder as its target",
        "a license without a version is not an SPDX id",
        "a name with one close id names that id",
        "a v before the version still finds the close ids",
        "a short name that only the license library reads is not an SPDX id",
        "a name after WITH gets exceptions as close ids",
    ];

    /// <summary>
    /// Vectors whose rejection Borea words in its own way. The page names the keys of a oneOf, where check_schema.py
    /// prints the raw message of jsonschema.
    /// </summary>
    private static readonly string[] OwnWords = ["a dependency names an id or alternatives", "a vehicle with an install folder"];

    private readonly ListingValidator _validator = new(new EmbeddedSchema());
    private readonly TomlListingFormat _format = new();

    public static TheoryData<string> Vectors
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var vector in Read().Where(vector => Rules.Contains((string?)vector["rule"]) || Named.Contains((string?)vector["name"])))
                data.Add((string)vector["name"]!);
            return data;
        }
    }

    /// <summary>The vectors of a type that the rollout gate of RFC 0085 holds.</summary>
    public static TheoryData<string> GatedVectors
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var vector in Read().Where(vector => vector["gated"] is not null))
                data.Add((string)vector["name"]!);
            return data;
        }
    }

    [Fact]
    public void NamedVectors_AreInTheCopy()
    {
        var names = Read().Select(vector => (string?)vector["name"]).ToHashSet();

        Assert.All(Named.Concat(OwnWords), name => Assert.Contains(name, names));
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

    [Theory]
    [MemberData(nameof(GatedVectors))]
    public void GatedVector_IsRefusedWithTheTitleOfTheGate(string name)
    {
        var vector = Read().Single(vector => (string?)vector["name"] == name);
        var type = (string)vector["gated"]!;
        Assert.Contains(type, UngatedSchema.GatedTypes);

        var issues = _validator.Validate(_format.Read((string)vector["toml"]!), new ListingCheckContext(null)).Issues;

        Assert.Contains(issues, issue => issue.Severity == ListingIssueSeverity.Error && $"{issue.Location}: {issue.Message}" == $"type: '{type}' is {UngatedSchema.GateTitle}");
    }

    [Theory]
    [MemberData(nameof(GatedVectors))]
    public async Task GatedVector_GivesTheAnswerOfTheChecksWithoutTheGate(string name)
    {
        var vector = Read().Single(vector => (string?)vector["name"] == name);
        var validator = new ListingValidator(new UngatedSchema());
        await validator.LoadSchemaAsync();

        var issues = validator.Validate(_format.Read((string)vector["toml"]!), new ListingCheckContext(null)).Issues;

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
