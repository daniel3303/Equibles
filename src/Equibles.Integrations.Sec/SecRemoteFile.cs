using Equibles.Integrations.Sec.Contracts;

namespace Equibles.Integrations.Sec;

/// <summary>
/// A read-only, seekable view of a large sec.gov file that fetches fixed blocks with byte-range
/// requests through <see cref="ISecEdgarClient.DownloadRange"/>, so a <c>ZipArchive</c> can read
/// its central directory and a few entries without downloading the whole archive. The two most
/// recent blocks stay cached, which covers the directory read at the end and a sequential entry
/// read.
/// </summary>
public sealed class SecRemoteFile : Stream
{
    public const int DefaultBlockSize = 8 * 1024 * 1024;

    private readonly ISecEdgarClient _client;
    private readonly string _url;
    private readonly int _blockSize;
    private readonly CancellationToken _cancellationToken;
    private readonly LinkedList<(long Index, byte[] Bytes)> _blocks = new();
    private long _length;
    private long _position;

    private SecRemoteFile(
        ISecEdgarClient client,
        string url,
        int blockSize,
        CancellationToken cancellationToken
    )
    {
        _client = client;
        _url = url;
        _blockSize = blockSize;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Opens the file, reading one byte to learn its length.</summary>
    public static async Task<SecRemoteFile> Open(
        ISecEdgarClient client,
        string url,
        int blockSize = DefaultBlockSize,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);
        var file = new SecRemoteFile(client, url, blockSize, cancellationToken);
        var probe = await client.DownloadRange(url, 0, 1, cancellationToken);
        file._length = probe.TotalLength;
        return file;
    }

    /// <summary>Range requests made so far, the length probe included.</summary>
    public int Requests { get; private set; } = 1;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_position >= _length || buffer.Length == 0)
            return 0;
        var index = _position / _blockSize;
        var block = Block(index);
        var within = (int)(_position - index * _blockSize);
        var read = Math.Min(buffer.Length, block.Length - within);
        block.AsSpan(within, read).CopyTo(buffer);
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        ArgumentOutOfRangeException.ThrowIfNegative(target, nameof(offset));
        _position = target;
        return _position;
    }

    public override void Flush() { }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    // ZipArchive and DeflateStream read synchronously; the fetch blocks this worker thread only.
    private byte[] Block(long index)
    {
        for (var node = _blocks.First; node != null; node = node.Next)
        {
            if (node.Value.Index != index)
                continue;
            _blocks.Remove(node);
            _blocks.AddFirst(node);
            return node.Value.Bytes;
        }

        var start = index * _blockSize;
        var count = (int)Math.Min(_blockSize, _length - start);
        var range = _client
            .DownloadRange(_url, start, count, _cancellationToken)
            .GetAwaiter()
            .GetResult();
        Requests++;
        _blocks.AddFirst((index, range.Bytes));
        if (_blocks.Count > 2)
            _blocks.RemoveLast();
        return range.Bytes;
    }
}
