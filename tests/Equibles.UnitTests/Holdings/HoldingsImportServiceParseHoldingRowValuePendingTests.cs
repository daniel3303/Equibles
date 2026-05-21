using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceParseHoldingRowValuePendingTests
{
    // ParseHoldingRow's load-bearing valuation contract: when the
    // (commonStockId, reportDate) pair is absent from context.StockPrices the
    // parsed InstitutionalHolding must carry ValuePending = true AND Value = 0.
    // The two flags are paired by design — ValuePending signals the importer to
    // backfill once Yahoo prices arrive, and Value = 0 keeps the row from
    // counting toward portfolio totals until then. A refactor that flipped the
    // condition to `var valuePending = hasPrice` (or computed Value from the
    // unparsed price slot) would compile, pass every priced-row test, and
    // silently leave whole quarters of unpriced holdings either marked as
    // already-valued at $0 (no backfill ever happens) or correctly valued but
    // never marked pending (same hole, opposite symptom).
    [Fact]
    public void ParseHoldingRow_NoPriceForStockReportDate_MarksValuePendingTrueAndValueZero()
    {
        var serviceType = typeof(HoldingsImportService);
        var method = serviceType.GetMethod(
            "ParseHoldingRow",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var commonStockId = Guid.NewGuid();
        var holderId = Guid.NewGuid();
        var filingDate = new DateOnly(2024, 11, 15);
        var reportDate = new DateOnly(2024, 9, 30);
        var accession = "0000950123-24-006477";

        var row = new Dictionary<string, string>
        {
            ["SSHPRNAMTTYPE"] = "SH",
            ["PUTCALL"] = "",
            ["SSHPRNAMT"] = "100",
            ["VOTING_AUTH_SOLE"] = "100",
            ["VOTING_AUTH_SHARED"] = "0",
            ["VOTING_AUTH_NONE"] = "0",
            ["OTHERMANAGER"] = "",
            ["INVESTMENTDISCRETION"] = "SOLE",
            ["TITLEOFCLASS"] = "COM",
        };

        var context = new ImportContext
        {
            CoverPages = new Dictionary<string, CoverPageRow>(),
            StockPrices = new Dictionary<(Guid, DateOnly), decimal>(),
            OtherManagers = new Dictionary<string, Dictionary<int, string>>(),
        };

        var result = method.Invoke(
            null,
            [row, accession, "037833100", commonStockId, holderId, filingDate, reportDate, context]
        );

        var holding = (InstitutionalHolding)result.GetType().GetField("Item1").GetValue(result);
        var valuePending = (bool)result.GetType().GetField("Item3").GetValue(result);

        valuePending.Should().BeTrue();
        holding.ValuePending.Should().BeTrue();
        holding.Value.Should().Be(0L);
    }
}
