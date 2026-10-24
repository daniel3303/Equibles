using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Holdings;

// The set form must return exactly what Get13FByHolderWithStock returns for each holder: that quarter's
// 13F rows only (no Schedule 13D/G stake dated on the quarter end), with the issuer loaded, and nothing
// from holders outside the set.
public class InstitutionalHoldingRepositoryGet13FByHoldersWithStockTests
{
    private static readonly DateOnly Quarter = new(2026, 6, 30);

    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new HoldingsModuleConfiguration(),
                new CorporateActionsModuleConfiguration(),
            }
        );
    }

    private static InstitutionalHolding Holding(
        EquityIssuer stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        FilingType filingType = FilingType.Form13F
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(30),
            ReportDate = reportDate,
            FilingType = filingType,
            Shares = 100,
            Value = 1_000,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{holder.Cik}-{reportDate:yyyyMMdd}-{filingType}",
        };

    [Fact]
    public async Task Get13FByHoldersWithStock_ReturnsEachRequestedHoldersQuarter13FRowsWithTheIssuer()
    {
        await using var db = NewDb();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "MSFT",
            Cik: "0000789019"
        );
        var first = new InstitutionalHolder { Cik = "1", Name = "First Capital" };
        var second = new InstitutionalHolder { Cik = "2", Name = "Second Partners" };
        var outside = new InstitutionalHolder { Cik = "3", Name = "Outside Fund" };
        db.AddRange(stock, first, second, outside);
        db.AddRange(
            Holding(stock, first, Quarter),
            Holding(stock, first, Quarter, FilingType.Schedule13G),
            Holding(stock, first, Quarter.AddMonths(-3)),
            Holding(stock, second, Quarter),
            Holding(stock, outside, Quarter)
        );
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rows = await new InstitutionalHoldingRepository(db)
            .Get13FByHoldersWithStock([first, second], Quarter)
            .ToListAsync();

        rows.Select(r => r.InstitutionalHolderId).Should().BeEquivalentTo([first.Id, second.Id]);
        rows.Should()
            .OnlyContain(r => r.FilingType == FilingType.Form13F && r.ReportDate == Quarter);
        rows.Should().OnlyContain(r => r.Issuer != null && r.Issuer.Ticker == "MSFT");
    }
}
