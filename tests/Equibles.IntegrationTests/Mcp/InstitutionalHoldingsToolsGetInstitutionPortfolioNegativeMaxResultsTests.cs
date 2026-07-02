using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetInstitutionPortfolioNegativeMaxResultsTests
    : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetInstitutionPortfolioNegativeMaxResultsTests(
        ParadeDbFixture fixture
    )
        : base(fixture) { }

    private InstitutionalHoldingsTools Sut() =>
        new(
            new InstitutionalHoldingRepository(DbContext),
            new InstitutionalHolderRepository(DbContext),
            new CommonStockRepository(DbContext),
            new StockSplitRepository(DbContext),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(DbContext),
                new StockSplitRepository(DbContext)
            ),
            ErrorManager,
            NullLogger<InstitutionalHoldingsTools>()
        );

    [Fact]
    public async Task GetInstitutionPortfolio_NegativeMaxResults_DoesNotSurfaceInternalError()
    {
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc",
            Cik = "0000320193",
        };
        var holder = new InstitutionalHolder
        {
            Cik = "0001067983",
            Name = "Berkshire Hathaway Inc",
            City = "Omaha",
            StateOrCountry = "NE",
        };
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<InstitutionalHolder>().Add(holder);
        await DbContext.SaveChangesAsync();

        DbContext
            .Set<InstitutionalHolding>()
            .Add(
                new InstitutionalHolding
                {
                    CommonStockId = stock.Id,
                    CommonStock = stock,
                    InstitutionalHolderId = holder.Id,
                    InstitutionalHolder = holder,
                    ReportDate = new DateOnly(2024, 3, 31),
                    FilingDate = new DateOnly(2024, 5, 15),
                    Shares = 10_000,
                    Value = 1_000_000,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                    AccessionNumber = "0000000000-24-000001",
                    TitleOfClass = "COM",
                    Cusip = "037833100",
                }
            );
        await DbContext.SaveChangesAsync();

        // Contract: maxResults is "Maximum number of holdings to return"; a negative
        // value is nonsensical input. It is forwarded unclamped into EF Core's
        // GetByHolder(...).Take(maxResults), so a negative cap reaches PostgreSQL as a
        // negative LIMIT, which the engine rejects. The tool must degrade gracefully
        // rather than leak the executor's internal-error sentinel to the caller.
        var result = await Sut().GetInstitutionPortfolio("Berkshire", maxResults: -1);

        result.Should().NotContain("An error occurred while executing");
    }
}
