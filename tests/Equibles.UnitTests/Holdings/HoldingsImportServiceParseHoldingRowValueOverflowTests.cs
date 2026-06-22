using System.Reflection;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// ParseHoldingRow derives a holding's stored value as (long)(shares × closePrice).
/// SSHPRNAMT is filer-controlled free text; ParseLong accepts any value up to
/// long.MaxValue, and a corrupt/fat-fingered share count is exactly why
/// Corrupt13FShareCountRepairer exists. Every other field parser in this class
/// degrades malformed input gracefully (ParseLong → 0, ClampLength clamps,
/// Filing13DGXmlParser range-checks the same decimal→long cast). So one oversized
/// row must not crash the whole filing's import: casting a decimal product that
/// exceeds long.MaxValue throws OverflowException. Oracle derived from the
/// contract (graceful per-field degradation), not the body.
/// </summary>
public class HoldingsImportServiceParseHoldingRowValueOverflowTests
{
    [Fact]
    public void ParseHoldingRow_SharesTimesPriceExceedsInt64_DoesNotThrowOverflow()
    {
        var method = typeof(HoldingsImportService).GetMethod(
            "ParseHoldingRow",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var stockId = Guid.NewGuid();
        var reportDate = new DateOnly(2025, 3, 31);
        // long.MaxValue shares × a $2 close overflows Int64 (≈1.84e19 > 9.22e18).
        var row = new Dictionary<string, string>
        {
            ["SSHPRNAMT"] = long.MaxValue.ToString(),
            ["SSHPRNAMTTYPE"] = "SH",
        };
        var context = new ImportContext
        {
            CoverPages = new Dictionary<string, CoverPageRow>(),
            StockPrices = new Dictionary<(Guid, DateOnly), decimal>
            {
                [(stockId, reportDate)] = 2m,
            },
        };
        var args = new object[]
        {
            row,
            "0001234567-25-000001",
            "037833100",
            stockId,
            Guid.NewGuid(),
            new DateOnly(2025, 4, 15),
            reportDate,
            context,
        };

        var ex = Record.Exception(() => method!.Invoke(null, args));
        var threwOverflow = (ex as TargetInvocationException)?.InnerException is OverflowException;

        threwOverflow
            .Should()
            .BeFalse(
                "a parseable-but-oversized share count must degrade gracefully like every other field parser, not crash the filing import with an OverflowException"
            );
    }
}
