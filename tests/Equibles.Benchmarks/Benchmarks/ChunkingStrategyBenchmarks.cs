using System.Text;
using BenchmarkDotNet.Attributes;
using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.BusinessLogic.Tokenization;

namespace Equibles.Benchmarks.Benchmarks;

/// <summary>
/// Token-window splitting cost for a normalized filing. <see cref="ChunkingStrategy.SplitIntoChunks"/>
/// is the third per-document stage in the embedding pipeline — after normalization and
/// Markdown conversion — and it pays an O(chunks) tokenizer Decode tax for every char-position
/// boundary it resolves, plus a LINQ enumerator chain. The fixture is sized to span multiple
/// 1024-token chunks with the 128-token overlap window, so the inner loop runs more than once.
/// </summary>
[MemoryDiagnoser]
public class ChunkingStrategyBenchmarks {
    private readonly ChunkingStrategy _sut = new(new TokenCounter());
    private string _input;

    [GlobalSetup]
    public void Setup() {
        // 8 192 chars of plain English text — roughly ~1 800 tokens with o200k_base,
        // forcing the chunker to produce ~2 chunks with the production 1024-token window
        // and a single overlap pass. Smaller inputs would skip the sentence-boundary
        // heuristic; much larger ones blow up benchmark wall-time without exercising
        // any new code path.
        var bag = new[] {
            "The Company reported strong revenue growth during the fiscal quarter.",
            "Operating expenses increased modestly compared to the prior year period.",
            "Net income rose driven by higher product margins and lower interest costs.",
            "Management remains cautiously optimistic about the macroeconomic outlook.",
            "Capital expenditures were directed toward expansion of production capacity.",
            "Free cash flow generation supports the ongoing share repurchase program.",
        };
        var builder = new StringBuilder(8_192 + 64);
        var i = 0;
        while (builder.Length < 8_192) {
            builder.Append(bag[i % bag.Length]).Append(' ');
            i++;
        }
        _input = builder.ToString(0, 8_192);
    }

    [Benchmark]
    public int SplitIntoChunks() => _sut.SplitIntoChunks(_input).Count;
}
