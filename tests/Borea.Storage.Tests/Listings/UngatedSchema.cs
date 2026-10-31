using System.Text.Json.Nodes;
using Borea.Core.Listings;
using Borea.Storage.Listings;

namespace Borea.Storage.Tests.Listings;

/// <summary>
/// The embedded schema without its rollout gate (properties.type.not), the schema of content-index once a steward
/// lifts the gate for every type.
/// </summary>
internal sealed class UngatedSchema : IListingSchemaSource
{
    public static string Text { get; } = Remove(ListingSchemaStore.EmbeddedText);

    /// <summary>The types the gate of the embedded schema holds.</summary>
    public static IReadOnlyList<string> GatedTypes { get; } = Gate()?["enum"]!.AsArray().Select(type => type!.GetValue<string>()).ToList() ?? [];

    /// <summary>The title the checks say a gated type with.</summary>
    public static string? GateTitle { get; } = Gate()?["title"]?.GetValue<string>();

    public Task<ListingSchema> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ListingSchema(Text, ListingSchemaOrigin.Downloaded));

    private static JsonNode? Gate() => JsonNode.Parse(ListingSchemaStore.EmbeddedText)!["properties"]!["type"]!["not"];

    private static string Remove(string text)
    {
        var node = JsonNode.Parse(text)!;
        node["properties"]!["type"]!.AsObject().Remove("not");
        return node.ToJsonString();
    }
}
