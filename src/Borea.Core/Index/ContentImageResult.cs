namespace Borea.Core.Index;

/// <summary>Why an image record has no bytes to show, so a client shows its placeholder.</summary>
public enum ContentImageFailure
{
    /// <summary>Loading images from author hosts is off, and the image is not in the cache.</summary>
    DisabledByPreference,

    /// <summary>The host did not answer, timed out, failed, or limited the rate, so a later try can work.</summary>
    Unavailable,

    /// <summary>The host answered with a status that is not the image.</summary>
    NotFound,

    /// <summary>The URL carries credentials, or its host resolves to an address that is not public.</summary>
    BlockedNetworkTarget,

    /// <summary>A redirect has no HTTPS target, or the chain has more than three redirects.</summary>
    RedirectNotAllowed,

    TooLarge,

    /// <summary>The bytes are not a PNG, JPEG or WebP image that can be read.</summary>
    UnsupportedFormat,

    Animated,

    /// <summary>The pixel size of the bytes is outside the limits of the image's role.</summary>
    OutsideLimits,

    /// <summary>The width, height, size or SHA-256 of the bytes differs from the record.</summary>
    FactsMismatch,
}

/// <summary>The verified bytes of an image record, or why there are none.</summary>
public sealed class ContentImageResult
{
    private readonly byte[]? _bytes;

    private ContentImageResult(byte[]? bytes, ContentImageFailure? failure, string? reason)
    {
        _bytes = bytes;
        Failure = failure;
        Reason = reason;
    }

    public bool IsLoaded => Failure is null;

    /// <summary>The verified image bytes, or empty when the image did not load.</summary>
    public ReadOnlyMemory<byte> Bytes => _bytes;

    public ContentImageFailure? Failure { get; }

    /// <summary>What went wrong in words for a log, or null when the image loaded.</summary>
    public string? Reason { get; }

    public static ContentImageResult Loaded(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return new(bytes, null, null);
    }

    public static ContentImageResult Failed(ContentImageFailure failure, string reason)
    {
        if (!Enum.IsDefined(failure))
            throw new ArgumentException("The failure is not defined.", nameof(failure));

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("The reason cannot be empty.", nameof(reason));

        return new(null, failure, reason);
    }
}
