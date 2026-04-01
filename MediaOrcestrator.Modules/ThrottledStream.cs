namespace MediaOrcestrator.Modules;

// TODO: Вроде работает терпимо, но на малых ограничениях скачки присутствуют
// Модифицированный ThrottledStream из https://github.com/bezzad/Downloader
public sealed class ThrottledStream : Stream
{
    private const long Infinite = long.MaxValue;
    private readonly Stream _baseStream;
    private long _bandwidthLimit;
    private long _bytesTransferred;
    private long _windowStart = Environment.TickCount64;

    public ThrottledStream(Stream baseStream, long? bandwidthLimit)
    {
        _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
        BandwidthLimit = bandwidthLimit ?? Infinite;
    }

    public long BandwidthLimit
    {
        get => _bandwidthLimit;
        set => _bandwidthLimit = value <= 0 ? Infinite : value;
    }

    public override bool CanRead => _baseStream.CanRead;
    public override bool CanSeek => _baseStream.CanSeek;
    public override bool CanWrite => _baseStream.CanWrite;
    public override long Length => _baseStream.Length;

    public override long Position
    {
        get => _baseStream.Position;
        set => _baseStream.Position = value;
    }

    private int MaxChunkSize => _bandwidthLimit >= Infinite
        ? int.MaxValue
        : (int)Math.Min(int.MaxValue, Math.Max(_bandwidthLimit / 10, 1024));

    public override void Flush()
    {
        _baseStream.Flush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return _baseStream.FlushAsync(cancellationToken);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _baseStream.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        _baseStream.SetLength(value);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var limited = Math.Min(count, MaxChunkSize);
        var result = _baseStream.Read(buffer, offset, limited);

        if (result > 0)
        {
            ThrottleSync(result);
        }

        return result;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var limited = Math.Min(count, MaxChunkSize);
        var result = await _baseStream.ReadAsync(buffer.AsMemory(offset, limited), cancellationToken).ConfigureAwait(false);

        if (result > 0)
        {
            await ThrottleAsync(result, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var limited = buffer[..Math.Min(buffer.Length, MaxChunkSize)];
        var result = await _baseStream.ReadAsync(limited, cancellationToken).ConfigureAwait(false);

        if (result > 0)
        {
            await ThrottleAsync(result, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        var pos = 0;

        while (pos < count)
        {
            var chunk = Math.Min(MaxChunkSize, count - pos);
            _baseStream.Write(buffer, offset + pos, chunk);
            ThrottleSync(chunk);
            pos += chunk;
        }
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var pos = 0;

        while (pos < count)
        {
            var chunk = Math.Min(MaxChunkSize, count - pos);
            await _baseStream.WriteAsync(buffer.AsMemory(offset + pos, chunk), cancellationToken).ConfigureAwait(false);
            await ThrottleAsync(chunk, cancellationToken).ConfigureAwait(false);
            pos += chunk;
        }
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var pos = 0;

        while (pos < buffer.Length)
        {
            var chunk = Math.Min(MaxChunkSize, buffer.Length - pos);
            await _baseStream.WriteAsync(buffer.Slice(pos, chunk), cancellationToken).ConfigureAwait(false);
            await ThrottleAsync(chunk, cancellationToken).ConfigureAwait(false);
            pos += chunk;
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await _baseStream.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    public override string ToString()
    {
        return _baseStream.ToString() ?? string.Empty;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _baseStream.Dispose();
        }

        base.Dispose(disposing);
    }

    private async ValueTask ThrottleAsync(int byteCount, CancellationToken cancellationToken)
    {
        if (_bandwidthLimit >= Infinite)
        {
            return;
        }

        _bytesTransferred += byteCount;
        var elapsed = Environment.TickCount64 - _windowStart;
        var expectedMs = (double)_bytesTransferred / _bandwidthLimit * 1000;
        var delayMs = (int)(expectedMs - elapsed);

        if (delayMs > 0)
        {
            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }

        if (elapsed >= 1000)
        {
            _bytesTransferred = 0;
            _windowStart = Environment.TickCount64;
        }
    }

    private void ThrottleSync(int byteCount)
    {
        if (_bandwidthLimit >= Infinite)
        {
            return;
        }

        _bytesTransferred += byteCount;
        var elapsed = Environment.TickCount64 - _windowStart;
        var expectedMs = (double)_bytesTransferred / _bandwidthLimit * 1000;
        var delayMs = (int)(expectedMs - elapsed);

        if (delayMs > 0)
        {
            Thread.Sleep(delayMs);
        }

        if (elapsed >= 1000)
        {
            _bytesTransferred = 0;
            _windowStart = Environment.TickCount64;
        }
    }
}
