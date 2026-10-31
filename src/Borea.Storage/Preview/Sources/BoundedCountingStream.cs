namespace Borea.Storage.Preview.Sources;

/// <summary>
/// A forward-only read stream that counts the bytes its inner stream really gives and ends at a limit. Over a
/// decompressing stream this counts the decompressed bytes, so the limit holds whatever length the archive declares.
/// </summary>
/// <remarks>
/// When the limit is reached, the stream reads one more byte from the inner stream to tell an entry that is exactly
/// as long as the limit from a longer one. If that byte is there, <see cref="LimitExceeded"/> becomes true and the
/// stream reports its end. So the inner stream gives at most the limit plus one byte.
/// </remarks>
internal sealed class BoundedCountingStream : Stream
{
    private readonly Stream _inner;
    private readonly long _limit;
    private readonly Action<int>? _counted;

    /// <param name="counted">Called with every positive count the inner stream gives, so an owner can add up the bytes of several streams.</param>
    public BoundedCountingStream(Stream inner, long limit, Action<int>? counted = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        _inner = inner;
        _limit = limit;
        _counted = counted;
    }

    /// <summary>The bytes the inner stream gave so far.</summary>
    public long BytesRead { get; private set; }

    /// <summary>Whether the inner stream had more bytes than the limit.</summary>
    public bool LimitExceeded { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => BytesRead;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (LimitExceeded || buffer.IsEmpty)
            return 0;

        var remaining = _limit - BytesRead;
        if (remaining <= 0)
        {
            Span<byte> probe = stackalloc byte[1];
            if (Count(_inner.Read(probe)) > 0)
                LimitExceeded = true;

            return 0;
        }

        return Count(_inner.Read(buffer[..(int)Math.Min(buffer.Length, remaining)]));
    }

    public override int ReadByte()
    {
        Span<byte> one = stackalloc byte[1];
        return Read(one) == 1 ? one[0] : -1;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();

        base.Dispose(disposing);
    }

    private int Count(int read)
    {
        if (read > 0)
        {
            BytesRead += read;
            _counted?.Invoke(read);
        }

        return read;
    }
}
