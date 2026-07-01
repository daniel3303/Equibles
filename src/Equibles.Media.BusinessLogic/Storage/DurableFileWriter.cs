using System.Runtime.InteropServices;

namespace Equibles.Media.BusinessLogic.Storage;

/// <summary>
/// Crash-safe, write-once file creation for the content-addressed store. Sequence:
/// write a temp file on the same filesystem → fsync the file → atomic rename into
/// place → fsync the containing directory (so the new directory entry survives a
/// crash). Because the store is content-addressed, a target that already exists holds
/// identical bytes, so the write is skipped (deduplication). Writing the file before
/// the caller saves the File row means a crash leaves a harmless orphan file (reclaimed
/// by the sweep), never a row pointing at nothing.
/// </summary>
public static class DurableFileWriter
{
    private const int BufferSize = 1 << 16;

    public static async Task WriteIfMissing(string fullPath, byte[] content)
    {
        if (System.IO.File.Exists(fullPath))
        {
            return; // dedup: identical content already stored
        }

        var directory = Path.GetDirectoryName(fullPath);

        // Record the deepest ancestor that already exists before we create the shard chain,
        // so afterwards we can fsync every directory whose entries changed — not just the
        // leaf. Sharding creates fresh <xx>/<yy> dirs; persisting the file's dirent alone
        // would still lose those parent dirents on a crash, orphaning the fsynced file.
        var deepestExistingAncestor = directory;
        while (deepestExistingAncestor != null && !Directory.Exists(deepestExistingAncestor))
        {
            deepestExistingAncestor = Path.GetDirectoryName(deepestExistingAncestor);
        }

        Directory.CreateDirectory(directory);

        // Temp file lives in the final directory so the rename stays on one filesystem
        // (a cross-device rename is not atomic). The leading dot + .tmp suffix let the
        // orphan sweep recognize and skip in-flight temp files.
        var tempPath = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            await using (
                var stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    BufferSize,
                    FileOptions.None
                )
            )
            {
                await stream.WriteAsync(content);
                stream.Flush(flushToDisk: true); // fsync the file's data + metadata
            }

            try
            {
                System.IO.File.Move(tempPath, fullPath, overwrite: false);
            }
            catch (IOException) when (System.IO.File.Exists(fullPath))
            {
                // A concurrent writer won the race with identical (content-addressed) bytes.
                return;
            }

            // Persist the file's dirent (leaf) and each freshly-created shard directory,
            // walking up to and including the first pre-existing ancestor (its entry set
            // also changed by gaining the topmost new child).
            for (var dir = directory; dir != null; dir = Path.GetDirectoryName(dir))
            {
                FsyncDirectory(dir);
                if (dir == deepestExistingAncestor)
                {
                    break;
                }
            }
        }
        finally
        {
            if (System.IO.File.Exists(tempPath))
            {
                try
                {
                    System.IO.File.Delete(tempPath);
                }
                catch (IOException)
                {
                    // Best-effort cleanup; the orphan sweep removes any straggler temp files.
                }
            }
        }
    }

    /// <summary>
    /// Like <see cref="WriteIfMissing"/> but WITHOUT any fsync — temp file + atomic rename
    /// only, leaving the data in the page cache. For bulk migration where the caller makes a
    /// whole batch durable at once via <see cref="SyncFileSystem"/> before committing the
    /// matching database rows: per-file fsyncs on a spinning disk are seek-bound (a few
    /// dozen per second), while buffered writes stream at full disk bandwidth.
    /// </summary>
    public static async Task WriteIfMissingBuffered(string fullPath, byte[] content)
    {
        if (System.IO.File.Exists(fullPath))
        {
            return; // dedup: identical content already stored
        }

        var directory = Path.GetDirectoryName(fullPath);
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await System.IO.File.WriteAllBytesAsync(tempPath, content);
            try
            {
                System.IO.File.Move(tempPath, fullPath, overwrite: false);
            }
            catch (IOException) when (System.IO.File.Exists(fullPath))
            {
                // A concurrent writer won the race with identical (content-addressed) bytes.
            }
        }
        finally
        {
            if (System.IO.File.Exists(tempPath))
            {
                try
                {
                    System.IO.File.Delete(tempPath);
                }
                catch (IOException)
                {
                    // Best-effort cleanup; the orphan sweep removes any straggler temp files.
                }
            }
        }
    }

    /// <summary>
    /// Flushes every dirty page of the filesystem containing <paramref name="directory"/> to
    /// stable storage (Linux syncfs). The batch-durability counterpart of
    /// <see cref="WriteIfMissingBuffered"/>: call once after writing a batch, before
    /// committing the database rows that point at it. Falls back to a global sync elsewhere.
    /// </summary>
    public static void SyncFileSystem(string directory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var fd = open(directory, O_RDONLY);
            if (fd >= 0)
            {
                try
                {
                    syncfs(fd);
                }
                finally
                {
                    close(fd);
                }
                return;
            }
        }

        if (
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        )
        {
            sync();
        }
    }

    /// <summary>
    /// Persists a directory's entries to stable storage. .NET has no built-in directory
    /// fsync, so on Unix we open the directory and fsync its descriptor. No-op on Windows
    /// (development only; production runs on Linux), where directory handles can't be fsynced.
    /// </summary>
    private static void FsyncDirectory(string directory)
    {
        if (
            !RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            && !RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        )
        {
            return;
        }

        var fd = open(directory, O_RDONLY);
        if (fd < 0)
        {
            return; // best-effort
        }

        try
        {
            fsync(fd);
        }
        finally
        {
            close(fd);
        }
    }

    private const int O_RDONLY = 0;

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int fsync(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int syncfs(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern void sync();

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);
}
