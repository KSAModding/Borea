namespace Borea.Core.Listings;

public enum ListingSchemaOrigin
{
    /// <summary>Loaded from the main branch of content-index just now.</summary>
    Downloaded = 0,

    /// <summary>Loaded from content-index earlier and kept in the cache.</summary>
    Cached = 1,

    /// <summary>The copy that ships with this build of Borea.</summary>
    Embedded = 2,
}

/// <summary>The authored schema of content-index, as JSON text.</summary>
public sealed record ListingSchema(string Text, ListingSchemaOrigin Origin);

public interface IListingSchemaSource
{
    /// <summary>The downloaded schema, else the cached one, else the embedded one. A schema that does not load is never returned.</summary>
    Task<ListingSchema> GetAsync(CancellationToken cancellationToken = default);
}

public interface IListingSchemaFetcher
{
    /// <exception cref="HttpRequestException">The schema could not be downloaded.</exception>
    Task<string> FetchAsync(CancellationToken cancellationToken = default);
}
