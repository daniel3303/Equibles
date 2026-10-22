using System.Runtime.InteropServices;

namespace Equibles.Media.BusinessLogic.Storage;

/// <summary>
/// A descriptor on the store's filesystem whose <see cref="Sync"/> flushes every dirty page of
/// it (Linux syncfs) and throws when that fails. Linux reports to syncfs only the writeback
/// errors recorded after the descriptor opened, so a batch opens it before its first buffered
/// write. Elsewhere (development only) it falls back to a global sync, which reports nothing.
/// </summary>
public sealed class FileSystemSyncHandle : IDisposable
{
    private readonly string _directory;
    private int _fd;

    private FileSystemSyncHandle(string directory, int fd)
    {
        _directory = directory;
        _fd = fd;
    }

    public static FileSystemSyncHandle Open(string directory)
    {
        if (
            !RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            && !RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        )
        {
            return new FileSystemSyncHandle(directory, -1);
        }

        var fd = DurableFileWriter.open(directory, DurableFileWriter.O_RDONLY);
        if (fd < 0)
        {
            throw new IOException(
                $"Cannot open {directory} to flush its filesystem (errno {Marshal.GetLastPInvokeError()})."
            );
        }

        return new FileSystemSyncHandle(directory, fd);
    }

    public void Sync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (DurableFileWriter.syncfs(_fd) != 0)
            {
                throw new IOException(
                    $"Flushing the filesystem of {_directory} failed (errno {Marshal.GetLastPInvokeError()})."
                );
            }

            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            DurableFileWriter.sync();
        }
    }

    public void Dispose()
    {
        if (_fd >= 0)
        {
            DurableFileWriter.close(_fd);
            _fd = -1;
        }
    }
}
