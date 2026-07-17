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

/// <summary>
/// Adversarial cover for <c>GetFundOperations</c>'s service-provider section, which the helper
/// sources from the latest report only. When the newest filing reports no service providers, an
/// explicit "names no service providers" note must render — and an older filing's providers must
/// not leak in. Existing tests only seed providers on the newest filing, leaving both the
/// empty-on-newest note and the newest-filing selection unexercised.
/// </summary>
public class NCenFundOperationsToolNewestFilingProvidersTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly NCenTools _tools;

    public NCenFundOperationsToolNewestFilingProvidersTests()
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

    [Fact]
    public async Task GetFundOperations_NewestFilingHasNoProviders_EmitsNoteWithoutLeakingOlderProviders()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "MXF",
            Name = "Mexico Fund Inc",
            Cik = "0000065433",
        };
        _dbContext.Set<CommonStock>().Add(stock);

        // Older filing carries a provider; newest filing carries none.
        var older = MakeFiling(stock.Id, "older", new DateOnly(2023, 1, 5));
        older.ServiceProviders.Add(
            new NCenServiceProvider
            {
                ProviderType = NCenServiceProviderType.InvestmentAdviser,
                Name = "OLD ADVISER FIRM",
                Country = "US",
                IsAffiliated = false,
            }
        );
        _dbContext.Set<NCenFiling>().Add(older);
        _dbContext.Set<NCenFiling>().Add(MakeFiling(stock.Id, "newer", new DateOnly(2025, 1, 15)));
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundOperations("MXF");

        // The section reflects only the latest report, which has no providers — so an explicit
        // note renders instead of a silently missing section, and the older filing's provider
        // must not leak in.
        result.Should().Contain("names no service providers");
        result.Should().Contain("2025-01-15");
        result.Should().NotContain("Service providers reported");
        result.Should().NotContain("OLD ADVISER FIRM");
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
