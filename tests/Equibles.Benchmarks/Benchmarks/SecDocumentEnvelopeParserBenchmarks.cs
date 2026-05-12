using System.Text;
using BenchmarkDotNet.Attributes;
using Equibles.Sec.BusinessLogic;

namespace Equibles.Benchmarks.Benchmarks;

/// <summary>
/// Per-filing scan cost for <see cref="SecDocumentEnvelopeParser.TryExtractPaperPdfFilename"/>.
/// The SEC document scraper falls back to this method whenever HTML normalization yields no
/// markdown — typical for <PAPER> 6-K/20-F submissions wrapping a uuencoded PDF. The implementation
/// iterates DOCUMENT blocks, IndexOf's the FILENAME tag, and Substring's each candidate; both the
/// found-quickly and full-scan paths are exercised here because production traffic hits both.
/// </summary>
[MemoryDiagnoser]
public class SecDocumentEnvelopeParserBenchmarks {
    private const int BlockCount = 8;
    private string _envelopeWithPdf;
    private string _envelopeWithoutPdf;

    [GlobalSetup]
    public void Setup() {
        // A realistic PAPER envelope: multiple DOCUMENT blocks, one of which is the PDF. The
        // parser scans block-by-block from the start, so placing the PDF near the end forces
        // every block to be examined — the cost we actually want to measure.
        _envelopeWithPdf = BuildEnvelope(includePdfAt: BlockCount - 1);

        // A same-shape envelope with no PDF — exercises the "no early exit" full-scan path,
        // which is what runs for HTML-only filings that happen to hit this fallback by mistake.
        _envelopeWithoutPdf = BuildEnvelope(includePdfAt: -1);
    }

    [Benchmark]
    public bool FindsPdfInPaperEnvelope() =>
        SecDocumentEnvelopeParser.TryExtractPaperPdfFilename(_envelopeWithPdf, out _);

    [Benchmark]
    public bool ScansEnvelopeWithNoPdf() =>
        SecDocumentEnvelopeParser.TryExtractPaperPdfFilename(_envelopeWithoutPdf, out _);

    private static string BuildEnvelope(int includePdfAt) {
        var sb = new StringBuilder();
        sb.Append("<SEC-DOCUMENT>0000320193-25-000001.txt : 20250101\n");
        for (var i = 0; i < BlockCount; i++) {
            var isPdf = i == includePdfAt;
            sb.Append("<DOCUMENT>\n");
            sb.Append("<TYPE>").Append(isPdf ? "PAPER" : "EX-99.1").Append('\n');
            sb.Append("<SEQUENCE>").Append(i + 1).Append('\n');
            sb.Append("<FILENAME>").Append(isPdf ? "filing.pdf" : $"exhibit-{i}.htm").Append('\n');
            sb.Append("<DESCRIPTION>Sample exhibit\n");
            sb.Append("<TEXT>\n");
            // A few hundred bytes of body content per block to keep IndexOf walking realistic
            // distances between tag markers — empty bodies underreport the true scan cost.
            for (var j = 0; j < 6; j++) {
                sb.Append("Lorem ipsum dolor sit amet, consectetur adipiscing elit. ")
                    .Append("Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.\n");
            }
            sb.Append("</TEXT>\n</DOCUMENT>\n");
        }
        return sb.ToString();
    }
}
