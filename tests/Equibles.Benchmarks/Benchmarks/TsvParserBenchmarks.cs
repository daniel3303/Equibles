using System.IO.Compression;
using System.Text;
using BenchmarkDotNet.Attributes;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.Benchmarks.Benchmarks;

/// <summary>
/// Per-row allocation cost for <see cref="TsvParser"/>, the hot inner loop of the
/// 13F import pipeline. Every SUBMISSION.tsv / INFOTABLE.tsv row produces a new
/// <see cref="Dictionary{TKey,TValue}"/> and trimmed string values — a quarterly
/// 13F data set can stream millions of rows through this method, so any change in
/// allocation shape shows up directly in worker memory pressure.
/// </summary>
[MemoryDiagnoser]
public class TsvParserBenchmarks {
    // Modeled on a realistic INFOTABLE row from SEC 13F data sets — 11 tab-separated
    // columns, mostly short fields plus one longer issuer name. The row count is
    // chosen to keep iteration time in the BDN sweet spot (tens of microseconds to
    // a few milliseconds) while still being representative of bulk parsing work.
    private const int RowCount = 1000;
    private static readonly string[] Headers = [
        "ACCESSION_NUMBER", "INFOTABLE_SK", "NAMEOFISSUER", "TITLEOFCLASS",
        "CUSIP", "FIGI", "VALUE", "SSHPRNAMT", "SSHPRNAMTTYPE", "PUTCALL", "INVESTMENTDISCRETION"
    ];
    private static readonly string[] SampleValues = [
        "0001234567-25-000123", "987654", "ACME CORPORATION COMMON STOCK", "COM",
        "00770F104", "BBG000B9XRY4", "1234567", "10000", "SH", "", "SOLE"
    ];

    private byte[] _zipBytes;
    private readonly TsvParser _sut = new();

    [GlobalSetup]
    public void Setup() {
        var tsv = new StringBuilder();
        tsv.AppendLine(string.Join('\t', Headers));
        for (var i = 0; i < RowCount; i++) {
            tsv.AppendLine(string.Join('\t', SampleValues));
        }

        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true)) {
            var entry = archive.CreateEntry("INFOTABLE.tsv");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            writer.Write(tsv.ToString());
        }

        _zipBytes = memoryStream.ToArray();
    }

    [Benchmark]
    public async Task<int> ParseInfoTableRows() {
        using var archive = new ZipArchive(new MemoryStream(_zipBytes), ZipArchiveMode.Read);
        var entry = archive.GetEntry("INFOTABLE.tsv")!;

        var count = 0;
        await foreach (var row in _sut.ParseEntry(entry)) {
            count++;
        }
        return count;
    }
}
