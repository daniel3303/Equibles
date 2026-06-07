using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Corrupt13FShareCountRepairerIsSuspectJointBoundaryTests
{
    // Joint boundary of both detection thresholds: exactly MinimumComparableRows (5) and
    // exactly the 80% fraction (4 of 5 duplicated) — the doc says "at least 80%", so this
    // smallest qualifying filing must flag. Existing tests pin only strictly-inside points.
    [Fact]
    public void IsSuspect_FourOfFiveComparableRowsDuplicated_True()
    {
        var duplicated = Enumerable
            .Range(1, 4)
            .Select(i => Row(shares: i * 1_000, reportedValue: i * 1_000));
        var healthy = new[] { Row(shares: 500, reportedValue: 125_000) };
        var rows = duplicated.Concat(healthy).ToList();

        Corrupt13FShareCountRepairer.IsSuspect(rows).Should().BeTrue();
    }

    private static BufferedHoldingRow Row(long shares, long reportedValue) =>
        new()
        {
            Holding = new InstitutionalHolding
            {
                CommonStockId = Guid.NewGuid(),
                InstitutionalHolderId = Guid.NewGuid(),
                FilingDate = new DateOnly(2026, 4, 15),
                ReportDate = new DateOnly(2026, 3, 31),
                Shares = shares,
                Value = 0,
                ShareType = ShareType.Shares,
                ValuePending = true,
            },
            ManagerEntry = new HoldingManagerEntry { Shares = shares, Value = 0 },
            ReportedValue = reportedValue,
        };
}
