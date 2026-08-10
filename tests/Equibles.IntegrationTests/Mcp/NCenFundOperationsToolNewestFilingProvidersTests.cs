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
/// Adversarial cover for <c>GetFundOperations</c>'s service-provider sections. The latest-report
/// table must never borrow providers from an older report, while the history must preserve every
/// exact filed-name state — including omitted roles, punctuation differences, and hostile
/// Markdown text — without claiming that a heuristic identity match proves no change.
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
            new FundSeriesRepository(_dbContext),
            errorManager: null,
            NullLogger<NCenTools>.Instance
        );
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task GetFundOperations_NewestFilingHasNoProviders_SeparatesLatestSnapshotFromHistory()
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

        // The latest snapshot does not borrow an older provider, while the separately labelled
        // history preserves the older filed state and the newest omission.
        result.Should().Contain("names no service providers");
        result.Should().Contain("2025-01-15");
        result.Should().NotContain("Service providers reported");
        result.Should().Contain("Service-provider history");
        result.Should().Contain("2023-01-05: OLD ADVISER FIRM; 2025-01-15: not reported");
    }

    [Fact]
    public async Task GetFundOperations_HistoryPreservesOmissionsExactNamesAndMarkdownBoundaries()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "MXF",
            Name = "Mexico Fund Inc",
            Cik = "0000065433",
        };
        _dbContext.Set<CommonStock>().Add(stock);

        var older = MakeFiling(stock.Id, "older", new DateOnly(2023, 1, 5));
        older.ServiceProviders.Add(
            new NCenServiceProvider
            {
                ProviderType = NCenServiceProviderType.PublicAccountant,
                Name = "A-B\\|\nLLP",
                Country = "U\\|\nS",
            }
        );
        var newest = MakeFiling(stock.Id, "newest", new DateOnly(2025, 1, 15));
        newest.ServiceProviders.Add(
            new NCenServiceProvider
            {
                ProviderType = NCenServiceProviderType.PublicAccountant,
                Name = "AB\\|LLP",
                Country = "U\\|S",
            }
        );
        _dbContext
            .Set<NCenFiling>()
            .AddRange(older, MakeFiling(stock.Id, "middle", new DateOnly(2024, 1, 10)), newest);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundOperations("MXF");

        result.Should().Contain("| Independent Public Accountant | AB\\\\\\|LLP | U\\\\\\|S |");
        result
            .Should()
            .Contain(
                "2023-01-05: A-B\\\\\\| LLP; 2024-01-10: not reported; 2025-01-15: AB\\\\\\|LLP"
            );
        result.Should().NotContain("No service-provider changes");
    }

    [Fact]
    public async Task GetFundOperations_SameDayAmendmentWinsAndHistoryIsDeterministic()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "MXF",
            Name = "Mexico Fund Inc",
            Cik = "0000065433",
        };
        _dbContext.Set<CommonStock>().Add(stock);

        var filed = new DateOnly(2025, 1, 15);
        var original = MakeFiling(stock.Id, "0000065433-25-000001", filed);
        original.CreationTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        original.ServiceProviders.Add(
            new NCenServiceProvider
            {
                ProviderType = NCenServiceProviderType.PublicAccountant,
                Name = "ORIGINAL AUDITOR LLP",
            }
        );
        var amendment = MakeFiling(stock.Id, "0000065433-25-000002", filed);
        amendment.IsAmendment = true;
        amendment.CreationTime = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        amendment.ServiceProviders.Add(
            new NCenServiceProvider
            {
                ProviderType = NCenServiceProviderType.PublicAccountant,
                Name = "AMENDED AUDITOR LLP",
            }
        );
        _dbContext.Set<NCenFiling>().AddRange(original, amendment);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundOperations("MXF");
        var latestSection = result[..result.IndexOf("Service-provider history")];

        latestSection.Should().Contain("AMENDED AUDITOR LLP");
        latestSection.Should().NotContain("ORIGINAL AUDITOR LLP");
        result
            .Should()
            .Contain(
                "2025-01-15 (original; accession 0000065433-25-000001): ORIGINAL AUDITOR LLP; "
                    + "2025-01-15 (amendment; accession 0000065433-25-000002): AMENDED AUDITOR LLP"
            );
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
