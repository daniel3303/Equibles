using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class CongressionalTradeReplayGateTests
{
    [Theory]
    [InlineData(true, false, 4)]
    [InlineData(true, true, 4)]
    [InlineData(false, true, 4)]
    [InlineData(false, false, 6)]
    public void SelectTradeParserVersion_EvidenceAndTickerScope_ActivatesExpectedVersion(
        bool evidenceBackfillPending,
        bool tickerScopeRestricted,
        int expectedVersion
    )
    {
        CongressionalTradeSyncService
            .SelectTradeParserVersion(evidenceBackfillPending, tickerScopeRestricted)
            .Should()
            .Be(expectedVersion);
    }
}
