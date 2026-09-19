using System.Text.Json;
using Borea.Core.Listings;
using Borea.Core.Paths;
using Borea.Storage.Files;
using Json.Schema;

namespace Borea.Storage.Listings;

/// <summary>
/// The authored schema of content-index: downloaded and cached, else the cached copy, else the copy in this build.
/// A text that does not load as a schema is never used or cached.
/// </summary>
public sealed class ListingSchemaStore : IListingSchemaSource
{
    private const string EmbeddedName = "Borea.Storage.Listings.authored.schema.json";

    private static readonly Lazy<string> Embedded = new(ReadEmbedded);

    private readonly IListingSchemaFetcher _fetcher;
    private readonly IGamePathProvider _paths;

    public ListingSchemaStore(IListingSchemaFetcher fetcher, IGamePathProvider paths)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    /// <summary>The schema that ships with this build.</summary>
    public static string EmbeddedText => Embedded.Value;

    public async Task<ListingSchema> GetAsync(CancellationToken cancellationToken = default)
    {
        var path = _paths.GetListingSchemaPath();
        try
        {
            var downloaded = await _fetcher.FetchAsync(cancellationToken).ConfigureAwait(false);
            if (Loads(downloaded))
            {
                await TryWriteAsync(path, downloaded, cancellationToken).ConfigureAwait(false);
                return new ListingSchema(downloaded, ListingSchemaOrigin.Downloaded);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
        }

        try
        {
            if (File.Exists(path))
            {
                var cached = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                if (Loads(cached))
                    return new ListingSchema(cached, ListingSchemaOrigin.Cached);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return new ListingSchema(EmbeddedText, ListingSchemaOrigin.Embedded);
    }

    internal static bool Loads(string text)
    {
        try
        {
            ListingSchemaCheck.Load(text);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or JsonSchemaException or ArgumentException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static async Task TryWriteAsync(string path, string text, CancellationToken cancellationToken)
    {
        try
        {
            await AtomicFile.WriteAllTextAsync(path, text, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string ReadEmbedded()
    {
        using var stream = typeof(ListingSchemaStore).Assembly.GetManifestResourceStream(EmbeddedName)
            ?? throw new InvalidOperationException("The authored schema is missing from this build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
