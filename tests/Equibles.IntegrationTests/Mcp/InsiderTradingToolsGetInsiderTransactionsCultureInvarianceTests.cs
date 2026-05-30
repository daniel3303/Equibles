using System.Globalization;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Mcp.Tools;
using Equibles.InsiderTrading.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class InsiderTradingToolsGetInsiderTransactionsCultureInvarianceTests : ParadeDbMcpTestBase
{
    public InsiderTradingToolsGetInsiderTransactionsCultureInvarianceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private InsiderTradingTools Sut() =>
        new(
            new InsiderTransactionRepository(DbContext),
            new InsiderOwnerRepository(DbContext),
            new Form144FilingRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<InsiderTradingTools>()
        );

    // GetInsiderTransactions renders the Shares / Price / Value / Owned After cells
    // with the culture-implicit :N0 / :N2 specifiers, which honour the thread
    // CurrentCulture. The established repo contract (the dozens of InvariantCulture
    // call sites across the MCP tools commenting "MCP markdown must not fork the
    // separators by host locale") is that the LLM-facing markdown renders the same
    // on every host. de-DE swaps the thousand separator (1,234,567 → 1.234.567),
    // forking the response — same bug class as the fixed Holdings render methods (#2628).
    [Fact]
    public async Task GetInsiderTransactions_UnderNonInvariantCulture_RendersSharesCultureInvariantly()
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

        // Pin de-DE only for the rendering call; CurrentCulture flows through the
        // tool's await chain via ExecutionContext. Base class restores invariant.
        var previous = CultureInfo.CurrentCulture;
        string result;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            result = await Sut().GetInsiderTransactions("AAPL");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // Every numeric cell must render with en-US separators on any host locale:
        // Shares (:N0), Price (:N2), Value (:N0), Owned After (:N0). de-DE would
        // produce 1.234.567 / $175,50 / $216.666.509 / 7.654.321.
        result.Should().Contain("| 1,234,567 |");
        result.Should().Contain("| $175.50 |");
        result.Should().Contain("| $216,666,509 |");
        result.Should().Contain("| 7,654,321 |");
    }
}
