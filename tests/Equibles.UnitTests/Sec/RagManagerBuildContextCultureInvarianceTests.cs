using System.Globalization;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;

namespace Equibles.UnitTests.Sec;

public class RagManagerBuildContextCultureInvarianceTests
{
    // Adversarial Lane A. RagManager.BuildContext renders the per-chunk excerpt
    // header via:
    //   $"**Excerpt {chunk.Index + 1} (line ~{chunk.StartLineNumber:N0}):**"
    // and the per-group document line via:
    //   $"**Document:** {group.Key.DocumentType} filed on {group.Key.ReportingDate}"
    //
    // The `:N0` specifier and the default DateOnly.ToString() both resolve
    // through the thread CurrentCulture. Same bug class as FormatSignedChange
    // (#2444), BuildHoldingKey (GH-2594), and RenderInstitutionPortfolio's
    // :N0/:N1 cells (GH-2597). de-DE swaps the thousands separator (`1,234`
    // → `1.234`) and renders DateOnly as `31.12.2024` instead of the
    // Invariant `12/31/2024`. The rendered string is fed straight into an
    // LLM context window — forking it by host locale breaks reproducibility
    // (the same chunks produce different prompt text on a German-locale host
    // vs the US-locale CI runner) and can confuse a model trained on en-US
    // conventions about whether the document filed on `12.31.2024` is in
    // December or what day-12 month 31 even means.
    //
    // The contract (repo convention; cf. FactMarkdown.Value, which explicitly
    // threads InvariantCulture for the sibling FinancialFacts MCP output,
    // and the three culture-invariance pins above): user-facing rendered
    // strings consumed downstream by LLMs/clients MUST render
    // culture-invariantly so the same input yields the same bytes
    // regardless of host CurrentCulture.
    //
    // Test strategy mirrors the FormatSignedChange / BuildHoldingKey /
    // RenderInstitutionPortfolio culture-invariance siblings: capture
    // CurrentCulture, render once under Invariant, render once under
    // de-DE, restore in finally, and assert byte-equality.
    [Fact]
    public async Task BuildContext_UnderNonInvariantCulture_RendersCultureInvariantly()
    {
        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var document = new Document
        {
            CommonStock = stock,
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2024, 12, 31),
        };
        var chunks = new List<Chunk>
        {
            new()
            {
                Index = 0,
                StartPosition = 100,
                StartLineNumber = 1234,
                Content = "some excerpt text",
                Document = document,
            },
        };

        var sut = new RagManager(
            hybridChunkSearcher: null,
            commonStockRepository: null,
            logger: null
        );

        var original = CultureInfo.CurrentCulture;
        string invariantOutput;
        string deDeOutput;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            invariantOutput = await sut.BuildContext(chunks);

            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            deDeOutput = await sut.BuildContext(chunks);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        deDeOutput
            .Should()
            .Be(
                invariantOutput,
                "RagManager.BuildContext output is fed into an LLM context window; :N0 on StartLineNumber and the default DateOnly.ToString() on ReportingDate follow CurrentCulture (de-DE → `1.234` and `31.12.2024` vs Invariant `1,234` and `12/31/2024`), forking the prompt by host locale — same bug class as the three culture-invariance siblings above"
            );
    }
}
