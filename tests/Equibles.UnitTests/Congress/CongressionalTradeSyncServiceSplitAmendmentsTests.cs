using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class CongressionalTradeSyncServiceSplitAmendmentsTests
{
    private static DisclosureTransaction Transaction(
        int row,
        CongressionalTradeFilingStatus status
    ) =>
        new()
        {
            MemberName = "Kevin Hern",
            SourceId = "20035196",
            FilingKind = CongressionalFilingKind.HousePeriodicTransactionReport,
            SourceRowIndex = row,
            FilingStatus = status,
        };

    private static CongressionalTrade Built(int row) =>
        new()
        {
            FilingKind = CongressionalFilingKind.HousePeriodicTransactionReport,
            SourceId = "20035196",
            SourceRowIndex = row,
            OwnerType = "JT",
            AssetName = $"Asset {row}",
            FiledTicker = $"T{row}",
        };

    [Fact]
    public void SplitAmendments_DeletedRow_BecomesARecordButNeverATrade()
    {
        var built = new List<CongressionalTrade> { Built(0), Built(1), Built(2) };

        var (trades, amendments) = CongressionalTradeSyncService.SplitAmendments(
            [
                Transaction(0, CongressionalTradeFilingStatus.New),
                Transaction(1, CongressionalTradeFilingStatus.Amended),
                Transaction(2, CongressionalTradeFilingStatus.Deleted),
            ],
            built
        );

        trades.Select(trade => trade.SourceRowIndex).Should().Equal(0, 1);
        amendments
            .Select(row => (row.SourceRowIndex, row.Status))
            .Should()
            .Equal(
                (1, CongressionalTradeFilingStatus.Amended),
                (2, CongressionalTradeFilingStatus.Deleted)
            );
        amendments[1].FiledTicker.Should().Be("T2");
        amendments
            .Should()
            .OnlyContain(row => row.Verdict == CongressionalTradeAmendmentVerdict.Pending);
    }
}
