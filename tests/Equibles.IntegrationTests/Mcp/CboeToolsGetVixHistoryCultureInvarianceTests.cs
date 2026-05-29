using System.Globalization;
using Equibles.Cboe.Data.Models;
using Equibles.Cboe.Mcp.Tools;
using Equibles.Cboe.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class CboeToolsGetVixHistoryCultureInvarianceTests : ParadeDbMcpTestBase
{
    private CboeTools Sut() =>
        new(
            new CboePutCallRatioRepository(DbContext),
            new CboeVixDailyRepository(DbContext),
            ErrorManager,
            NullLogger<CboeTools>()
        );

    public CboeToolsGetVixHistoryCultureInvarianceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // GetVixHistory renders the OHLC cells with the culture-implicit :F2 specifier,
    // which honours the thread CurrentCulture. The established repo contract (the
    // dozens of InvariantCulture call sites across the MCP tools commenting "MCP
    // markdown must not fork the separators by host locale") is that the LLM-facing
    // markdown renders the same on every host. de-DE swaps the decimal separator
    // (18.55 → 18,55), forking the response — same bug class as the fixed Holdings
    // render methods (#2628).
    [Fact(Skip = "GH-2789 — GetVixHistory :F2 cells follow host CurrentCulture")]
    public async Task GetVixHistory_UnderNonInvariantCulture_RendersCloseCultureInvariantly()
    {
        DbContext
            .Set<CboeVixDaily>()
            .Add(
                new CboeVixDaily
                {
                    Date = new DateOnly(2026, 4, 1),
                    Open = 14.20m,
                    High = 19.30m,
                    Low = 13.80m,
                    Close = 18.55m,
                }
            );
        await DbContext.SaveChangesAsync();

        // Pin de-DE only for the rendering call; CurrentCulture flows through the
        // tool's await chain via ExecutionContext. Base class restores invariant.
        var previous = CultureInfo.CurrentCulture;
        string result;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            result = await Sut().GetVixHistory(startDate: "2026-03-01", endDate: "2026-04-30");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // A VIX close of 18.55 must render with an en-US decimal point on every host locale.
        result.Should().Contain("| 18.55 |");
    }
}
