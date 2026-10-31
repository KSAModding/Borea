using System.Security.Cryptography;

namespace Borea.Storage.Files;

/// <summary>Writes through to another stream and keeps the SHA-256 and the count of the bytes written, so a file is hashed while it is written.</summary>
internal sealed class HashingWriteStream : Stream
{
    private readonly Stream _inner;
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long _written;

    public HashingWriteStream(Stream inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => _written;

    public override long Position
    {
        get => _written;
        set => throw new NotSupportedException();
    }

    /// <summary>The SHA-256 of the bytes written so far, in lower-case hex.</summary>
    public string Sha256() => Convert.ToHexStringLower(_hash.GetCurrentHash());

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _inner.Write(buffer);
        _hash.AppendData(buffer);
        _written += buffer.Length;
    }

    public override void Flush() => _inner.Flush();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _hash.Dispose();
        base.Dispose(disposing);
    }
}
