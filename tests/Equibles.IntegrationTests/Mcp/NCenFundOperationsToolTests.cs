using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.IntegrationTests.Mcp;

public class NCenFundOperationsToolTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly NCenTools _tools;

    public NCenFundOperationsToolTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new SecTestModuleConfiguration()
        );
        _tools = new NCenTools(
            new NCenFilingRepository(_dbContext),
            new CommonStockRepository(_dbContext),
            errorManager: null,
            NullLogger<NCenTools>.Instance
        );
    }

    public void Dispose() => _dbContext.Dispose();

    private CommonStock SeedStock(string ticker = "MXF", string cik = "0000065433")
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = "Mexico Fund Inc",
            Cik = cik,
        };
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.SaveChanges();
        return stock;
    }

    [Fact]
    public async Task GetFundOperations_StockNotFound_ReturnsNotFoundMessage()
    {
        var result = await _tools.GetFundOperations("ZZZZ");

        result.Should().Contain("ZZZZ");
    }

    [Fact]
    public async Task GetFundOperations_NoFilings_ReturnsEmptyMessage()
    {
        SeedStock();

        var result = await _tools.GetFundOperations("MXF");

        result.Should().Contain("No Form N-CEN annual reports found for MXF.");
    }

    [Fact]
    public async Task GetFundOperations_WithFilings_RendersTableNewestFirstWithProviders()
    {
        var stock = SeedStock();
        _dbContext.Set<NCenFiling>().Add(MakeFiling(stock.Id, "older", new DateOnly(2023, 1, 5)));

        var newer = MakeFiling(stock.Id, "newer", new DateOnly(2025, 1, 15));
        newer.ServiceProviders.Add(
            new NCenServiceProvider
            {
                ProviderType = NCenServiceProviderType.InvestmentAdviser,
                Name = "IMPULSORA DEL FONDO MEXICO SC",
                Country = "MX",
                IsAffiliated = false,
            }
        );
        _dbContext.Set<NCenFiling>().Add(newer);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundOperations("MXF");

        result.Should().Contain("Mexico Fund Inc");
        result.Should().Contain("811-02409");
        result.Should().Contain("Investment Adviser");
        result.Should().Contain("IMPULSORA DEL FONDO MEXICO SC");
        // Newest filing renders before the older one.
        result
            .IndexOf("2025-01-15", StringComparison.Ordinal)
            .Should()
            .BeLessThan(result.IndexOf("2023-01-05", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetFundOperations_RespectsMaxResults()
    {
        var stock = SeedStock();
        for (var i = 0; i < 5; i++)
        {
            _dbContext
                .Set<NCenFiling>()
                .Add(MakeFiling(stock.Id, $"acc-{i}", new DateOnly(2021, 1, 1).AddYears(i)));
        }
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundOperations("MXF", maxResults: 2);

        result.Should().Contain("showing 2 most recent");
    }

    [Fact]
    public async Task GetFundOperations_GlossesRegistrationTypeCode()
    {
        var stock = SeedStock();
        _dbContext.Set<NCenFiling>().Add(MakeFiling(stock.Id, "acc", new DateOnly(2025, 1, 15)));
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundOperations("MXF");

        result.Should().Contain("N-2 (closed-end fund)");
    }

    private static NCenFiling MakeFiling(Guid stockId, string accession, DateOnly filingDate)
    {
        return new NCenFiling
        {
            CommonStockId = stockId,
            AccessionNumber = accession,
            FilingDate = filingDate,
            IsAmendment = false,
            RegistrantName = "MEXICO FUND INC",
            InvestmentCompanyType = "N-2",
            InvestmentCompanyFileNumber = "811-02409",
            RegistrantLei = "00000000000000238096",
            State = "US-MD",
            Country = "US",
            ReportEndingPeriod = filingDate.AddMonths(-2),
            IsReportPeriodLessThan12Months = false,
        };
    }
}
