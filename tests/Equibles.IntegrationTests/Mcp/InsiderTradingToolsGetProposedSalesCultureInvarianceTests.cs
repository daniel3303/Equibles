using System.Globalization;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Mcp.Tools;
using Equibles.InsiderTrading.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Adversarial cover for <c>GetProposedSales</c>'s monetary/share cells under a non-invariant host
/// culture. MCP markdown must render byte-identically on every host (the established repo contract
/// behind the sibling GetInsiderOwnership / GetInsiderTransactions culture-invariance pins); a
/// de-DE host swaps the separators (87,500,000 → 87.500.000), forking the response. Guards the
/// Form 144 amount/share columns against a future bare-:N0 regression of the GH-3058 / GH-3068 class.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderTradingToolsGetProposedSalesCultureInvarianceTests : ParadeDbMcpTestBase
{
    public InsiderTradingToolsGetProposedSalesCultureInvarianceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetProposedSales_UnderNonInvariantCulture_RendersAmountsCultureInvariantly()
    {
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        DbContext.Set<CommonStock>().Add(stock);
        DbContext
            .Set<Form144Filing>()
            .Add(
                new Form144Filing
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    AccessionNumber = "0000320193-26-000001",
                    FilingDate = new DateOnly(2026, 4, 20),
                    SellerName = "Jane Insider",
                    RelationshipToIssuer = "Director",
                    SecurityClassTitle = "Common Stock",
                    BrokerName = "Goldman Sachs",
                    SharesToBeSold = 500_000,
                    AggregateMarketValue = 87_500_000,
                    SharesOutstanding = 16_000_000_000,
                    ApproxSaleDate = new DateOnly(2026, 5, 1),
                    SecuritiesExchangeName = "NASDAQ",
                }
            );
        await DbContext.SaveChangesAsync();

        var previous = CultureInfo.CurrentCulture;
        string result;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            result = await Sut().GetProposedSales("AAPL");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // Shares and aggregate market value must use en-US grouping on every host;
        // de-DE would render 500.000 and $87.500.000.
        result.Should().Contain("| 500,000 |");
        result.Should().Contain("| $87,500,000 |");
        result.Should().NotContain("87.500.000");
    }

    private InsiderTradingTools Sut() =>
        new(
            new InsiderTransactionRepository(DbContext),
            new InsiderOwnerRepository(DbContext),
            new Form144FilingRepository(DbContext),
            new CommonStockRepository(DbContext),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<InsiderTradingTools>()
        );
}
