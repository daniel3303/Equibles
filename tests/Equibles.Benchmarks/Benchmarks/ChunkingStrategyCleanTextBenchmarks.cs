using System.Text;
using BenchmarkDotNet.Attributes;
using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.BusinessLogic.Tokenization;

namespace Equibles.Benchmarks.Benchmarks;

/// <summary>
/// Per-chunk text-cleanup cost for <see cref="ChunkingStrategy.CleanText"/>. The method is
/// called once per emitted chunk inside <see cref="ChunkingStrategy.SplitIntoChunks"/>, and
/// each call spins up a new <c>AngleSharp.Html.Parser.HtmlParser</c>, parses the chunk into
/// a DOM to strip residual tags, and runs a <c>Regex.Replace</c> over the extracted text.
/// On a multi-thousand-filing backlog the embedding pipeline produces tens of chunks per
/// document, so this benchmark surfaces a cost the end-to-end SplitIntoChunks benchmark
/// averages over — useful for spotting parser-allocation regressions that hide inside the
/// loop. The fixture is a realistic post-Markdown chunk: paragraph-shaped text mixed with
/// a handful of residual span/em/strong tags and currency runs that the SEC normalizer
/// pipeline does not always strip cleanly.
/// </summary>
[MemoryDiagnoser]
public class ChunkingStrategyCleanTextBenchmarks
{
    private readonly ChunkingStrategy _sut = new(new TokenCounter());
    private string _input;

    [GlobalSetup]
    public void Setup()
    {
        // ~4 KB of post-Markdown text with residual inline HTML — representative of the
        // worst-case shape CleanText sees in production (clean prose is the cheap path).
        var fragments = new[]
        {
            "The Company reported <strong>strong</strong> revenue growth during the fiscal quarter.",
            "Operating expenses increased <em>modestly</em> compared to the prior <span>year</span> period.",
            "Net income rose driven by higher product margins and <strong>lower</strong> interest costs.",
            "Management remains <em>cautiously optimistic</em> about the macroeconomic outlook.",
            "Capital expenditures were directed toward expansion of <span>production capacity</span>.",
            "Free cash flow generation supports the ongoing <strong>share repurchase</strong> program.",
        };
        var builder = new StringBuilder(4_096 + 128);
        var i = 0;
        while (builder.Length < 4_096)
        {
            builder.Append(fragments[i % fragments.Length]).Append("    \n  ");
            i++;
        }
        _input = builder.ToString(0, 4_096);
    }

    [Benchmark]
    public int CleanHtmlChunk() => _sut.CleanText(_input).Length;
}
