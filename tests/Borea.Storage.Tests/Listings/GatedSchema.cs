using System.Text.Json.Nodes;
using Borea.Core.Listings;
using Borea.Storage.Listings;

namespace Borea.Storage.Tests.Listings;

/// <summary>
/// The embedded schema with a rollout gate (properties.type.not) that holds vehicles and saves, the way content-index
/// holds a new type back until a steward lifts the gate.
/// </summary>
internal sealed class GatedSchema : IListingSchemaSource
{
    /// <summary>The title the checks say a gated type with.</summary>
    public const string Title = "a type that the index does not accept yet";

    private static readonly string Text = Add(ListingSchemaStore.EmbeddedText);

    public Task<ListingSchema> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ListingSchema(Text, ListingSchemaOrigin.Downloaded));

    private static string Add(string text)
    {
        var node = JsonNode.Parse(text)!;
        node["properties"]!["type"]!["not"] = new JsonObject { ["title"] = Title, ["enum"] = new JsonArray("vehicle", "save") };
        return node.ToJsonString();
    }
}
