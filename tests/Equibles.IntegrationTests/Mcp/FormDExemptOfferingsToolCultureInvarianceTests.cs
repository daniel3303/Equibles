using System.Globalization;
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
/// Adversarial cover for <c>GetExemptOfferings</c>'s monetary cells under a non-invariant host
/// culture. MCP markdown must render byte-identically on every host (the established repo contract
/// behind the sibling culture-invariance pins); a de-DE host swaps the separators
/// (5,000,000 → 5.000.000), forking the response. FormDTools is the one MCP tool in this area
/// without a culture pin — this guards the offering-amount column against a future bare-:N0
/// regression of the kind tracked under GH-3058 / GH-3068.
/// </summary>
public class FormDExemptOfferingsToolCultureInvarianceTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly FormDTools _tools;

    public FormDExemptOfferingsToolCultureInvarianceTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new SecTestModuleConfiguration()
        );
        _tools = new FormDTools(
            new FormDFilingRepository(_dbContext),
            new CommonStockRepository(_dbContext),
            errorManager: null,
            NullLogger<FormDTools>.Instance
        );
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task GetExemptOfferings_UnderNonInvariantCulture_RendersOfferingAmountInvariantly()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext
            .Set<FormDFiling>()
            .Add(
                new FormDFiling
                {
                    CommonStockId = stock.Id,
                    AccessionNumber = "acc",
                    FilingDate = new DateOnly(2025, 5, 27),
                    IsAmendment = false,
                    EntityName = "Apple Inc.",
                    EntityType = "Corporation",
                    JurisdictionOfInc = "CALIFORNIA",
                    IndustryGroup = "Technology",
                    FederalExemptions = "06b",
                    TotalOfferingAmount = 5_000_000,
                    IsOfferingAmountIndefinite = false,
                    TotalAmountSold = 2_500_000,
                    TotalRemaining = 2_500_000,
                    IsRemainingIndefinite = false,
                    MinimumInvestmentAccepted = 25_000,
                    TotalNumberAlreadyInvested = 10,
                }
            );
        await _dbContext.SaveChangesAsync();

        var previous = CultureInfo.CurrentCulture;
        string result;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            result = await _tools.GetExemptOfferings("AAPL");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // The offering amount must use en-US grouping on every host; de-DE would render 5.000.000.
        result.Should().Contain("5,000,000");
        result.Should().NotContain("5.000.000");
    }
}
