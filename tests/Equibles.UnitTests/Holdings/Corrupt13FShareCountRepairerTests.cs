using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

// Pins the detection + repair of filings whose share-count column duplicates
// the value column (issue: some filers' software writes the position's dollar
// value into sshPrnamt — e.g. DIAMANT ASSET MANAGEMENT 0001731124-26-000002,
// J. Stern & Co. 0002011335-26-000002). Ingesting those counts verbatim and
// deriving Value = shares × price inflated portfolios ~1000×, corrupting the
// AUM-movers and top-by-AUM rankings.
public class Corrupt13FShareCountRepairerTests
{
    private static readonly DateOnly PostRuleFilingDate = new(2026, 4, 15);
    private static readonly DateOnly ReportDate = new(2026, 3, 31);

    [Fact]
    public void IsSuspect_EveryShareRowDuplicatesValue_True()
    {
        var rows = Enumerable
            .Range(1, 6)
            .Select(i => Row(shares: i * 1000, reportedValue: i * 1000))
            .ToList();

        Corrupt13FShareCountRepairer.IsSuspect(rows).Should().BeTrue();
    }

    [Fact]
    public void IsSuspect_FewerThanFiveComparableRows_False()
    {
        // Four rows all duplicated: below the minimum sample, a small filing
        // must never be flagged on coincidence (e.g. stocks at exactly $1.00).
        var rows = Enumerable
            .Range(1, 4)
            .Select(i => Row(shares: i * 1000, reportedValue: i * 1000))
            .ToList();

        Corrupt13FShareCountRepairer.IsSuspect(rows).Should().BeFalse();
    }

    [Fact]
    public void IsSuspect_DuplicationBelowEightyPercent_False()
    {
        // 7 of 10 comparable rows duplicated (70%) — under the threshold.
        var duplicated = Enumerable
            .Range(1, 7)
            .Select(i => Row(shares: i * 1000, reportedValue: i * 1000));
        var healthy = Enumerable
            .Range(1, 3)
            .Select(i => Row(shares: i * 100, reportedValue: i * 25_000));
        var rows = duplicated.Concat(healthy).ToList();

        Corrupt13FShareCountRepairer.IsSuspect(rows).Should().BeFalse();
    }

    [Fact]
    public void IsSuspect_PrincipalRowsEqualValue_NotCounted()
    {
        // A par bond legitimately reports principal == value, so PRN rows must
        // not contribute to the duplicated-column signal.
        var rows = Enumerable
            .Range(1, 6)
            .Select(i =>
                Row(
                    shares: i * 1_000_000,
                    reportedValue: i * 1_000_000,
                    shareType: ShareType.Principal
                )
            )
            .ToList();

        Corrupt13FShareCountRepairer.IsSuspect(rows).Should().BeFalse();
    }

    [Fact]
    public void Repair_PricedDuplicatedRow_RecoversSharesFromValueDividedByPrice()
    {
        // DIAMANT-shaped row: sshPrnamt carries the dollar value. With the
        // quarter's closing price the true count is value ÷ price.
        var row = Row(shares: 2_500_000, reportedValue: 2_500_000);
        var rows = new List<BufferedHoldingRow> { row };
        var prices = Prices(row, 250.00m);

        var outcome = Corrupt13FShareCountRepairer.Repair(rows, prices);

        outcome.Should().Be(new Corrupt13FRepairOutcome(RepairedRows: 1, DroppedRows: 0));
        row.Holding.Shares.Should().Be(10_000L);
        row.Holding.Value.Should().Be(2_500_000L);
        row.Holding.ValuePending.Should().BeFalse();
        row.ManagerEntry.Shares.Should().Be(10_000L);
        row.ManagerEntry.Value.Should().Be(2_500_000L);
    }

    [Fact]
    public void Repair_NoPriceButVotingTotalPresent_UsesVotingTotalAndMarksValuePending()
    {
        // These filers report the true count in the voting-authority columns
        // (verified against EDGAR for both reference filings). Without a price
        // the count is recovered from there and the value left for the
        // pending-price retry path.
        var row = Row(shares: 6_702_883, reportedValue: 6_702_883, votingSole: 6_751);
        var rows = new List<BufferedHoldingRow> { row };

        var outcome = Corrupt13FShareCountRepairer.Repair(
            rows,
            new Dictionary<(Guid, DateOnly), decimal>()
        );

        outcome.Should().Be(new Corrupt13FRepairOutcome(RepairedRows: 1, DroppedRows: 0));
        row.Holding.Shares.Should().Be(6_751L);
        row.Holding.Value.Should().Be(0L);
        row.Holding.ValuePending.Should().BeTrue();
    }

    [Fact]
    public void Repair_NoPriceAndNoVotingTotal_DropsRow()
    {
        var row = Row(shares: 1_000_000, reportedValue: 1_000_000);
        var rows = new List<BufferedHoldingRow> { row };

        var outcome = Corrupt13FShareCountRepairer.Repair(
            rows,
            new Dictionary<(Guid, DateOnly), decimal>()
        );

        outcome.Should().Be(new Corrupt13FRepairOutcome(RepairedRows: 0, DroppedRows: 1));
        rows.Should().BeEmpty();
    }

    [Fact]
    public void Repair_FilingBefore2023_TreatsReportedValueAsThousands()
    {
        // Pre-modernization filings (before 2023-01-03) report value in
        // thousands of dollars; the recovered count must scale accordingly.
        var row = Row(
            shares: 1_000,
            reportedValue: 1_000,
            filingDate: new DateOnly(2021, 8, 12),
            reportDate: new DateOnly(2021, 6, 30)
        );
        var rows = new List<BufferedHoldingRow> { row };
        var prices = Prices(row, 100.00m);

        Corrupt13FShareCountRepairer.Repair(rows, prices);

        // $1,000 thousands = $1,000,000 ÷ $100 = 10,000 shares.
        row.Holding.Shares.Should().Be(10_000L);
        row.Holding.Value.Should().Be(1_000_000L);
    }

    [Fact]
    public void Repair_HealthyRowInSuspectFiling_LeftUntouched()
    {
        // A suspect filing can still contain rows where the share count is
        // genuine (e.g. 19 of Align Financial's 233 rows); only duplicated
        // rows may be rewritten.
        var healthy = Row(shares: 500, reportedValue: 125_000);
        var rows = new List<BufferedHoldingRow> { healthy };
        var prices = Prices(healthy, 250.00m);

        var outcome = Corrupt13FShareCountRepairer.Repair(rows, prices);

        outcome.Should().Be(new Corrupt13FRepairOutcome(RepairedRows: 0, DroppedRows: 0));
        healthy.Holding.Shares.Should().Be(500L);
    }

    private static BufferedHoldingRow Row(
        long shares,
        long reportedValue,
        ShareType shareType = ShareType.Shares,
        long votingSole = 0,
        DateOnly? filingDate = null,
        DateOnly? reportDate = null
    )
    {
        var holding = new InstitutionalHolding
        {
            CommonStockId = Guid.NewGuid(),
            InstitutionalHolderId = Guid.NewGuid(),
            FilingDate = filingDate ?? PostRuleFilingDate,
            ReportDate = reportDate ?? ReportDate,
            Shares = shares,
            Value = 0,
            ShareType = shareType,
            VotingAuthSole = votingSole,
            ValuePending = true,
        };

        return new BufferedHoldingRow
        {
            Holding = holding,
            ManagerEntry = new HoldingManagerEntry { Shares = shares, Value = 0 },
            ReportedValue = reportedValue,
        };
    }

    private static Dictionary<(Guid, DateOnly), decimal> Prices(
        BufferedHoldingRow row,
        decimal closePrice
    ) => new() { [(row.Holding.CommonStockId, row.Holding.ReportDate)] = closePrice };
}
