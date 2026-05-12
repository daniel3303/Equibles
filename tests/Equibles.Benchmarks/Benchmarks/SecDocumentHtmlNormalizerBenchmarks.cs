using System.Text;
using BenchmarkDotNet.Attributes;
using Equibles.Sec.BusinessLogic;

namespace Equibles.Benchmarks.Benchmarks;

/// <summary>
/// End-to-end normalization cost for a single SEC filing's HTML body. Every filing
/// downloaded by the SEC worker is normalized exactly once before chunking, so this
/// benchmark gates the steady-state throughput of the document pipeline. The fixture
/// is a synthetic 10-K-shaped SGML envelope (DOCUMENT/TYPE/FILENAME/TEXT) with enough
/// repeated sections to exercise the AngleSharp parse and every normalization step
/// (XBRL strip, table normalization, pagination removal, heading and list conversion,
/// currency consolidation) — not just the cheap "extract envelope" path.
/// </summary>
[MemoryDiagnoser]
public class SecDocumentHtmlNormalizerBenchmarks {
    private const int SectionCount = 40;
    private readonly SecDocumentHtmlNormalizer _sut = new();
    private string _input;

    [GlobalSetup]
    public void Setup() {
        // Build a SEC SGML envelope wrapping a body with ~40 sections — headings,
        // paragraphs, tables, lists, and currency runs. The body intentionally mixes
        // every shape the normalizer's pipeline rewrites; a body of pure paragraphs
        // would skip most steps and underreport the real cost.
        var body = new StringBuilder();
        body.Append("<html><body>");
        for (var i = 0; i < SectionCount; i++) {
            body.Append($"<p style=\"font-weight:bold;font-size:14pt\">ITEM {i}. RISK FACTORS</p>");
            body.Append("<p>The Company faces various risks that could materially affect its business, ")
                .Append("financial condition, and results of operations. These risks include, but are not ")
                .Append("limited to, the following items described below.</p>");
            body.Append("<table><tr><td>Revenue</td><td>$1,234</td><td>$987</td></tr>")
                .Append("<tr><td>Cost</td><td>$456</td><td>$321</td></tr>")
                .Append("<tr><td>Profit</td><td>$778</td><td>$666</td></tr></table>");
            body.Append("<p>1. First key point about the risk.</p>");
            body.Append("<p>2. Second key point about the risk.</p>");
            body.Append("<p>3. Third key point about the risk.</p>");
            body.Append($"<p style=\"text-align:center\">{i + 1}</p>"); // page number
        }
        body.Append("</body></html>");

        var envelope = new StringBuilder();
        envelope.Append("<SEC-DOCUMENT>0000320193-25-000001.txt : 20250101\n");
        envelope.Append("<DOCUMENT>\n<TYPE>10-K\n<SEQUENCE>1\n<FILENAME>aapl-20250101.htm\n");
        envelope.Append("<DESCRIPTION>Annual report\n<TEXT>\n");
        envelope.Append(body);
        envelope.Append("\n</TEXT>\n</DOCUMENT>\n");
        // Add an exhibit that should be retained (EX-21 is under 100)
        envelope.Append("<DOCUMENT>\n<TYPE>EX-21.1\n<SEQUENCE>2\n<FILENAME>ex21.htm\n<TEXT>\n");
        envelope.Append("<html><body><p>List of subsidiaries.</p></body></html>");
        envelope.Append("\n</TEXT>\n</DOCUMENT>\n");

        _input = envelope.ToString();
    }

    [Benchmark]
    public string Normalize() => _sut.Normalize(_input);
}
