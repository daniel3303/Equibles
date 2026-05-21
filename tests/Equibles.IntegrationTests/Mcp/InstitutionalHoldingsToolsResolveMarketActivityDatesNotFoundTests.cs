using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Sibling pin to <see cref="InstitutionalHoldingsToolsResolveMarketActivityDatesNoPriorQuarterTests"/>.
/// Covers the OTHER error arm of <c>ResolveMarketActivityDates</c>: when the
/// user supplies a parseable <c>reportDate</c> that isn't in the available
/// dates, the helper must return an error that NAMES the missing date and
/// LISTS the available alternatives — never silently fall back to the latest
/// (which would dump someone else's quarter under the user's requested date
/// header) or index-out-of-range. A regression removing the
/// <c>targetIndex &lt; 0</c> guard fails here.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsResolveMarketActivityDatesNotFoundTests : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsResolveMarketActivityDatesNotFoundTests(
        ParadeDbFixture fixture
    )
        : base(fixture) { }

    [Fact]
    public async Task GetMostHeldStocks_ReportDateNotInAvailableDates_ReturnsNotFoundErrorListingAvailable()
    {
        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);
        var aapl = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "C1",
        };
        var filer = new InstitutionalHolder { Cik = "H1", Name = "Sole Filer" };
        DbContext.AddRange(aapl, filer);
        DbContext.Add(MakeHolding(aapl, filer, prior, shares: 100, value: 100_000));
        DbContext.Add(MakeHolding(aapl, filer, current, shares: 100, value: 200_000));
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

        var output = await sut.GetMostHeldStocks(reportDate: "2099-12-31");

        output.Should().Contain("Report date 2099-12-31 not found");
        output.Should().Contain("Available dates:");
        output.Should().Contain("2024-12-31");
    }

    private static InstitutionalHolding MakeHolding(
        CommonStock stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long shares,
        long value
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{holder.Cik}-{stock.Ticker}-{reportDate:yyyyMMdd}",
        };
}
