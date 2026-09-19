using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Borea.Core.Listings;

/// <summary>The release host an author names: a GitHub repository or a SpaceDock mod.</summary>
public abstract partial record ListingSourceReference
{
    private ListingSourceReference()
    {
    }

    public sealed record GitHub(string Owner, string Repository) : ListingSourceReference
    {
        public string FullName => $"{Owner}/{Repository}";
    }

    public sealed record SpaceDock(long ModId) : ListingSourceReference;

    /// <summary>
    /// Reads "owner/repo", a github.com URL of the repository or one of its pages,
    /// a SpaceDock mod id, or a spacedock.info URL of the mod.
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out ListingSourceReference? reference)
    {
        reference = null;
        var value = text?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return false;

        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var modId) && modId > 0)
        {
            reference = new SpaceDock(modId);
            return true;
        }

        if (OwnerAndRepository().Match(value) is { Success: true } shortForm)
        {
            reference = new GitHub(shortForm.Groups[1].Value, Trimmed(shortForm.Groups[2].Value));
            return true;
        }

        if (!Uri.TryCreate(value.Contains("://", StringComparison.Ordinal) ? value : "https://" + value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http"))
            return false;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var host = uri.Host.ToLowerInvariant();
        if (host is "github.com" or "www.github.com" && segments.Length >= 2
            && OwnerAndRepository().IsMatch($"{segments[0]}/{segments[1]}"))
        {
            reference = new GitHub(segments[0], Trimmed(segments[1]));
            return true;
        }

        if (host is "spacedock.info" or "www.spacedock.info" && segments.Length >= 2 && segments[0] == "mod"
            && long.TryParse(segments[1], NumberStyles.None, CultureInfo.InvariantCulture, out modId) && modId > 0)
        {
            reference = new SpaceDock(modId);
            return true;
        }

        return false;
    }

    private static string Trimmed(string repository) =>
        repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? repository[..^4] : repository;

    [GeneratedRegex("^([A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)/([A-Za-z0-9._-]+)$")]
    private static partial Regex OwnerAndRepository();
}
