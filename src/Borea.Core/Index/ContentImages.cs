using System.Collections.ObjectModel;

namespace Borea.Core.Index;

/// <summary>The images of one authored document: an optional icon and the images its description references.</summary>
public sealed class ContentImages
{
    public const int MaxDescriptionImages = 16;

    public IconImage? Icon { get; }

    public IReadOnlyList<DescriptionImage> Description { get; }

    public ContentImages(IconImage? icon, IReadOnlyList<DescriptionImage> description)
    {
        ArgumentNullException.ThrowIfNull(description);

        var copy = description.ToArray();
        if (copy.Any(image => image is null))
            throw new ArgumentException("A description image cannot be null.", nameof(description));

        if (copy.Length > MaxDescriptionImages)
            throw new ArgumentException($"A document can have at most {MaxDescriptionImages} description images.", nameof(description));

        if (copy.Select(image => image.Id).Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Each description image id can appear only once.", nameof(description));

        Icon = icon;
        Description = new ReadOnlyCollection<DescriptionImage>(copy);
    }

    /// <summary>The description image with <paramref name="id"/>, compared case-sensitively, or null.</summary>
    public DescriptionImage? FindDescriptionImage(string id) =>
        Description.FirstOrDefault(image => string.Equals(image.Id, id, StringComparison.Ordinal));
}

/// <summary>The facts an image record states about an image on the author's host.</summary>
public abstract class ContentImage
{
    public string Url { get; }

    /// <summary>Hex SHA-256 of the image bytes, normalized to uppercase.</summary>
    public string Sha256 { get; }

    public int Width { get; }

    public int Height { get; }

    public long SizeBytes { get; }

    /// <summary>The SPDX license expression of the image. Null when the image is under the license of its document.</summary>
    public string? License { get; }

    public string? Attribution { get; }

    /// <summary>The HTTPS URL of the original work.</summary>
    public string? Source { get; }

    private protected ContentImage(
        string url,
        string sha256,
        int width,
        int height,
        long sizeBytes,
        string? license,
        string? attribution,
        string? source)
    {
        if (!IsHttps(url))
            throw new ArgumentException("The image url must be an absolute HTTPS URL.", nameof(url));

        if (sha256 is null || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            throw new ArgumentException("The image sha256 must be 64 hex characters.", nameof(sha256));

        if (width < 1)
            throw new ArgumentOutOfRangeException(nameof(width), width, "The image width must be positive.");

        if (height < 1)
            throw new ArgumentOutOfRangeException(nameof(height), height, "The image height must be positive.");

        if (sizeBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes, "The image size must be positive.");

        if (license is not null && string.IsNullOrWhiteSpace(license))
            throw new ArgumentException("The image license cannot be empty.", nameof(license));

        if (attribution is not null && string.IsNullOrWhiteSpace(attribution))
            throw new ArgumentException("The image attribution cannot be empty.", nameof(attribution));

        if (source is not null && !IsHttps(source))
            throw new ArgumentException("The image source must be an absolute HTTPS URL.", nameof(source));

        Url = url;
        Sha256 = sha256.ToUpperInvariant();
        Width = width;
        Height = height;
        SizeBytes = sizeBytes;
        License = license;
        Attribution = attribution;
        Source = source;
    }

    private static bool IsHttps(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}

/// <summary>The square image that stands for a listing in lists, tiles and headers.</summary>
public sealed class IconImage : ContentImage
{
    public const int MinPixels = 256;

    public const int MaxPixels = 1024;

    public const long MaxBytes = 256 * 1024;

    public IconImage(
        string url,
        string sha256,
        int width,
        int height,
        long sizeBytes,
        string? license = null,
        string? attribution = null,
        string? source = null)
        : base(url, sha256, width, height, sizeBytes, license, attribution, source)
    {
        if (width != height)
            throw new ArgumentException($"The icon must be square, but is {width} by {height} pixels.", nameof(height));

        if (width is < MinPixels or > MaxPixels)
            throw new ArgumentOutOfRangeException(nameof(width), width, $"The icon side must be {MinPixels} to {MaxPixels} pixels.");

        if (sizeBytes > MaxBytes)
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes, $"The icon can be at most {MaxBytes} bytes.");
    }
}

/// <summary>An image that the description of its document references by <see cref="Id"/>.</summary>
public sealed class DescriptionImage : ContentImage
{
    public const int MaxPixels = 2048;

    public const long MaxBytes = 1024 * 1024;

    public const int MaxIdLength = 64;

    /// <summary>The case-sensitive name that a ksa-image reference in the description uses.</summary>
    public string Id { get; }

    public DescriptionImage(
        string id,
        string url,
        string sha256,
        int width,
        int height,
        long sizeBytes,
        string? license = null,
        string? attribution = null,
        string? source = null)
        : base(url, sha256, width, height, sizeBytes, license, attribution, source)
    {
        if (!IsValidId(id))
            throw new ArgumentException($"The description image id '{id}' must be 1 to {MaxIdLength} ASCII letters, digits, '-' and '_', and start and end with a letter or digit.", nameof(id));

        if (width > MaxPixels)
            throw new ArgumentOutOfRangeException(nameof(width), width, $"A description image side can be at most {MaxPixels} pixels.");

        if (height > MaxPixels)
            throw new ArgumentOutOfRangeException(nameof(height), height, $"A description image side can be at most {MaxPixels} pixels.");

        if (sizeBytes > MaxBytes)
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes, $"A description image can be at most {MaxBytes} bytes.");

        Id = id;
    }

    public static bool IsValidId(string? id) =>
        id is { Length: > 0 and <= MaxIdLength }
        && char.IsAsciiLetterOrDigit(id[0])
        && char.IsAsciiLetterOrDigit(id[^1])
        && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
