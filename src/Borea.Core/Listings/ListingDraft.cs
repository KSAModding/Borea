using Borea.Core.Mods;

namespace Borea.Core.Listings;

/// <summary>
/// A listing or a pack version of content-index as the author edits it. <see cref="ToDocument"/> writes it onto the listed
/// document it came from, so every key and table this model does not name stays as it was.
/// </summary>
public sealed record ListingDraft
{
    public const string ModType = "mod";

    public const string ModLoaderType = "mod-loader";

    public const string ModPackType = "modpack";

    public const string ListingsFolder = "listings";

    public const string PacksFolder = "packs";

    public const int SpecVersion = 1;

    /// <summary>The listed document this draft changes, or for a pack the listed version it follows. Null for a new document.</summary>
    public AuthoredTable? Original { get; init; }

    /// <summary>Whether the draft changes a listed document in place. A pack version never changes, so a pack draft is never an edit.</summary>
    public bool IsEdit => Original is not null && !IsPack;

    /// <summary>Whether the draft is the next version of a listed pack: a new file that keeps what the listed version has.</summary>
    public bool IsNextVersion => Original is not null && IsPack;

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

    public bool IsPack => Type == ModPackType;

    /// <summary>Whether the draft is a vehicle or a save (RFC 0085), whose forums thread and compatibility are optional.</summary>
    public bool IsVehicleOrSave => Type is ListingFolders.VehicleType or ListingFolders.SaveType;

    /// <summary>The version of a pack. A pack document is its own release, so only a pack has one.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>When the pack version is published, as ISO 8601 UTC.</summary>
    public string? ReleasedAt { get; init; }

    public string? Changelog { get; init; }

    /// <summary>The mods of a pack, each pinned to one release.</summary>
    public IReadOnlyList<ListingPackMember> Mods { get; init; } = [];

    /// <summary>The vehicles of a pack, each pinned to one release (RFC 0085).</summary>
    public IReadOnlyList<ListingPackMember> Vehicles { get; init; } = [];

    /// <summary>The saves of a pack, each pinned to one release (RFC 0085).</summary>
    public IReadOnlyList<ListingPackMember> Saves { get; init; } = [];

    /// <summary>Every pin of a pack: the mods, then the vehicles, then the saves.</summary>
    public IReadOnlyList<ListingPackMember> Pins => [.. Mods, .. Vehicles, .. Saves];

    /// <summary>Where the document lives in content-index. Each pack version is a file of its own.</summary>
    public string Path => IsPack ? $"{PacksFolder}/{Id}/{Version}.toml" : $"{ListingFolders.Of(Type)}/{Id}.toml";

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
            Version = document.GetString("version") ?? string.Empty,
            ReleasedAt = document.GetString("released_at"),
            Changelog = document.GetString("changelog"),
            Mods = ReadMembers(document, ContentType.Mod),
            Vehicles = ReadMembers(document, ContentType.Vehicle),
            Saves = ReadMembers(document, ContentType.Save),
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
        SetOrRemove(document, "releases", WriteReleases(Releases, document.GetTable("releases")));

        var links = new AuthoredTable();
        foreach (var link in Links)
            links.Set(link.Key, link.Url);
        // A mod keeps writing both tables, so its checks still name a missing forums link or game_min.
        SetOrRemove(document, "links", IsVehicleOrSave && links.Count == 0 ? null : links);

        var compatibility = document.GetTable("compatibility")?.Clone() ?? new AuthoredTable();
        SetOrRemove(compatibility, "game_min", IsVehicleOrSave && GameMin.Length == 0 ? null : GameMin);
        SetOrRemove(compatibility, "game_max", GameMax);
        SetOrRemove(document, "compatibility", IsVehicleOrSave && compatibility.Count == 0 ? null : compatibility);

        SetOrRemove(document, "loader", Loader is null ? null : WriteLoader(Loader));
        SetOrRemove(document, "dependencies", Dependencies.Count == 0 ? null : Dependencies.Select(WriteDependency).Cast<object>().ToList());

        var images = new AuthoredTable();
        if (Icon is not null)
            images.Set("icon", WriteImage(Icon));
        if (DescriptionImages.Count > 0)
            images.Set("description", DescriptionImages.Select(WriteImage).Cast<object>().ToList());
        SetOrRemove(document, "images", images.Count == 0 ? null : images);

        // Only a pack names these keys, so a listing keeps them as they were.
        if (IsPack)
        {
            SetOrRemove(document, "version", Version.Length == 0 ? null : Version);
            SetOrRemove(document, "released_at", string.IsNullOrEmpty(ReleasedAt) ? null : ReleasedAt);
            SetOrRemove(document, "changelog", string.IsNullOrEmpty(Changelog) ? null : Changelog);
            SetOrRemove(document, "mods", Mods.Count == 0 ? null : Mods.Select(WriteMember).Cast<object>().ToList());
            SetOrRemove(document, "vehicles", Vehicles.Count == 0 ? null : Vehicles.Select(WriteMember).Cast<object>().ToList());
            SetOrRemove(document, "saves", Saves.Count == 0 ? null : Saves.Select(WriteMember).Cast<object>().ToList());
        }

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

        var github = table.GetString("github");
        var spaceDock = table["spacedock"] as long?;
        var authority = table.GetString("authority");

        // The rule of DtoMapper.MapReleaseSource: a share host counts with an authority, or as the only host.
        var shares = table.Entries
            .Where(entry => ReleaseHost.IsShareKey(entry.Key) && entry.Value is string id && ModIds.IsValid(id))
            .Select(entry => new ListingShareHost(entry.Key, (string)entry.Value))
            .ToList();
        var share = shares.FirstOrDefault(entry => entry.Key == authority)
            ?? (authority is not null || (github is null && spaceDock is null && shares.Count == 1) ? shares.FirstOrDefault() : null);
        return new ListingReleases(github, spaceDock, authority, table.GetString("since"), table.GetString("tag_prefix"), share);
    }

    private static AuthoredTable? WriteReleases(ListingReleases? releases, AuthoredTable? original)
    {
        if (releases is null || (string.IsNullOrEmpty(releases.GitHub) && releases.SpaceDock is null && releases.Share is null))
            return null;

        var table = original?.Clone() ?? new AuthoredTable();
        SetOrRemove(table, "github", string.IsNullOrEmpty(releases.GitHub) ? null : releases.GitHub);
        SetOrRemove(table, "spacedock", releases.SpaceDock);
        SetOrRemove(table, "authority", string.IsNullOrEmpty(releases.Authority) ? null : releases.Authority);
        SetOrRemove(table, "since", string.IsNullOrEmpty(releases.Since) ? null : releases.Since);
        SetOrRemove(table, "tag_prefix", string.IsNullOrEmpty(releases.TagPrefix) ? null : releases.TagPrefix);
        if (releases.Share is { } share)
            table.Set(share.Key, share.Id);
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

    /// <summary>The section of a pack that pins content of one type.</summary>
    public static string Section(ContentType type) => type switch
    {
        ContentType.Vehicle => "vehicles",
        ContentType.Save => "saves",
        _ => "mods",
    };

    private static List<ListingPackMember> ReadMembers(AuthoredTable document, ContentType type) =>
        document.GetList(Section(type))?.OfType<AuthoredTable>().Select(table => ReadMember(table) with { Type = type }).ToList() ?? [];

    private static ListingPackMember ReadMember(AuthoredTable table) =>
        new(table.GetString("id") ?? string.Empty, table.GetString("version") ?? string.Empty) { Original = table.Clone() };

    private static AuthoredTable WriteMember(ListingPackMember member)
    {
        var table = member.Original?.Clone() ?? new AuthoredTable();
        table.Set("id", member.Id);
        table.Set("version", member.Version);
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

/// <summary>
/// The [releases] table: the hosts the watcher reads, which of them decides when more than one is named, the version
/// from which the watcher also stamps the older releases (RFC 0079), the text that the tag of every mod release
/// on GitHub starts with (RFC 0096), and the share host of a vehicle or a save (RFC 0094).
/// </summary>
public sealed record ListingReleases(string? GitHub, long? SpaceDock, string? Authority = null, string? Since = null, string? TagPrefix = null, ListingShareHost? Share = null);

/// <summary>A share host under [releases], such as ksamods = "ExampleRover": the key of the host in hosts.toml and the listing id there.</summary>
public sealed record ListingShareHost(string Key, string Id);

public sealed record ListingLoader(string Id, string Min, string? Max = null);

/// <summary>One [[dependencies]] entry. <see cref="Preserved"/> holds an entry the page cannot edit, such as one with any_of, and is written back unchanged.</summary>
public sealed record ListingDependency(string Id, string Kind, string? Min = null, string? Max = null)
{
    public AuthoredTable? Preserved { get; init; }
}

/// <summary>One [[mods]], [[vehicles]] or [[saves]] entry of a pack. <see cref="Original"/> holds the entry as it was read, so a key the page does not name stays.</summary>
public sealed record ListingPackMember(string Id, string Version)
{
    public AuthoredTable? Original { get; init; }

    /// <summary>The type the section of the pin says, which the pinned listing must have.</summary>
    public ContentType Type { get; init; } = ContentType.Mod;
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
