using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Sibling to GetConsensusHoldings_MinFundsFilter_ExcludesBelowThreshold (which
/// pins the filter dropping below-threshold rows but still leaving non-zero
/// rows). The empty-table fallback at line 1261 fires when minFunds is set
/// higher than the maximum consensus across any stock — every stock is
/// excluded. A refactor that dropped the empty-rows guard would render the
/// table header with no rows beneath, confusing the MCP consumer with a dead
/// table when their filter is just too strict. Pin the explicit threshold
/// message.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetConsensusHoldingsThresholdAboveAllTests
    : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetConsensusHoldingsThresholdAboveAllTests(
        ParadeDbFixture fixture
    )
        : base(fixture) { }

    [Fact]
    public async Task GetConsensusHoldings_MinFundsHigherThanAnyStocksConsensus_ReportsNoMatches()
    {
        var aapl = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var fundA = new InstitutionalHolder { Cik = "T_A", Name = "Threshold A" };
        var fundB = new InstitutionalHolder { Cik = "T_B", Name = "Threshold B" };
        DbContext.AddRange(aapl, fundA, fundB);
        var date = new DateOnly(2024, 12, 31);
        // AAPL is held by both — consensus = 2. minFunds = 3 must exclude it.
        DbContext.Add(MakeHolding(aapl, fundA, date, value: 1_000_000));
        DbContext.Add(MakeHolding(aapl, fundB, date, value: 1_500_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(verify),
            new InstitutionalHolderRepository(verify),
            new CommonStockRepository(verify),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        var output = await sut.GetConsensusHoldings("Threshold A, Threshold B", minFunds: 3);

        output.Should().Contain("No stocks meet the minFunds threshold");
        output.Should().NotContain("| # | Ticker |");
    }

    private static InstitutionalHolding MakeHolding(
        CommonStock stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long value
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = value / 100,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{holder.Cik}-{reportDate:yyyyMMdd}",
        };
}
