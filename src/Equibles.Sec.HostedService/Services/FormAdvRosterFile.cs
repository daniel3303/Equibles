using System.IO.Compression;
using System.Text;
using Equibles.Integrations.Sec.FormAdv;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Opens one downloaded Form ADV roster in any shape SEC has published it: a ZIP holding a CSV
/// (late 2025 onward), a ZIP holding an .xlsx workbook (2019 to mid-2023) or a bare .xlsx
/// workbook (mid-2023 to late 2025).
/// </summary>
internal static class FormAdvRosterFile
{
    /// <summary>
    /// The roster's records, the header first, or null when a ZIP holds neither a CSV nor a
    /// workbook. A workbook is buffered in memory because its reader needs to seek.
    /// </summary>
    internal static IEnumerable<List<string>> Open(Stream download, string url)
    {
        if (new Uri(url).AbsolutePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return XlsxRecordReader.Read(Buffer(download));

        var archive = new ZipArchive(download, ZipArchiveMode.Read);
        var csv = archive.Entries.FirstOrDefault(entry =>
            entry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
        );
        if (csv != null)
            return ReadCsv(archive, csv);
        var workbook = archive.Entries.FirstOrDefault(entry =>
            entry.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
        );
        if (workbook == null)
        {
            archive.Dispose();
            return null;
        }
        using (var entryStream = workbook.Open())
        {
            var buffered = Buffer(entryStream);
            archive.Dispose();
            return XlsxRecordReader.Read(buffered);
        }
    }

    private static IEnumerable<List<string>> ReadCsv(ZipArchive archive, ZipArchiveEntry entry)
    {
        using (archive)
        {
            using var entryStream = entry.Open();
            // The SEC publishes the CSV in Latin-1; reading it as UTF-8 would corrupt accented names.
            using var reader = new StreamReader(entryStream, Encoding.Latin1);
            foreach (var record in CsvRecordReader.Read(reader))
                yield return record;
        }
    }

    private static MemoryStream Buffer(Stream source)
    {
        var buffer = new MemoryStream();
        source.CopyTo(buffer);
        buffer.Position = 0;
        return buffer;
    }
}
