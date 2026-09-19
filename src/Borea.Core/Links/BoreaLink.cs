using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Borea.Core.Mods;

namespace Borea.Core.Links;

public enum BoreaLinkKind
{
    Mod,
    Pack,
    Install,
}

/// <summary>
/// A link that opens Borea: borea://mod/&lt;id&gt;, borea://pack/&lt;id&gt;, or
/// borea://install/&lt;id&gt; with an optional ?version=&lt;semver&gt;. Anything else is refused.
/// </summary>
public sealed record BoreaLink(BoreaLinkKind Kind, string Id, ModVersion? Version = null)
{
    public const string Scheme = "borea";

    public const int MaxLength = 2048;

    private const string Prefix = Scheme + "://";

    private const int LoggedLength = 200;

    /// <summary>Whether the text names the borea scheme. It says nothing about whether the link is valid.</summary>
    public static bool HasScheme(string? text) => text is not null && text.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase);

    /// <param name="refusal">Why the link was refused, in English for the log.</param>
    public static bool TryParse(string? text, [NotNullWhen(true)] out BoreaLink? link, [NotNullWhen(false)] out string? refusal)
    {
        link = Read(text, out var reason);
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
        _ => Version is { } version ? $"{Prefix}install/{Id}?version={version}" : $"{Prefix}install/{Id}",
    };

    private static BoreaLink? Read(string? text, out string refusal)
    {
        if (string.IsNullOrEmpty(text))
            return Refuse("the link is empty", out refusal);
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
        else
            return Refuse("the link kind is not mod, pack or install", out refusal);

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

    private static BoreaLink? Refuse(string reason, out string refusal)
    {
        refusal = reason;
        return null;
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
