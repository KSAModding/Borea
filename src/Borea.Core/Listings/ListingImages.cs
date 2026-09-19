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
    private ListingImageMeasurement(string? sha256, int width, int height, long size, string? format, string? problem)
    {
        Sha256 = sha256;
        Width = width;
        Height = height;
        Size = size;
        Format = format;
        Problem = problem;
    }

    public bool IsMeasured => Problem is null;

    /// <summary>Lowercase hex SHA-256 of the bytes.</summary>
    public string? Sha256 { get; }

    public int Width { get; }

    public int Height { get; }

    public long Size { get; }

    public string? Format { get; }

    public string? Problem { get; }

    public static ListingImageMeasurement Failed(string problem) => new(null, 0, 0, 0, null, problem);

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

/// <summary>Measures an image an author already hosts.</summary>
public interface IListingImageMeasurer
{
    /// <summary>Fetches the image over HTTPS with the network rules of the image fetching of Borea, and measures it.</summary>
    Task<ListingImageMeasurement> MeasureAsync(string url, ListingImageRole role, CancellationToken cancellationToken = default);
}
