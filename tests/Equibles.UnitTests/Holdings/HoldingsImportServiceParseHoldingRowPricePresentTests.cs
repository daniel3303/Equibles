using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceParseHoldingRowPricePresentTests
{
    // Sibling pin to ParseHoldingRow_NoPriceForStockReportDate_MarksValuePendingTrueAndValueZero.
    // That pin covers the no-price arm: Value = 0, ValuePending = true. This pin
    // covers the priced arm: Value = (long)(shares × closePrice), ValuePending = false.
    //
    // The risk this catches and the sibling does NOT: a refactor that casts
    // closePrice (decimal) to long BEFORE multiplying — e.g. swapping
    // `(long)(shares * closePrice)` for `(long)closePrice * shares` —
    // compiles, passes the no-price pin (which never enters this branch),
    // and silently rounds every per-share price to whole dollars. A $12.50
    // closing price on 1,000 shares would persist as $12,000 instead of
    // $12,500: ~4% under-valuation propagated through every priced-quarter
    // import and into AUM, % of portfolio, top-N concentration. Pick a
    // non-integer per-share price so the truncate-before-multiply regression
    // is observably wrong.
    [Fact]
    public void ParseHoldingRow_PricePresentForStockReportDate_ComputesValueFromSharesTimesPriceAndClearsPending()
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
            ["SSHPRNAMT"] = "1000",
            ["VOTING_AUTH_SOLE"] = "1000",
            ["VOTING_AUTH_SHARED"] = "0",
            ["VOTING_AUTH_NONE"] = "0",
            ["OTHERMANAGER"] = "",
            ["INVESTMENTDISCRETION"] = "SOLE",
            ["TITLEOFCLASS"] = "COM",
        };

        var context = new ImportContext
        {
            CoverPages = new Dictionary<string, CoverPageRow>(),
            StockPrices = new Dictionary<(Guid, DateOnly), decimal>
            {
                [(commonStockId, reportDate)] = 12.50m,
            },
            OtherManagers = new Dictionary<string, Dictionary<int, string>>(),
        };

        var result = method.Invoke(
            null,
            [row, accession, "037833100", commonStockId, holderId, filingDate, reportDate, context]
        );

        var holding = (InstitutionalHolding)result.GetType().GetField("Item1").GetValue(result);
        var valuePending = (bool)result.GetType().GetField("Item3").GetValue(result);

        valuePending.Should().BeFalse();
        holding.ValuePending.Should().BeFalse();
        holding.Value.Should().Be(12_500L);
    }
}
