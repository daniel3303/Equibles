using System.Text;
using BenchmarkDotNet.Attributes;
using Equibles.Sec.BusinessLogic;

namespace Equibles.Benchmarks.Benchmarks;

/// <summary>
/// HTML-to-Markdown conversion cost for a normalized SEC filing body. After
/// <see cref="SecDocumentHtmlNormalizer"/> rewrites the envelope, every filing
/// passes through <see cref="SecDocumentHtmlToMarkdownConverter.Convert"/> once
/// to produce the canonical Markdown form used for chunking and embedding —
/// so this benchmark sits on the same per-document hot path as the normalizer.
/// The fixture matches the normalizer benchmark's section shape so cost
/// comparisons between the two stages stay apples-to-apples.
/// </summary>
[MemoryDiagnoser]
public class SecDocumentHtmlToMarkdownConverterBenchmarks
{
    private const int SectionCount = 40;
    private readonly SecDocumentHtmlToMarkdownConverter _sut = new();
    private string _input;

    [GlobalSetup]
    public void Setup()
    {
        // Post-normalization shape: plain HTML with headings, paragraphs, tables, and
        // ordered-list paragraphs. No envelope, no style attributes (the normalizer
        // already stripped those — the converter's own style-attribute regex pass is
        // a defensive belt-and-braces, not the common case).
        var html = new StringBuilder();
        html.Append("<body>");
        for (var i = 0; i < SectionCount; i++)
        {
            html.Append($"<h2>Item {i}. Risk Factors</h2>");
            html.Append(
                    "<p>The Company faces various risks that could materially affect its business, "
                )
                .Append(
                    "financial condition, and results of operations. These risks include, but are not "
                )
                .Append("limited to, the following items described below.</p>");
            html.Append("<table><thead><tr><th>Line</th><th>2025</th><th>2024</th></tr></thead>")
                .Append("<tbody>")
                .Append("<tr><td>Revenue</td><td>$1,234</td><td>$987</td></tr>")
                .Append("<tr><td>Cost</td><td>$456</td><td>$321</td></tr>")
                .Append("<tr><td>Profit</td><td>$778</td><td>$666</td></tr>")
                .Append("</tbody></table>");
            html.Append("<ol>")
                .Append("<li>First key point about the risk.</li>")
                .Append("<li>Second key point about the risk.</li>")
                .Append("<li>Third key point about the risk.</li>")
                .Append("</ol>");
        }
        html.Append("</body>");

        _input = html.ToString();
    }

    [Benchmark]
    public string Convert() => _sut.Convert(_input);
}
