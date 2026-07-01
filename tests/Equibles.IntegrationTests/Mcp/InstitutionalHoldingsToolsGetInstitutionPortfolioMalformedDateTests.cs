using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Adversarial cover for <c>GetInstitutionPortfolio</c>'s report-date resolution. The
/// <c>reportDate</c> parameter is documented as "defaults to latest available", so an unparseable
/// value must degrade to the latest available quarter rather than surface an internal error or an
/// empty result. The existing portfolio tests only exercise the implicit-latest (null) path, so
/// the malformed-input fallback branch is otherwise unexercised.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetInstitutionPortfolioMalformedDateTests
    : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetInstitutionPortfolioMalformedDateTests(
        ParadeDbFixture fixture
    )
        : base(fixture) { }

    [Fact]
    public async Task GetInstitutionPortfolio_MalformedReportDate_FallsBackToLatestQuarter()
    {
        var holder = new InstitutionalHolder { Cik = "1", Name = "Berkshire Hathaway Inc." };
        var apple = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var microsoft = new CommonStock
        {
            Ticker = "MSFT",
            Name = "Microsoft Corp.",
            Cik = "0000789019",
        };
        DbContext.Add(holder);
        DbContext.Add(apple);
        DbContext.Add(microsoft);

        var olderQuarter = new DateOnly(2024, 9, 30);
        var latestQuarter = new DateOnly(2024, 12, 31);
        // Only MSFT exists in the older quarter; only AAPL in the latest.
        DbContext.Add(MakeHolding(holder, microsoft, olderQuarter, shares: 500, value: 50_000_000));
        DbContext.Add(MakeHolding(holder, apple, latestQuarter, shares: 1_000, value: 100_000_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(verify),
            new InstitutionalHolderRepository(verify),
            new CommonStockRepository(verify),
            new StockSplitRepository(verify),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        var output = await sut.GetInstitutionPortfolio("Berkshire", reportDate: "not-a-date");

        // A garbage date must not error; it resolves to the latest quarter (2024-12-31, AAPL),
        // never the older quarter's MSFT-only holdings.
        output.Should().NotContain("An error occurred while executing");
        output.Should().Contain("as of 2024-12-31");
        output.Should().Contain("AAPL");
        output.Should().NotContain("MSFT");
    }

    private static InstitutionalHolding MakeHolding(
        InstitutionalHolder holder,
        CommonStock stock,
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
            AccessionNumber = $"acc-{stock.Ticker}",
        };
}
