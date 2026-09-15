using System.IO.Compression;

namespace Equibles.Integrations.Esma.Models;

// A verified zip held on disk; the single XML entry streams out without ever being fully in memory.
public sealed class FirdsDownload : IDisposable
{
    private readonly string _path;
    private ZipArchive _archive;

    public FirdsDownload(FirdsFile file, string path, long bytes)
    {
        File = file;
        _path = path;
        Bytes = bytes;
    }

    public FirdsFile File { get; }
    public long Bytes { get; }

    public Stream OpenXml()
    {
        _archive?.Dispose();
        _archive = ZipFile.OpenRead(_path);
        var entries = _archive
            .Entries.Where(entry => entry.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (entries.Count != 1)
            throw new InvalidDataException("FIRDS archive must contain exactly one XML file.");
        return entries[0].Open();
    }

    public void Dispose()
    {
        _archive?.Dispose();
        try
        {
            System.IO.File.Delete(_path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
