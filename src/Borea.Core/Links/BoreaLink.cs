using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.Core.Links;

public enum BoreaLinkKind
{
    Mod,
    Pack,
    Install,
    ModList,
}

/// <summary>Why Borea refused a modlist link, so that the player can tell the server owner what to fix.</summary>
public enum ModListLinkProblem
{
    /// <summary>The link is not a modlist link, or it was not refused.</summary>
    None,

    /// <summary>The link has a path, a parameter other than one mods and at most one name and one id, or no mods.</summary>
    Form,

    /// <summary>The link is longer than <see cref="BoreaLink.MaxLength"/>, which a long list reaches before <see cref="BoreaLink.MaxModListEntries"/>.</summary>
    TooLong,
    NoVersion,
    BadId,
    BadVersion,
    Duplicate,
    TooMany,
    BadName,
    BadListId,
}

/// <summary>
/// A link that opens Borea: borea://mod/&lt;id&gt;, borea://pack/&lt;id&gt;,
/// borea://install/&lt;id&gt; with an optional ?version=&lt;semver&gt;, or
/// borea://modlist?mods=&lt;id&gt;@&lt;semver&gt;,... with an optional &amp;name=&lt;text&gt; and &amp;id=&lt;list id&gt;. Anything else is refused.
/// </summary>
/// <param name="Id">The content id, or empty for a modlist link, which names its mods in <see cref="Mods"/>.</param>
public sealed record BoreaLink(BoreaLinkKind Kind, string Id, ModVersion? Version = null)
{
    public const string Scheme = "borea";

    public const int MaxLength = 2048;

    public const int MaxModListEntries = 100;

    public const int MaxNameLength = 64;

    private const string Prefix = Scheme + "://";

    private const int LoggedLength = 200;

    /// <summary>The mods of a modlist link at exact versions, in the order of the link.</summary>
    public IReadOnlyList<ModListEntry> Mods { get; init; } = [];

    /// <summary>The name a modlist link suggests for the new instance. It is text from outside, so Borea only shows it.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// The id a modlist link gives its list, for example the id of a server, so that a later link with the same id can
    /// be told apart from another list with the same name. It has the form of a content id, and Borea does not act on it yet.
    /// </summary>
    public string? ListId { get; init; }

    /// <summary>Whether the text names the borea scheme. It says nothing about whether the link is valid.</summary>
    public static bool HasScheme(string? text) => text is not null && text.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase);

    /// <param name="refusal">Why the link was refused, in English for the log.</param>
    public static bool TryParse(string? text, [NotNullWhen(true)] out BoreaLink? link, [NotNullWhen(false)] out string? refusal)
        => TryParse(text, out link, out refusal, out _);

    /// <param name="refusal">Why the link was refused, in English for the log.</param>
    /// <param name="problem">What is wrong with a refused modlist link, and <see cref="ModListLinkProblem.None"/> for any other link.</param>
    public static bool TryParse(string? text, [NotNullWhen(true)] out BoreaLink? link, [NotNullWhen(false)] out string? refusal, out ModListLinkProblem problem)
    {
        problem = ModListLinkProblem.None;
        link = Read(text, out var reason, ref problem);
        if (link is not null)
        {
            refusal = null;
            return true;
        }

        refusal = reason;
        return false;
    }

    /// <summary>The text cut short and with every character outside printable ASCII replaced, so that it fits one log line.</summary>
    public static string ForLog(string? text)
    {
        if (text is null)
            return "(none)";

        var shown = text.Length > LoggedLength ? text[..LoggedLength] + "..." : text;
        return string.Create(shown.Length, shown, (span, value) =>
        {
            for (var index = 0; index < value.Length; index++)
                span[index] = value[index] is >= ' ' and <= '~' ? value[index] : '?';
        });
    }

    public override string ToString() => Kind switch
    {
        BoreaLinkKind.Mod => $"{Prefix}mod/{Id}",
        BoreaLinkKind.Pack => $"{Prefix}pack/{Id}",
        BoreaLinkKind.ModList => $"{Prefix}modlist?mods={string.Join(',', Mods.Select(entry => $"{entry.ModId}@{entry.Version}"))}"
            + (Name is null ? string.Empty : $"&name={EncodeName(Name)}")
            + (ListId is null ? string.Empty : $"&id={ListId}"),
        _ => Version is { } version ? $"{Prefix}install/{Id}?version={version}" : $"{Prefix}install/{Id}",
    };

    private static BoreaLink? Read(string? text, out string refusal, ref ModListLinkProblem problem)
    {
        if (string.IsNullOrEmpty(text))
            return Refuse("the link is empty", out refusal);
        if (text.Length > MaxLength && text.StartsWith(Prefix + "modlist", StringComparison.OrdinalIgnoreCase))
            return Refuse(ModListLinkProblem.TooLong, $"the link is longer than {MaxLength} characters", out refusal, out problem);
        if (text.Length > MaxLength)
            return Refuse($"the link is longer than {MaxLength} characters", out refusal);
        if (text.Any(character => character is < '!' or > '~'))
            return Refuse("the link has a character that is not printable ASCII", out refusal);
        if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return Refuse("the link does not start with borea://", out refusal);

        var rest = text[Prefix.Length..];
        if (rest.Contains('#'))
            return Refuse("the link has a fragment", out refusal);

        var queryStart = rest.IndexOf('?');
        var query = queryStart < 0 ? null : rest[(queryStart + 1)..];
        var segments = (queryStart < 0 ? rest : rest[..queryStart]).Split('/');
        var host = segments[0];
        if (host.Contains('@'))
            return Refuse("the link has user info", out refusal);
        if (host.Contains(':'))
            return Refuse("the link has a port", out refusal);

        BoreaLinkKind kind;
        if (host.Equals("mod", StringComparison.OrdinalIgnoreCase))
            kind = BoreaLinkKind.Mod;
        else if (host.Equals("pack", StringComparison.OrdinalIgnoreCase))
            kind = BoreaLinkKind.Pack;
        else if (host.Equals("install", StringComparison.OrdinalIgnoreCase))
            kind = BoreaLinkKind.Install;
        else if (host.Equals("modlist", StringComparison.OrdinalIgnoreCase))
            return ReadModList(segments, query, out refusal, ref problem);
        else
            return Refuse("the link kind is not mod, pack, install or modlist", out refusal);

        if (segments.Length is not (2 or 3) || (segments.Length == 3 && segments[2].Length > 0))
            return Refuse("the link does not name exactly one id", out refusal);

        var id = Decode(segments[1]);
        if (!ModIds.IsValid(id))
            return Refuse("the id is not a valid content id", out refusal);

        if (query is null)
        {
            refusal = string.Empty;
            return new BoreaLink(kind, id!);
        }

        if (kind != BoreaLinkKind.Install)
            return Refuse("only an install link takes a query", out refusal);

        var separator = query.IndexOf('=');
        if (query.Contains('&') || separator < 0 || !query.AsSpan(0, separator).SequenceEqual("version"))
            return Refuse("the query is not one version parameter", out refusal);

        if (!ModVersion.TryParse(Decode(query[(separator + 1)..]), out var version))
            return Refuse("the version is not a valid SemVer version", out refusal);

        refusal = string.Empty;
        return new BoreaLink(kind, id!, version);
    }

    /// <summary>Each parameter may come once. An unknown one is refused, so that a Borea that does not know a later parameter never ignores it.</summary>
    private static BoreaLink? ReadModList(string[] segments, string? query, out string refusal, ref ModListLinkProblem problem)
    {
        if (segments.Length > 2 || (segments.Length == 2 && segments[1].Length > 0))
            return Refuse(ModListLinkProblem.Form, "a modlist link has no path", out refusal, out problem);

        string? modsText = null;
        string? nameText = null;
        string? listIdText = null;
        foreach (var parameter in (query ?? string.Empty).Split('&'))
        {
            var separator = parameter.IndexOf('=');
            var key = separator < 0 ? parameter : parameter[..separator];
            var value = separator < 0 ? null : parameter[(separator + 1)..];
            if (key == "mods" && modsText is null && value is not null)
                modsText = value;
            else if (key == "name" && nameText is null && value is not null)
                nameText = value;
            else if (key == "id" && listIdText is null && value is not null)
                listIdText = value;
            else
                return Refuse(ModListLinkProblem.Form, "the query of a modlist link is not one mods and at most one name and one id parameter", out refusal, out problem);
        }

        var decoded = modsText is null ? null : Decode(modsText);
        if (string.IsNullOrEmpty(decoded))
            return Refuse(ModListLinkProblem.Form, "the mods parameter is missing, empty or has a broken escape", out refusal, out problem);

        var parts = decoded.Split(',');
        if (parts.Length > MaxModListEntries)
            return Refuse(ModListLinkProblem.TooMany, $"the modlist names more than {MaxModListEntries} mods", out refusal, out problem);

        var mods = new List<ModListEntry>(parts.Length);
        var seen = new HashSet<string>(ModIds.Comparer);
        foreach (var part in parts)
        {
            var at = part.IndexOf('@');
            if (at < 0)
                return Refuse(ModListLinkProblem.NoVersion, "a mod of the modlist has no exact version", out refusal, out problem);

            var id = part[..at];
            if (!ModIds.IsValid(id))
                return Refuse(ModListLinkProblem.BadId, "a mod of the modlist is not a valid content id", out refusal, out problem);
            if (!ModVersion.TryParse(part[(at + 1)..], out var version))
                return Refuse(ModListLinkProblem.BadVersion, "a mod of the modlist has a version that is not a valid SemVer version", out refusal, out problem);
            if (!seen.Add(id))
                return Refuse(ModListLinkProblem.Duplicate, "the modlist names a mod more than once", out refusal, out problem);

            mods.Add(new ModListEntry(id, version, enabled: true));
        }

        string? name = null;
        if (nameText is not null)
        {
            name = DecodeName(nameText);
            if (name is null || name.Length > MaxNameLength || string.IsNullOrWhiteSpace(name))
                return Refuse(ModListLinkProblem.BadName, $"the name is not 1 to {MaxNameLength} characters of printable ASCII", out refusal, out problem);
        }

        string? listId = null;
        if (listIdText is not null)
        {
            listId = Decode(listIdText);
            if (!ModIds.IsValid(listId))
                return Refuse(ModListLinkProblem.BadListId, "the id of the modlist is not a valid content id", out refusal, out problem);
        }

        refusal = string.Empty;
        return new BoreaLink(BoreaLinkKind.ModList, string.Empty) { Mods = mods, Name = name, ListId = listId };
    }

    /// <summary>Reads a plus or an escaped space as a space, and decodes the rest like <see cref="Decode"/>.</summary>
    private static string? DecodeName(string value)
    {
        var decoded = value.Replace("%20", "+", StringComparison.OrdinalIgnoreCase).Split('+').Select(Decode).ToList();
        return decoded.Any(part => part is null) ? null : string.Join(' ', decoded);
    }

    /// <summary>Keeps letters, digits and the unreserved marks, writes a space as a plus, and escapes everything else.</summary>
    private static string EncodeName(string name)
    {
        var encoded = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            if (character == ' ')
                encoded.Append('+');
            else if (char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '~')
                encoded.Append(character);
            else
                encoded.Append('%').Append(((int)character).ToString("X2", CultureInfo.InvariantCulture));
        }

        return encoded.ToString();
    }

    private static BoreaLink? Refuse(string reason, out string refusal)
    {
        refusal = reason;
        return null;
    }

    private static BoreaLink? Refuse(ModListLinkProblem kind, string reason, out string refusal, out ModListLinkProblem problem)
    {
        problem = kind;
        return Refuse(reason, out refusal);
    }

    /// <summary>Decodes the percent escapes once, or returns null for a broken escape or one that is not printable ASCII.</summary>
    private static string? Decode(string value)
    {
        if (!value.Contains('%'))
            return value;

        var decoded = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '%')
            {
                decoded.Append(value[index]);
                continue;
            }

            if (index + 2 >= value.Length
                || !byte.TryParse(value.AsSpan(index + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var escaped)
                || escaped is < (byte)'!' or > (byte)'~')
                return null;

            decoded.Append((char)escaped);
            index += 2;
        }

        return decoded.ToString();
    }
}
