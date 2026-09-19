namespace Borea.Core.Listings;

/// <summary>
/// A listing of content-index as the author edits it. <see cref="ToDocument"/> writes it onto the listed
/// document it came from, so every key and table this model does not name stays as it was.
/// </summary>
public sealed record ListingDraft
{
    public const string ModType = "mod";

    public const string ModLoaderType = "mod-loader";

    public const string ListingsFolder = "listings";

    public const int SpecVersion = 1;

    /// <summary>The listed document this draft changes, or null for a new listing.</summary>
    public AuthoredTable? Original { get; init; }

    public bool IsEdit => Original is not null;

    public string Id { get; init; } = string.Empty;

    public string Type { get; init; } = ModType;

    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<string> Authors { get; init; } = [];

    public string Abstract { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string License { get; init; } = string.Empty;

    public IReadOnlyList<string> Tags { get; init; } = [];

    public string? Status { get; init; }

    public string? SupersededBy { get; init; }

    public ListingReleases? Releases { get; init; }

    public IReadOnlyList<ListingLink> Links { get; init; } = [];

    public string GameMin { get; init; } = string.Empty;

    public string? GameMax { get; init; }

    public ListingLoader? Loader { get; init; }

    public IReadOnlyList<ListingDependency> Dependencies { get; init; } = [];

    public ListingImageRecord? Icon { get; init; }

    public IReadOnlyList<ListingImageRecord> DescriptionImages { get; init; } = [];

    /// <summary>Where the document lives in content-index.</summary>
    public string Path => $"{ListingsFolder}/{Id}.toml";

    public string? LinkOf(string key) => Links.FirstOrDefault(link => string.Equals(link.Key, key, StringComparison.OrdinalIgnoreCase))?.Url;

    public static ListingDraft FromDocument(AuthoredTable document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var links = document.GetTable("links")?.Entries
            .Where(entry => entry.Value is string)
            .Select(entry => new ListingLink(entry.Key, (string)entry.Value))
            .ToList() ?? [];
        var compatibility = document.GetTable("compatibility");
        var images = document.GetTable("images");

        return new ListingDraft
        {
            Original = document.Clone(),
            Id = document.GetString("id") ?? string.Empty,
            Type = document.GetString("type") ?? ModType,
            Name = document.GetString("name") ?? string.Empty,
            Authors = Strings(document.GetList("authors")),
            Abstract = document.GetString("abstract") ?? string.Empty,
            Description = document.GetString("description"),
            License = document.GetString("license") ?? string.Empty,
            Tags = Strings(document.GetList("tags")),
            Status = document.GetString("status"),
            SupersededBy = document.GetString("superseded_by"),
            Releases = ReadReleases(document.GetTable("releases")),
            Links = links,
            GameMin = compatibility?.GetString("game_min") ?? string.Empty,
            GameMax = compatibility?.GetString("game_max"),
            Loader = document.GetTable("loader") is { } loader
                ? new ListingLoader(loader.GetString("id") ?? string.Empty, loader.GetString("min") ?? string.Empty, loader.GetString("max"))
                : null,
            Dependencies = document.GetList("dependencies")?.OfType<AuthoredTable>().Select(ReadDependency).ToList() ?? [],
            Icon = images?.GetTable("icon") is { } icon ? ReadImage(icon) : null,
            DescriptionImages = images?.GetList("description")?.OfType<AuthoredTable>().Select(ReadImage).ToList() ?? [],
        };
    }

    /// <summary>The authored document of this draft, written onto a copy of <see cref="Original"/>.</summary>
    public AuthoredTable ToDocument()
    {
        var document = Original?.Clone() ?? new AuthoredTable();
        if (!document.Contains("spec_version"))
            document.Set("spec_version", (long)SpecVersion);

        document.Set("id", Id);
        document.Set("type", Type);
        document.Set("name", Name);
        document.Set("authors", Authors.Cast<object>().ToList());
        document.Set("abstract", Abstract);
        SetOrRemove(document, "description", string.IsNullOrEmpty(Description) ? null : Description);
        document.Set("license", License);
        SetOrRemove(document, "tags", Tags.Count == 0 ? null : Tags.Cast<object>().ToList());
        SetOrRemove(document, "status", Status);
        SetOrRemove(document, "superseded_by", SupersededBy);
        SetOrRemove(document, "releases", WriteReleases(Releases));

        var links = new AuthoredTable();
        foreach (var link in Links)
            links.Set(link.Key, link.Url);
        document.Set("links", links);

        var compatibility = document.GetTable("compatibility")?.Clone() ?? new AuthoredTable();
        compatibility.Set("game_min", GameMin);
        SetOrRemove(compatibility, "game_max", GameMax);
        document.Set("compatibility", compatibility);

        SetOrRemove(document, "loader", Loader is null ? null : WriteLoader(Loader));
        SetOrRemove(document, "dependencies", Dependencies.Count == 0 ? null : Dependencies.Select(WriteDependency).Cast<object>().ToList());

        var images = new AuthoredTable();
        if (Icon is not null)
            images.Set("icon", WriteImage(Icon));
        if (DescriptionImages.Count > 0)
            images.Set("description", DescriptionImages.Select(WriteImage).Cast<object>().ToList());
        SetOrRemove(document, "images", images.Count == 0 ? null : images);

        return document;
    }

    private static void SetOrRemove(AuthoredTable table, string key, object? value)
    {
        if (value is null)
            table.Remove(key);
        else
            table.Set(key, value);
    }

    private static IReadOnlyList<string> Strings(IReadOnlyList<object>? values) => values?.OfType<string>().ToList() ?? [];

    private static ListingReleases? ReadReleases(AuthoredTable? table)
    {
        if (table is null)
            return null;

        return new ListingReleases(table.GetString("github"), table["spacedock"] as long?, table.GetString("authority"));
    }

    private static AuthoredTable? WriteReleases(ListingReleases? releases)
    {
        if (releases is null || (string.IsNullOrEmpty(releases.GitHub) && releases.SpaceDock is null))
            return null;

        var table = new AuthoredTable();
        if (!string.IsNullOrEmpty(releases.GitHub))
            table.Set("github", releases.GitHub);
        if (releases.SpaceDock is { } spaceDock)
            table.Set("spacedock", spaceDock);
        if (!string.IsNullOrEmpty(releases.Authority))
            table.Set("authority", releases.Authority);
        return table;
    }

    private static AuthoredTable WriteLoader(ListingLoader loader)
    {
        var table = new AuthoredTable();
        table.Set("id", loader.Id);
        table.Set("min", loader.Min);
        if (!string.IsNullOrEmpty(loader.Max))
            table.Set("max", loader.Max);
        return table;
    }

    private static ListingDependency ReadDependency(AuthoredTable table)
    {
        var editable = table.Entries.All(entry => entry.Key is "id" or "kind" or "min" or "max" && entry.Value is string)
            && table.GetString("id") is not null;
        return new ListingDependency(
            table.GetString("id") ?? string.Empty,
            table.GetString("kind") ?? string.Empty,
            table.GetString("min"),
            table.GetString("max"))
        {
            Preserved = editable ? null : table.Clone(),
        };
    }

    private static AuthoredTable WriteDependency(ListingDependency dependency)
    {
        if (dependency.Preserved is { } preserved)
            return preserved.Clone();

        var table = new AuthoredTable();
        table.Set("id", dependency.Id);
        table.Set("kind", dependency.Kind);
        if (!string.IsNullOrEmpty(dependency.Min))
            table.Set("min", dependency.Min);
        if (!string.IsNullOrEmpty(dependency.Max))
            table.Set("max", dependency.Max);
        return table;
    }

    private static ListingImageRecord ReadImage(AuthoredTable table) => new(table.GetString("url") ?? string.Empty)
    {
        Id = table.GetString("id"),
        Sha256 = table.GetString("sha256"),
        Width = table["width"] as long?,
        Height = table["height"] as long?,
        Size = table["size"] as long?,
        License = table.GetString("license"),
        Attribution = table.GetString("attribution"),
        Source = table.GetString("source"),
    };

    private static AuthoredTable WriteImage(ListingImageRecord image)
    {
        var table = new AuthoredTable();
        SetOrRemove(table, "id", string.IsNullOrEmpty(image.Id) ? null : image.Id);
        table.Set("url", image.Url);
        SetOrRemove(table, "sha256", image.Sha256);
        SetOrRemove(table, "width", image.Width);
        SetOrRemove(table, "height", image.Height);
        SetOrRemove(table, "size", image.Size);
        SetOrRemove(table, "license", string.IsNullOrEmpty(image.License) ? null : image.License);
        SetOrRemove(table, "attribution", string.IsNullOrEmpty(image.Attribution) ? null : image.Attribution);
        SetOrRemove(table, "source", string.IsNullOrEmpty(image.Source) ? null : image.Source);
        return table;
    }
}

public sealed record ListingLink(string Key, string Url);

/// <summary>The [releases] table: the hosts the watcher reads, and which of them decides when both are named.</summary>
public sealed record ListingReleases(string? GitHub, long? SpaceDock, string? Authority = null);

public sealed record ListingLoader(string Id, string Min, string? Max = null);

/// <summary>One [[dependencies]] entry. <see cref="Preserved"/> holds an entry the page cannot edit, such as one with any_of, and is written back unchanged.</summary>
public sealed record ListingDependency(string Id, string Kind, string? Min = null, string? Max = null)
{
    public AuthoredTable? Preserved { get; init; }
}

/// <summary>An image record of RFC 0058. The facts stay null until the image is measured.</summary>
public sealed record ListingImageRecord(string Url)
{
    /// <summary>The name a ksa-image reference uses. Only a description image has one.</summary>
    public string? Id { get; init; }

    public string? Sha256 { get; init; }

    public long? Width { get; init; }

    public long? Height { get; init; }

    public long? Size { get; init; }

    public string? License { get; init; }

    public string? Attribution { get; init; }

    public string? Source { get; init; }
}
