using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using Borea.Core.Preview;
using Borea.Preview.Scene;
using Borea.Preview.Textures;

namespace Borea.Preview.Tests.Textures;

public sealed class PreviewTextureLoaderTests
{
    private static readonly PreviewAssetRef Texture = new("Core", "Textures/A.ktx2");

    [Fact]
    public async Task AZstdKtx2WithBc7_IsBoxDownscaledToTheTarget()
    {
        const int Size = 16;
        var pixels = Enumerable.Range(0, Size * Size).SelectMany(_ => new byte[] { 200, 100, 50, 255 }).ToArray();
        var encoder = new BcEncoder(CompressionFormat.Bc7);
        encoder.OutputOptions.GenerateMipMaps = false;
        var blocks = encoder.EncodeToRawBytes(pixels, Size, Size, PixelFormat.Rgba32, 0, out _, out _);
        var source = new FakeSource { Files = { [Texture.Path] = Ktx2TestWriter.Write(145, Size, Size, [blocks], scheme: 2) } };

        var (image, notice) = await new PreviewTextureLoader(null).LoadAsync(source, Texture, 4, CancellationToken.None);

        Assert.Null(notice);
        Assert.Equal((4, 4), (image!.Width, image.Height));
        Assert.All(image.Rgba.Chunk(4), pixel => Assert.InRange(Math.Abs(pixel[0] - 200) + Math.Abs(pixel[1] - 100) + Math.Abs(pixel[2] - 50), 0, 6));
    }

    [Fact]
    public async Task APng_GoesThroughTheDecoderAtTheTargetWidth()
    {
        var decoder = new FakeDecoder(64, 32);
        var source = new FakeSource { Files = { [Texture.Path] = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0] } };

        var (image, notice) = await new PreviewTextureLoader(decoder).LoadAsync(source, Texture, 16, CancellationToken.None);

        Assert.Null(notice);
        Assert.Equal(16, decoder.DecodedWidth);
        Assert.Equal((16, 8), (image!.Width, image.Height));
    }

    [Theory]
    [InlineData(20000, 20000)]
    [InlineData(8192, 8192)]
    public async Task AnImageThatReportsAHugeSize_IsRefusedBeforeItDecodes(int width, int height)
    {
        var decoder = new FakeDecoder(width, height);
        var source = new FakeSource { Files = { [Texture.Path] = [0xFF, 0xD8, 0xFF, 0xE0] } };

        var (image, notice) = await new PreviewTextureLoader(decoder).LoadAsync(source, Texture, 512, CancellationToken.None);

        Assert.Null(image);
        Assert.Equal(PreviewNoticeKind.UnsupportedTexture, notice!.Kind);
        Assert.Equal(0, decoder.DecodeCount);
    }

    [Fact]
    public async Task AnUnknownMagic_GivesUnsupportedTexture()
    {
        var source = new FakeSource { Files = { [Texture.Path] = "DDS |not a texture"u8.ToArray() } };

        var (image, notice) = await new PreviewTextureLoader(new FakeDecoder(4, 4)).LoadAsync(source, Texture, 512, CancellationToken.None);

        Assert.Null(image);
        Assert.Equal(new PreviewNotice(PreviewNoticeKind.UnsupportedTexture, "Core/Textures/A.ktx2", "The file is not KTX2, PNG or JPEG."), notice);
    }

    [Fact]
    public async Task ASourceThatThrowsIOException_GivesAssetFailed()
    {
        var source = new FakeSource { Throw = true };

        var (image, notice) = await new PreviewTextureLoader(null).LoadAsync(source, Texture, 512, CancellationToken.None);

        Assert.Null(image);
        Assert.Equal(PreviewNoticeKind.AssetFailed, notice!.Kind);
    }

    [Fact]
    public async Task ADecoderThatThrowsAnyOtherException_GivesAssetFailed()
    {
        var decoder = new FakeDecoder(64, 64) { DecodeException = new IndexOutOfRangeException("A bug in a decoder.") };
        var source = new FakeSource { Files = { [Texture.Path] = [0xFF, 0xD8, 0xFF, 0xE0] } };

        var (image, notice) = await new PreviewTextureLoader(decoder).LoadAsync(source, Texture, 512, CancellationToken.None);

        Assert.Null(image);
        Assert.Equal(new PreviewNotice(PreviewNoticeKind.AssetFailed, "Core/Textures/A.ktx2", "A bug in a decoder."), notice);
    }

    [Fact]
    public async Task ASecondLoad_ComesFromTheCacheWithoutAnOpen()
    {
        var source = new FakeSource { Files = { [Texture.Path] = Ktx2TestWriter.Write(37, 2, 2, [new byte[16]]) } };
        var loader = new PreviewTextureLoader(null);

        var first = await loader.LoadAsync(source, Texture, 512, CancellationToken.None);
        var second = await loader.LoadAsync(source, Texture, 512, CancellationToken.None);

        Assert.Same(first.Image, second.Image);
        Assert.Equal(1, source.Opens);
    }

    [Fact]
    public void TheCache_EvictsTheLeastRecentlyUsedImageAboveItsCap_AndKeepsNoImageLargerThanTheCap()
    {
        var cache = new TextureLruCache(32);
        cache.Add("a", Image(2));
        cache.Add("b", Image(2));
        Assert.True(cache.TryGet("a", out _));

        cache.Add("c", Image(2));
        cache.Add("d", Image(3));

        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.False(cache.TryGet("d", out _));
        Assert.Equal((2, 32L), (cache.Count, cache.Bytes));
    }

    [Fact]
    public async Task ACancellationDuringTheLoad_StopsItBeforeTheDecode()
    {
        using var cancellation = new CancellationTokenSource();
        var decoder = new FakeDecoder(64, 64) { OnReadSize = cancellation.Cancel };
        var source = new FakeSource { Files = { [Texture.Path] = [0xFF, 0xD8, 0xFF, 0xE0] } };
        var loader = new PreviewTextureLoader(decoder);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loader.LoadAsync(source, Texture, 512, cancellation.Token));

        Assert.Equal(0, decoder.DecodeCount);
        Assert.Equal(0, loader.Cache.Count);
    }

    private static PreviewImage Image(int size) => new(size, size, new byte[size * size * 4]);

    private sealed class FakeSource : IPreviewAssetSource
    {
        private int _opens;

        public Dictionary<string, byte[]> Files { get; } = [];

        public bool Throw { get; init; }

        public int Opens => _opens;

        public Stream? TryOpenRead(PreviewAssetRef asset)
        {
            Interlocked.Increment(ref _opens);
            if (Throw)
            {
                throw new IOException("The archive is gone.");
            }

            return Files.TryGetValue(asset.Path, out var bytes) ? new MemoryStream(bytes, writable: false) : null;
        }

        public Stream? TryOpenHead(PreviewAssetRef asset) => TryOpenRead(asset);

        public string GetCacheKey(PreviewAssetRef asset) => $"{asset.Source}/{asset.Path}";

        public void Dispose()
        {
        }
    }

    private sealed class FakeDecoder(int width, int height) : IRgbaImageDecoder
    {
        public Action? OnReadSize { get; init; }

        public Exception? DecodeException { get; init; }

        public int DecodeCount { get; private set; }

        public int DecodedWidth { get; private set; }

        public bool TryReadSize(Stream stream, out int reportedWidth, out int reportedHeight)
        {
            OnReadSize?.Invoke();
            (reportedWidth, reportedHeight) = (width, height);
            return true;
        }

        public PreviewImage Decode(Stream stream, int decodeWidth)
        {
            Assert.Equal(0, stream.Position);
            DecodeCount++;
            if (DecodeException is not null)
            {
                throw DecodeException;
            }

            DecodedWidth = decodeWidth;
            var decodeHeight = Math.Max(1, height * decodeWidth / width);
            return new PreviewImage(decodeWidth, decodeHeight, new byte[decodeWidth * decodeHeight * 4]);
        }
    }
}
