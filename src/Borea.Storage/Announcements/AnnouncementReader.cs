using Borea.Core.Announcements;
using Borea.Core.Mods;
using Tomlyn;
using Tomlyn.Model;

namespace Borea.Storage.Announcements;

/// <summary>Reads announcements.toml. A post that breaks a rule is skipped, and only a file that breaks the format is rejected.</summary>
public sealed class AnnouncementReader : IAnnouncementReader
{
    public const int SpecVersion = 1;

    public async Task<AnnouncementFile> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
            return new AnnouncementFile([], []);

        return Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
    }

    internal static AnnouncementFile Parse(string text)
    {
        TomlTable root;
        try
        {
            root = TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
        }
        catch (Exception exception)
        {
            throw new InvalidDataException($"The file is not valid TOML. {exception.Message}", exception);
        }

        if (!root.TryGetValue("spec_version", out var version) || version is not long number)
            throw new InvalidDataException("The file has no integer spec_version.");
        if (number != SpecVersion)
            throw new InvalidDataException($"The file has spec_version {number}, and this Borea reads only {SpecVersion}.");

        var posts = new List<Announcement>();
        var skipped = new List<string>();
        if (!root.TryGetValue("posts", out var value))
            return new AnnouncementFile(posts, skipped);

        IEnumerable<object?> entries = value switch
        {
            TomlTableArray tables => tables,
            TomlArray array => array,
            _ => throw new InvalidDataException("posts is not an array of tables."),
        };

        var ids = new HashSet<string>(ModIds.Comparer);
        var position = 0;
        foreach (var entry in entries)
        {
            position++;
            Announcement? post = null;
            var error = entry is TomlTable table ? ReadPost(table, out post) : "it is not a table.";
            if (post is not null && !ids.Add(post.Id))
                error = $"the id '{post.Id}' appears more than once.";

            if (error is null && post is not null)
                posts.Add(post);
            else
                skipped.Add($"Post {position}: {error}");
        }

        return new AnnouncementFile(posts, skipped);
    }

    private static string? ReadPost(TomlTable table, out Announcement? post)
    {
        post = null;
        if (Text(table, "id") is not { } id || !ModIds.IsValid(id))
            return "the id is missing or not a valid id.";
        if (Text(table, "title") is not { } title)
            return $"'{id}' has no title.";
        if (Date(table) is not { } date)
            return $"'{id}' has no date, or it is not a TOML date or offset date-time.";
        if (Text(table, "body") is not { } body)
            return $"'{id}' has no body.";

        string? link = null;
        if (table.TryGetValue("link", out var linkValue))
        {
            if (linkValue is not string text || !Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                return $"'{id}' has a link that is not an HTTPS URL.";
            link = text;
        }

        post = new Announcement(id, title.Trim(), date, body, link);
        return null;
    }

    private static string? Text(TomlTable table, string key)
        => table.TryGetValue(key, out var value) && value is string text && !string.IsNullOrWhiteSpace(text) ? text : null;

    private static DateTimeOffset? Date(TomlTable table)
    {
        if (!table.TryGetValue("date", out var value))
            return null;

        return value switch
        {
            TomlDateTime { Kind: TomlDateTimeKind.LocalDate } local => new DateTimeOffset(local.DateTime.Year, local.DateTime.Month, local.DateTime.Day, 0, 0, 0, TimeSpan.Zero),
            TomlDateTime { Kind: TomlDateTimeKind.OffsetDateTimeByZ or TomlDateTimeKind.OffsetDateTimeByNumber } offset => offset.DateTime,
            _ => null,
        };
    }
}
