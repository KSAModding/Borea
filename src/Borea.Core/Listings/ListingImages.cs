using System.Security.Cryptography;
using Borea.Core.Index;

namespace Borea.Core.Listings;

public enum ListingImageRole
{
    Icon = 0,
    Description = 1,
}

/// <summary>The facts of an image record, or why the image cannot be one.</summary>
public sealed record ListingImageMeasurement
{
    private ListingImageMeasurement(string? sha256, int width, int height, long size, string? format, string? problem, bool isUnavailable = false)
    {
        Sha256 = sha256;
        Width = width;
        Height = height;
        Size = size;
        Format = format;
        Problem = problem;
        IsUnavailable = isUnavailable;
    }

    public bool IsMeasured => Problem is null;

    /// <summary>
    /// True when the host did not answer, timed out, failed or limited the rate, so the checks could not evaluate the image and a later try
    /// can work. Any other problem is one that the checks of RFC 0058 reject.
    /// </summary>
    public bool IsUnavailable { get; }

    /// <summary>Lowercase hex SHA-256 of the bytes.</summary>
    public string? Sha256 { get; }

    public int Width { get; }

    public int Height { get; }

    public long Size { get; }

    public string? Format { get; }

    public string? Problem { get; }

    public static ListingImageMeasurement Failed(string problem) => new(null, 0, 0, 0, null, problem);

    public static ListingImageMeasurement Unavailable(string problem) => new(null, 0, 0, 0, null, problem, isUnavailable: true);

    /// <summary>Measures the bytes with the rules of RFC 0058 and RFC 0065 for the role.</summary>
    public static ListingImageMeasurement Of(ReadOnlySpan<byte> bytes, ListingImageRole role)
    {
        var cap = MaxBytes(role);
        if (bytes.Length > cap)
            return Failed($"the image is {bytes.Length} bytes, above the cap of {cap}");

        if (ContentImageBytes.Inspect(bytes) is not { } facts)
            return Failed("the bytes are not PNG, JPEG or WebP");

        if (facts.Animated)
            return Failed($"the {facts.Format} is animated");

        if (OutsideLimits(role, facts.Width, facts.Height) is { } outside)
            return Failed(outside);

        return new ListingImageMeasurement(Convert.ToHexStringLower(SHA256.HashData(bytes)), facts.Width, facts.Height, bytes.Length, facts.Format, null);
    }

    public static long MaxBytes(ListingImageRole role) => role == ListingImageRole.Icon ? IconImage.MaxBytes : DescriptionImage.MaxBytes;

    /// <summary>Why the size breaks the pixel limits of the role, in the words of the checks, or null.</summary>
    public static string? OutsideLimits(ListingImageRole role, long width, long height)
    {
        var shorter = Math.Min(width, height);
        var longer = Math.Max(width, height);
        if (role == ListingImageRole.Icon)
        {
            if (shorter is >= IconImage.MinShorterSidePixels and <= IconImage.MaxShorterSidePixels && longer <= IconImage.MaxSideRatio * shorter)
                return null;

            return $"{width} by {height} pixels is outside the limits: the shorter side {IconImage.MinShorterSidePixels} to {IconImage.MaxShorterSidePixels}, "
                + $"the longer side at most {IconImage.MaxSideRatio} times the shorter side";
        }

        return shorter >= 1 && longer <= DescriptionImage.MaxPixels
            ? null
            : $"{width} by {height} pixels is outside 1 to {DescriptionImage.MaxPixels} per side";
    }
}

/// <summary>
/// An address on raw.githubusercontent.com that follows a branch, so the file there can change after the record is written.
/// </summary>
/// <param name="Path">The path of the file in the repository, as the address writes it.</param>
/// <param name="IsSure">True for a refs/heads/ address. A plain name, such as main/, can also be a tag, which only GitHub can tell.</param>
public sealed record GitHubBranchAddress(string Owner, string Repository, string Branch, string Path, bool IsSure)
{
    public const string RawHost = "raw.githubusercontent.com";

    /// <summary>The address of the same file at <paramref name="commit"/>.</summary>
    public string AtCommit(string commit) => $"https://{RawHost}/{Owner}/{Repository}/{commit}/{Path}";

    /// <summary>
    /// The branch name when the address can name only one branch. A refs/heads/ address whose path has a folder can also name a branch
    /// with a slash, such as release/1.0, so only GitHub can tell.
    /// </summary>
    public string? CertainBranch => IsSure && !Path.Contains('/', StringComparison.Ordinal) ? Branch : null;

    /// <summary>The same refs/heads/ address read with the next path segment as part of the branch name, or null.</summary>
    public GitHubBranchAddress? Longer()
    {
        var slash = Path.IndexOf('/', StringComparison.Ordinal);
        return IsSure && slash > 0
            ? this with { Branch = $"{Branch}/{Uri.UnescapeDataString(Path[..slash])}", Path = Path[(slash + 1)..] }
            : null;
    }

    /// <summary>The branch address, or null for another host, a commit, a tag, or an address with a query.</summary>
    public static GitHubBranchAddress? Of(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, RawHost, StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0)
        {
            return null;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4)
            return null;

        if (segments[2] == "refs")
        {
            return segments.Length >= 6 && segments[3] == "heads"
                ? new GitHubBranchAddress(segments[0], segments[1], Uri.UnescapeDataString(segments[4]), string.Join('/', segments[5..]), IsSure: true)
                : null;
        }

        return IsCommit(segments[2])
            ? null
            : new GitHubBranchAddress(segments[0], segments[1], Uri.UnescapeDataString(segments[2]), string.Join('/', segments[3..]), IsSure: false);
    }

    /// <summary>Whether the text is a full or abbreviated commit hash.</summary>
    public static bool IsCommit(string text) => text.Length is >= 7 and <= 64 && text.All(char.IsAsciiHexDigitLower);
}

/// <summary>The branch that an image address follows, and the commit address of the same file when Borea found one.</summary>
/// <param name="Branch">The branch name, or null for a refs/heads/ address whose branch GitHub did not confirm.</param>
/// <param name="CommitUrl">The address of the file at the commit the branch points to, which has the same bytes, or null.</param>
public sealed record ListingImageBranch(string? Branch, string? CommitUrl);

/// <summary>What the check of a listed image read, and the branch its address follows.</summary>
/// <param name="Branch">The branch of an address on raw.githubusercontent.com, or null when the address follows no branch.</param>
public sealed record ListingImageCheck(ListingImageMeasurement Measurement, ListingImageBranch? Branch);

/// <summary>Measures an image an author already hosts.</summary>
public interface IListingImageMeasurer
{
    /// <summary>Fetches the image over HTTPS with the network rules of the image fetching of Borea, and measures it.</summary>
    Task<ListingImageMeasurement> MeasureAsync(string url, ListingImageRole role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the image of a listed record once. For an address on a branch of raw.githubusercontent.com it asks GitHub for the commit of
    /// the branch first and reads the file at that commit, so the same read gives the current bytes and proves the commit address.
    /// </summary>
    Task<ListingImageCheck> CheckAsync(string url, ListingImageRole role, CancellationToken cancellationToken = default);

    /// <summary>
    /// For an address that follows a branch on raw.githubusercontent.com, asks GitHub for the current commit of the branch and gives the
    /// address of the file at that commit when its bytes have <paramref name="sha256"/>. Null when the address follows no branch.
    /// </summary>
    Task<ListingImageBranch?> FindCommitAddressAsync(string url, ListingImageRole role, string sha256, CancellationToken cancellationToken = default);
}
