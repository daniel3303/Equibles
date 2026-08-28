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
            new FundSeriesRepository(_dbContext),
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
    public async Task GetFundNcenReports_StockNotFound_ReturnsNotFoundMessage()
    {
        var result = await _tools.GetFundNcenReports("ZZZZ");

        result.Should().Contain("ZZZZ");
    }

    [Fact]
    public async Task GetFundNcenReports_NoFilings_ReturnsEmptyMessage()
    {
        SeedStock();

        var result = await _tools.GetFundNcenReports("MXF");

        result.Should().Contain("No Form N-CEN annual reports found for Mexico Fund Inc (MXF).");
        result.Should().Contain("coverage result, not evidence that the fund has no N-CEN filing");
    }

    [Fact]
    public async Task GetFundNcenReports_WithFilings_RendersTableNewestFirstWithProviders()
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

        var result = await _tools.GetFundNcenReports("MXF");

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
    public async Task GetFundNcenReports_RespectsMaxResults()
    {
        var stock = SeedStock();
        for (var i = 0; i < 5; i++)
        {
            _dbContext
                .Set<NCenFiling>()
                .Add(MakeFiling(stock.Id, $"acc-{i}", new DateOnly(2021, 1, 1).AddYears(i)));
        }
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundNcenReports("MXF", maxResults: 2);

        result.Should().Contain("showing 2 most recent");
    }

    [Fact]
    public async Task GetFundNcenReports_GlossesRegistrationTypeCode()
    {
        var stock = SeedStock();
        _dbContext.Set<NCenFiling>().Add(MakeFiling(stock.Id, "acc", new DateOnly(2025, 1, 15)));
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundNcenReports("MXF");

        result.Should().Contain("N-2 (closed-end fund)");
    }

    [Fact]
    public async Task GetFundNcenReports_FlattensAndEscapesFiledTableCodes()
    {
        var stock = SeedStock();
        var filing = MakeFiling(stock.Id, "acc", new DateOnly(2025, 1, 15));
        filing.InvestmentCompanyType = "N|2\n# TYPE";
        filing.InvestmentCompanyFileNumber = "811|02409\n# FILE";
        _dbContext.Add(filing);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundNcenReports("MXF");

        result.Should().Contain("N\\|2 # TYPE");
        result.Should().Contain("811\\|02409 # FILE");
        result.Should().NotContain("\n# TYPE");
        result.Should().NotContain("\n# FILE");
    }

    [Fact]
    public async Task GetFundNcenReports_SeriesTicker_ResolvesBeforeReportingRegistrantCoverageGap()
    {
        _dbContext.Add(
            new FundSeries
            {
                LatestNportFilingId = Guid.NewGuid(),
                IdentityKey = "rc:1100663:S000004310",
                Slug = "ishares-core-sp-500-etf-s000004310",
                RegistrantCik = "1100663",
                SeriesId = "S000004310",
                SeriesName = "ISHARES CORE S&P 500 ETF",
                RegistrantName = "ISHARES TRUST",
                Ticker = "IVV",
                LatestReportPeriodDate = new DateOnly(2026, 3, 31),
                LatestFilingDate = new DateOnly(2026, 5, 15),
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundNcenReports("ivv");

        result.Should().Contain("resolves to ISHARES CORE S&P 500 ETF (IVV)");
        result.Should().Contain("coverage result, not an identifier-resolution failure");
    }

    [Fact]
    public async Task GetFundNcenReports_VerifiedAliasWinsOverConflictingTrackedStockTicker()
    {
        var conflictingStock = SeedStock("VOO");
        _dbContext.Add(MakeFiling(conflictingStock.Id, "wrong-series", new DateOnly(2026, 5, 16)));
        _dbContext.Add(
            new FundSeries
            {
                LatestNportFilingId = Guid.NewGuid(),
                IdentityKey = "rc:0000102909:S000002839",
                Slug = "vanguard-500-index-fund-s000002839",
                RegistrantCik = "0000102909",
                SeriesId = "S000002839",
                SeriesName = "VANGUARD 500 INDEX FUND",
                RegistrantName = "VANGUARD INDEX FUNDS",
                LatestReportPeriodDate = new DateOnly(2026, 3, 31),
                LatestFilingDate = new DateOnly(2026, 5, 15),
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundNcenReports("VOO");

        result.Should().Contain("resolves to VANGUARD 500 INDEX FUND");
        result.Should().Contain("coverage result, not an identifier-resolution failure");
        result.Should().NotContain("811-02409");
    }

    [Fact]
    public async Task GetFundNcenReports_CoverageMessageFlattensFiledMarkdownBoundaries()
    {
        _dbContext.Add(
            new FundSeries
            {
                LatestNportFilingId = Guid.NewGuid(),
                IdentityKey = "rc:1100663:S000004310",
                Slug = "unsafe-fund-s000004310",
                RegistrantCik = "1100663",
                SeriesId = "S000004310",
                SeriesName = "ISHARES\n# SYNTHETIC | FUND",
                RegistrantName = "ISHARES\r\nTRUST | EXTRA",
                LatestReportPeriodDate = new DateOnly(2026, 3, 31),
                LatestFilingDate = new DateOnly(2026, 5, 15),
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundNcenReports("unsafe-fund-s000004310");

        result.Should().Contain("ISHARES # SYNTHETIC \\| FUND");
        result.Should().Contain("ISHARES  TRUST \\| EXTRA");
        result.Should().NotContain("\n# SYNTHETIC");
    }

    [Fact]
    public async Task GetFundNcenReports_UnknownIdentifierStatesDirectoryCoverageLimits()
    {
        var result = await _tools.GetFundNcenReports("UNKNOWN");

        result.Should().Contain("fixed-income-only series");
        result.Should().Contain("coverage result, not evidence that the fund does not exist");
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
