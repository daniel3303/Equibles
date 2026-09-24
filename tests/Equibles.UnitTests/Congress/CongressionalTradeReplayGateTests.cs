using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class CongressionalTradeReplayGateTests
{
    private const CongressionalFilingKind House =
        CongressionalFilingKind.HousePeriodicTransactionReport;
    private const CongressionalFilingKind Senate =
        CongressionalFilingKind.SenatePeriodicTransactionReport;

    [Theory]
    [InlineData(House, true, false, 4)]
    [InlineData(House, true, true, 4)]
    [InlineData(House, false, true, 4)]
    [InlineData(House, false, false, 7)]
    [InlineData(Senate, true, false, 4)]
    [InlineData(Senate, false, true, 4)]
    [InlineData(Senate, false, false, 6)]
    public void SelectTradeParserVersion_EvidenceAndTickerScope_ActivatesExpectedVersion(
        CongressionalFilingKind kind,
        bool evidenceBackfillPending,
        bool tickerScopeRestricted,
        int expectedVersion
    )
    {
        CongressionalTradeSyncService
            .SelectTradeParserVersion(kind, evidenceBackfillPending, tickerScopeRestricted)
            .Should()
            .Be(expectedVersion);
    }

    [Fact]
    public void DescribePartialFiling_NamesTheFilingAndBothCounts()
    {
        var message = CongressionalTradeSyncService.DescribePartialFiling(
            House,
            new ProcessedFiling("20024680", new DateOnly(2024, 3, 5), 3, RejectedRowCount: 2),
            7
        );

        message.Should().Contain("20024680");
        message.Should().Contain("2024-03-05");
        message.Should().Contain("2 transaction rows could not be parsed");
        message.Should().Contain("(3 stored)");
        message.Should().Contain("parser version 7");
    }

    [Fact]
    public void DescribeTickerConflict_NamesTheSourceRowAndBothTickers()
    {
        var message = CongressionalTradeSyncService.DescribeTickerConflict(
            new CongressionalTradeSyncService.TradeTickerConflict(
                House,
                "20024680",
                4,
                "IRA",
                "SCHW"
            ),
            7
        );

        message.Should().Contain("20024680 row 4");
        message.Should().Contain("reads ticker SCHW");
        message.Should().Contain("filed IRA");
    }
}
