using System.Text.Json.Nodes;
using Borea.Core.Listings;

namespace Borea.Storage.Listings;

/// <summary>Checks an authored document with the schema from <see cref="IListingSchemaSource"/> and the rules the schema cannot express.</summary>
public sealed class ListingValidator : IListingValidator
{
    private readonly IListingSchemaSource _schemas;
    private readonly SpdxLicenseList _licenses;
    private volatile Loaded _loaded;

    public ListingValidator(IListingSchemaSource schemas)
        : this(schemas, SpdxLicenseList.Embedded)
    {
    }

    internal ListingValidator(IListingSchemaSource schemas, SpdxLicenseList licenses)
    {
        _schemas = schemas ?? throw new ArgumentNullException(nameof(schemas));
        _licenses = licenses ?? throw new ArgumentNullException(nameof(licenses));
        _loaded = new Loaded(ListingSchemaCheck.Load(ListingSchemaStore.EmbeddedText), ListingSchemaOrigin.Embedded);
    }

    public ListingSchemaOrigin SchemaOrigin => _loaded.Origin;

    public async Task<ListingSchemaOrigin> LoadSchemaAsync(CancellationToken cancellationToken = default)
    {
        var schema = await _schemas.GetAsync(cancellationToken).ConfigureAwait(false);
        _loaded = new Loaded(ListingSchemaCheck.Load(schema.Text), schema.Origin);
        return schema.Origin;
    }

    public ListingCheckResult Validate(AuthoredTable document, ListingCheckContext context)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);

        var issues = _loaded.Check.Check(ToJson(document)).ToList();
        issues.AddRange(ListingRules.Check(document, context, _licenses));
        return new ListingCheckResult(issues.Distinct().ToList());
    }

    internal static JsonNode ToJson(object value) => value switch
    {
        AuthoredTable table => new JsonObject(table.Entries.Select(entry => KeyValuePair.Create(entry.Key, (JsonNode?)ToJson(entry.Value)))),
        IReadOnlyList<object> list => new JsonArray(list.Select(item => (JsonNode?)ToJson(item)).ToArray()),
        string text => JsonValue.Create(text),
        long number => JsonValue.Create(number),
        double number => JsonValue.Create(number),
        bool flag => JsonValue.Create(flag),
        _ => throw new ArgumentException($"A value of type {value.GetType().Name} is not part of an authored document.", nameof(value)),
    };

    private sealed record Loaded(ListingSchemaCheck Check, ListingSchemaOrigin Origin);
}
