using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Corrupt13FShareCountRepairerPre2023VotingFallbackTests
{
    // Combination pin: pre-2023 thousands scaling applies to the dollar VALUE only — the
    // voting-authority total is a share count, so the no-price fallback must use it verbatim
    // (existing tests pin thousands×priced and voting×post-2023 separately, never together).
    [Fact]
    public void Repair_FilingBefore2023NoPriceVotingTotalPresent_UsesVotingTotalUnscaled()
    {
        var holding = new InstitutionalHolding
        {
            CommonStockId = Guid.NewGuid(),
            InstitutionalHolderId = Guid.NewGuid(),
            FilingDate = new DateOnly(2021, 8, 12),
            ReportDate = new DateOnly(2021, 6, 30),
            Shares = 6_702_883,
            Value = 0,
            ShareType = ShareType.Shares,
            VotingAuthSole = 6_751,
            ValuePending = true,
        };
        var row = new BufferedHoldingRow
        {
            Holding = holding,
            ManagerEntry = new HoldingManagerEntry { Shares = 6_702_883, Value = 0 },
            ReportedValue = 6_702_883,
        };
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
}
