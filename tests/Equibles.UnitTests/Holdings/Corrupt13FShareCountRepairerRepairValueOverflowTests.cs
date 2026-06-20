using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Corrupt13FShareCountRepairer.Repair salvages filings whose sshPrnamt column duplicates
/// the dollar-value column, recovering the true share count as value ÷ price. It exists
/// precisely to handle corrupt/oversized filer input — so an extreme reported value is in
/// its expected domain, not an edge case. The recovery uses an unguarded
/// (long)Math.Round(reportedDollars / closePrice) cast: when the quotient exceeds Int64
/// (a large reported value over a sub-dollar close) the cast throws OverflowException,
/// aborting the import batch the repairer is meant to rescue. The contract is graceful
/// degradation (drop/skip the row, mirror the now-fixed ParseHoldingRow and sibling
/// range-checks), not a crash. Oracle derived from the contract, not the body.
/// </summary>
public class Corrupt13FShareCountRepairerRepairValueOverflowTests
{
    [Fact(Skip = "GH-3858 — Repair throws OverflowException on an oversized duplicated row")]
    public void Repair_DuplicatedRowValueOverPriceExceedsInt64_DoesNotThrowOverflow()
    {
        // Duplicated row (Shares == ReportedValue) with an oversized value and a $0.50
        // close: reportedDollars ÷ price ≈ 1.84e19 overflows Int64 at the (long) cast.
        var row = DuplicatedRow(value: long.MaxValue);
        var rows = new List<BufferedHoldingRow> { row };
        var prices = new Dictionary<(Guid, DateOnly), decimal>
        {
            [(row.Holding.CommonStockId, row.Holding.ReportDate)] = 0.50m,
        };

        var act = () => Corrupt13FShareCountRepairer.Repair(rows, prices);

        act.Should()
            .NotThrow<OverflowException>(
                "an oversized corrupt row is exactly what the repairer exists to handle; it must degrade gracefully, not crash the import batch with an OverflowException"
            );
    }

    private static BufferedHoldingRow DuplicatedRow(long value)
    {
        var holding = new InstitutionalHolding
        {
            CommonStockId = Guid.NewGuid(),
            InstitutionalHolderId = Guid.NewGuid(),
            FilingDate = new DateOnly(2026, 4, 15),
            ReportDate = new DateOnly(2026, 3, 31),
            Shares = value,
            Value = 0,
            ShareType = ShareType.Shares,
            ValuePending = true,
        };

        return new BufferedHoldingRow
        {
            Holding = holding,
            ManagerEntry = new HoldingManagerEntry { Shares = value, Value = 0 },
            ReportedValue = value,
        };
    }
}
