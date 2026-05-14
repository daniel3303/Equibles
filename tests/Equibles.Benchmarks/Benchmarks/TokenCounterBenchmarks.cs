using System.Text;
using BenchmarkDotNet.Attributes;
using Equibles.Sec.BusinessLogic.Tokenization;

namespace Equibles.Benchmarks.Benchmarks;

/// <summary>
/// Throughput and allocation profile for <see cref="TokenCounter.CountTokens"/>, the inner
/// measurement used during document chunking before embedding. Every chunked SEC filing
/// runs through tiktoken's o200k_base encoder once per chunk to size the window, so any
/// regression in tokenizer setup or per-call allocations multiplies across the corpus.
/// The benchmark holds the encoder warm (singleton in production) and varies input length
/// so we can watch both fixed overhead and per-token scaling.
/// </summary>
[MemoryDiagnoser]
public class TokenCounterBenchmarks
{
    private readonly TokenCounter _sut = new();
    private string _text;

    // Spans typical SEC chunk shapes:
    //   ~256 chars  → a paragraph or table cell
    //   ~2 048 chars → a single 1024-token chunk (the production ChunkingStrategy size)
    //   ~16 384 chars → a full short item (~Item 1A risk paragraph)
    [Params(256, 2048, 16384)]
    public int CharCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Build a deterministic, vocabulary-realistic input — random ASCII bytes
        // tokenise pathologically (one token per char), and a constant repeated word
        // tokenises sub-realistically (encoder collapses runs). Cycling through a
        // small bag of plain English words approximates real prose density.
        var bag = new[]
        {
            "the",
            "company",
            "reported",
            "revenue",
            "growth",
            "during",
            "fiscal",
            "quarter",
            "ended",
            "December",
            "compared",
            "prior",
            "year",
            "period",
            "increased",
            "operating",
            "expenses",
            "decreased",
            "net",
            "income",
        };
        var builder = new StringBuilder(CharCount + 16);
        var i = 0;
        while (builder.Length < CharCount)
        {
            builder.Append(bag[i % bag.Length]).Append(' ');
            i++;
        }
        _text = builder.ToString(0, CharCount);
    }

    [Benchmark]
    public int CountTokens() => _sut.CountTokens(_text);
}
