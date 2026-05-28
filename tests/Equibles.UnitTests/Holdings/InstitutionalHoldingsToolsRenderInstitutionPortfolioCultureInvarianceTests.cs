using System.Globalization;
using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsRenderInstitutionPortfolioCultureInvarianceTests
{
    // Adversarial Lane A. RenderInstitutionPortfolio builds the per-holding
    // markdown row via:
    //   $"| {i + 1} | {ticker} | {name} | {h.Shares:N0} | {h.Value / 1_000_000m:N1} |"
    //
    // The `:N0` and `:N1` format specifiers resolve through the thread
    // CurrentCulture's NumberFormatInfo. Same bug class as
    // ShortDataTools.FormatSignedChange (sibling
    // ShortDataToolsFormatSignedChangeCultureInvarianceTests, fix #2444) and
    // HoldingsImportService.BuildHoldingKey (GH-2594). de-DE's NumberFormat
    // swaps `,` and `.` (thousands `.`, decimal `,`) — `1,234,567` becomes
    // `1.234.567` and `1,234.5` becomes `1.234,5` — forking the
    // LLM-consumed markdown by host locale.
    //
    // The contract (repo convention; cf. FactMarkdown.Value threading
    // InvariantCulture and the two sibling pins above): MCP output formatters
    // MUST render culture-invariantly so the same call returns the same
    // bytes regardless of host CurrentCulture.
    //
    // Test strategy mirrors the FormatSignedChange and BuildHoldingKey
    // culture-invariance siblings: capture CurrentCulture, switch to de-DE,
    // reflection-invoke the private static, restore in finally, compare
    // against the Invariant rendering. A failure manifests as the
    // thousand/decimal-separator swap.
    [Fact(
        Skip = "GH-2597 — RenderInstitutionPortfolio :N0/:N1 cells follow CurrentCulture (de-DE → 1.234.567 / 1.234,6 vs Invariant 1,234,567 / 1,234.6); MCP output forks by host locale"
    )]
    public void RenderInstitutionPortfolio_UnderNonInvariantCulture_RendersSharesAndValueCellsCultureInvariantly()
    {
        var method = typeof(InstitutionalHoldingsTools).GetMethod(
            "RenderInstitutionPortfolio",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var holder = new InstitutionalHolder { Name = "ACME Capital", Cik = "0001234567" };
        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var holdings = new List<InstitutionalHolding>
        {
            new()
            {
                CommonStock = stock,
                Shares = 1_234_567,
                Value = 1_234_567_890L,
            },
        };
        var targetDate = new DateOnly(2024, 12, 31);
        object[] args = [holder, targetDate, holdings];

        var original = CultureInfo.CurrentCulture;
        string invariantOutput;
        string deDeOutput;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            invariantOutput = (string)method.Invoke(null, args);

            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            deDeOutput = (string)method.Invoke(null, args);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        deDeOutput
            .Should()
            .Be(
                invariantOutput,
                "MCP markdown output is consumed by LLMs trained on en-US conventions; :N0 / :N1 without an explicit IFormatProvider follow CurrentCulture (de-DE → 1.234.567 / 1.234,6), forking the response by host locale — same bug class as the FormatSignedChange and BuildHoldingKey culture-invariance siblings"
            );
    }
}
