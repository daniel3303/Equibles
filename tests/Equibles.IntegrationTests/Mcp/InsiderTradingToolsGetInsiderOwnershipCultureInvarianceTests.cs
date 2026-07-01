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

[Collection(ParadeDbCollection.Name)]
public class InsiderTradingToolsGetInsiderOwnershipCultureInvarianceTests : ParadeDbMcpTestBase
{
    public InsiderTradingToolsGetInsiderOwnershipCultureInvarianceTests(ParadeDbFixture fixture)
        : base(fixture) { }

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

    // GetInsiderOwnership renders the Shares Owned cell as {SharesOwnedAfter:N0} with the
    // culture-implicit specifier, which honours the thread CurrentCulture. The established
    // repo contract (the InvariantCulture call sites and the sibling GetInsiderTransactions
    // culture pin: "MCP markdown must not fork the separators by host locale") is byte-identical
    // output on every host. de-DE swaps the thousand separator (7,654,321 → 7.654.321), forking
    // the response — same bug class as #3013 / #3030 / #3035 / #3043.
    [Fact]
    public async Task GetInsiderOwnership_UnderNonInvariantCulture_RendersSharesOwnedCultureInvariantly()
    {
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var owner = new InsiderOwner
        {
            OwnerCik = "0001234567",
            Name = "John Doe",
            City = "New York",
            StateOrCountry = "NY",
            IsDirector = true,
        };
        var transaction = new InsiderTransaction
        {
            CommonStock = stock,
            InsiderOwner = owner,
            TransactionDate = new DateOnly(2024, 6, 14),
            FilingDate = new DateOnly(2024, 6, 15),
            TransactionCode = TransactionCode.Sale,
            Shares = 1_234_567,
            PricePerShare = 175.50m,
            AcquiredDisposed = AcquiredDisposed.Disposed,
            SharesOwnedAfter = 7_654_321,
            OwnershipNature = OwnershipNature.Direct,
            SecurityTitle = "Common Stock",
            AccessionNumber = "0001234567-24-000001",
        };
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<InsiderOwner>().Add(owner);
        DbContext.Set<InsiderTransaction>().Add(transaction);
        await DbContext.SaveChangesAsync();

        var previous = CultureInfo.CurrentCulture;
        string result;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            result = await Sut().GetInsiderOwnership("AAPL");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // The Shares Owned cell (bare :N0) must render with en-US grouping on every
        // host locale; de-DE would produce 7.654.321.
        result.Should().Contain("| 7,654,321 |");
    }
}
