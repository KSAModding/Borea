using System.Globalization;
using Borea.Core.Preview;
using Borea.Preview.Scene;

namespace Borea.Preview.Textures;

/// <summary>
/// Turns a texture asset into a <see cref="PreviewImage"/> whose larger side is at most the target, or into a notice
/// that says why the material keeps its flat color. The format comes from the first bytes of the file and not from
/// its name, because a mod can name its files as it likes. KTX2 goes through <see cref="Ktx2Reader"/> and
/// <see cref="BcDecoder"/>, PNG and JPEG go through the <see cref="IRgbaImageDecoder"/> of the App, and any other
/// format gives <see cref="PreviewNoticeKind.UnsupportedTexture"/>.
/// </summary>
/// <remarks>
/// At most <see cref="MaxParallelLoads"/> loads decode at the same time. The decoded images stay in a least recently
/// used cache that is keyed by <see cref="IPreviewAssetSource.GetCacheKey"/> and the target. For one file the target
/// selects exactly one KTX2 level, so this key is the key of the level, and a hit needs no open of the file.
/// </remarks>
public sealed class PreviewTextureLoader
{
    /// <summary>The largest width or height of a level or image that the loader decodes.</summary>
    public const int MaxImageSide = 8192;

    public const long DefaultCacheBytes = 64L * 1024 * 1024;

    public const int MaxParallelLoads = 4;

    private const int MagicBytes = 12;

    private readonly IRgbaImageDecoder? _imageDecoder;
    private readonly TextureLruCache _cache;

    // The semaphore is not disposed, because the loader never asks for its wait handle, and a dispose while a load
    // still waits would make that load throw.
    private readonly SemaphoreSlim _slots = new(MaxParallelLoads, MaxParallelLoads);

    /// <param name="imageDecoder">The decoder for PNG and JPEG, or null when those formats keep the flat color.</param>
    public PreviewTextureLoader(IRgbaImageDecoder? imageDecoder, long cacheBytes = DefaultCacheBytes)
    {
        _imageDecoder = imageDecoder;
        _cache = new TextureLruCache(cacheBytes);
    }

    internal TextureLruCache Cache => _cache;

    /// <summary>
    /// Loads the texture at <paramref name="asset"/> with its larger side at most <paramref name="target"/> pixels.
    /// A file that is missing, damaged, too large or of another format gives a null image and a notice, and never an
    /// exception.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ObjectDisposedException">The source was disposed, which the caller treats as a cancellation.</exception>
    public async Task<(PreviewImage? Image, PreviewNotice? Notice)> LoadAsync(
        IPreviewAssetSource source,
        PreviewAssetRef asset,
        int target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentOutOfRangeException.ThrowIfLessThan(target, 1);
        cancellationToken.ThrowIfCancellationRequested();

        string key;
        try
        {
            key = source.GetCacheKey(asset) + "#" + target.ToString(CultureInfo.InvariantCulture);
        }
        catch (IOException exception)
        {
            return Failed(asset, exception.Message);
        }

        if (_cache.TryGet(key, out var cached))
        {
            return (cached, null);
        }

        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A load of the same file that held a slot before this one may have filled the cache meanwhile.
            if (_cache.TryGet(key, out cached))
            {
                return (cached, null);
            }

            var result = await Task.Run(() => Load(source, asset, target, cancellationToken), cancellationToken).ConfigureAwait(false);
            if (result.Image is { } image)
            {
                _cache.Add(key, image);
            }

            return result;
        }
        finally
        {
            _slots.Release();
        }
    }

    private (PreviewImage? Image, PreviewNotice? Notice) Load(IPreviewAssetSource source, PreviewAssetRef asset, int target, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = source.TryOpenRead(asset);
            if (stream is null)
            {
                return Failed(asset, "The file is missing or too large.");
            }

            Span<byte> magic = stackalloc byte[MagicBytes];
            var read = stream.ReadAtLeast(magic, MagicBytes, throwOnEndOfStream: false);
            magic = magic[..read];
            stream.Position = 0;

            if (magic.StartsWith(Ktx2Magic))
            {
                return LoadKtx2(stream, asset, target, cancellationToken);
            }

            if (magic.StartsWith(PngMagic) || magic.StartsWith(JpegMagic))
            {
                return LoadRaster(stream, asset, target, cancellationToken);
            }

            return Unsupported(asset, "The file is not KTX2, PNG or JPEG.");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or FormatException)
        {
            return Failed(asset, exception.Message);
        }
    }

    private static ReadOnlySpan<byte> Ktx2Magic => [0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> PngMagic => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> JpegMagic => [0xFF, 0xD8, 0xFF];

    private static (PreviewImage?, PreviewNotice?) LoadKtx2(Stream stream, PreviewAssetRef asset, int target, CancellationToken cancellationToken)
    {
        var header = Ktx2Reader.ReadHeader(stream);
        if (header.Value is not { } ktx)
        {
            return FromKtx2Status(asset, header.Status, header.Problem);
        }

        var level = Ktx2Reader.ChooseLevel(ktx, target);
        var width = ktx.LevelWidth(level);
        var height = ktx.LevelHeight(level);
        if (width > MaxImageSide || height > MaxImageSide)
        {
            return TooLarge(asset, width, height);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var data = Ktx2Reader.ReadLevel(stream, ktx, level);
        if (data.Value is not { } bytes)
        {
            return FromKtx2Status(asset, data.Status, data.Problem);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!BcDecoder.TryDecode(bytes.Format, bytes.Data, width, height, out var rgba))
        {
            return Unsupported(asset, $"The format {bytes.Format} is not decoded.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return (BoxDownscale(new PreviewImage(width, height, rgba!), target), null);
    }

    private (PreviewImage?, PreviewNotice?) LoadRaster(Stream stream, PreviewAssetRef asset, int target, CancellationToken cancellationToken)
    {
        if (_imageDecoder is null)
        {
            return Unsupported(asset, "PNG and JPEG need an image decoder, and none is set.");
        }

        if (!_imageDecoder.TryReadSize(stream, out var width, out var height) || width <= 0 || height <= 0)
        {
            return Failed(asset, "The image header could not be read.");
        }

        if (width > MaxImageSide || height > MaxImageSide)
        {
            return TooLarge(asset, width, height);
        }

        cancellationToken.ThrowIfCancellationRequested();
        stream.Position = 0;
        var scale = Math.Min(1.0, (double)target / Math.Max(width, height));
        var image = _imageDecoder.Decode(stream, Math.Max(1, (int)Math.Round(width * scale)));

        cancellationToken.ThrowIfCancellationRequested();
        return (BoxDownscale(image, target), null);
    }

    /// <summary>
    /// Averages square boxes of a whole number of pixels, so that the larger side becomes at most
    /// <paramref name="target"/>. The boxes at the right and bottom edges can be smaller, and they average only the
    /// pixels they cover.
    /// </summary>
    internal static PreviewImage BoxDownscale(PreviewImage image, int target)
    {
        var largest = Math.Max(image.Width, image.Height);
        if (largest <= target)
        {
            return image;
        }

        var factor = (largest + target - 1) / target;
        var width = (image.Width + factor - 1) / factor;
        var height = (image.Height + factor - 1) / factor;
        var source = image.Rgba;
        var rgba = new byte[width * height * 4];
        Span<int> sums = stackalloc int[4];
        for (var y = 0; y < height; y++)
        {
            var top = y * factor;
            var bottom = Math.Min(top + factor, image.Height);
            for (var x = 0; x < width; x++)
            {
                var left = x * factor;
                var right = Math.Min(left + factor, image.Width);
                sums.Clear();
                for (var sy = top; sy < bottom; sy++)
                {
                    var row = ((sy * image.Width) + left) * 4;
                    for (var i = row; i < row + ((right - left) * 4); i += 4)
                    {
                        sums[0] += source[i];
                        sums[1] += source[i + 1];
                        sums[2] += source[i + 2];
                        sums[3] += source[i + 3];
                    }
                }

                var count = (bottom - top) * (right - left);
                var output = ((y * width) + x) * 4;
                for (var channel = 0; channel < 4; channel++)
                {
                    rgba[output + channel] = (byte)((sums[channel] + (count / 2)) / count);
                }
            }
        }

        return new PreviewImage(width, height, rgba);
    }

    private static (PreviewImage?, PreviewNotice?) FromKtx2Status(PreviewAssetRef asset, Ktx2Status status, string problem) =>
        status == Ktx2Status.Unsupported ? Unsupported(asset, problem) : Failed(asset, problem);

    private static (PreviewImage?, PreviewNotice?) TooLarge(PreviewAssetRef asset, int width, int height) =>
        Unsupported(asset, $"The size {width}x{height} is larger than {MaxImageSide} pixels.");

    private static (PreviewImage?, PreviewNotice?) Unsupported(PreviewAssetRef asset, string detail) =>
        (null, new PreviewNotice(PreviewNoticeKind.UnsupportedTexture, Subject(asset), detail));

    private static (PreviewImage?, PreviewNotice?) Failed(PreviewAssetRef asset, string detail) =>
        (null, new PreviewNotice(PreviewNoticeKind.AssetFailed, Subject(asset), detail));

    private static string Subject(PreviewAssetRef asset) => $"{asset.Source}/{asset.Path}";
}
