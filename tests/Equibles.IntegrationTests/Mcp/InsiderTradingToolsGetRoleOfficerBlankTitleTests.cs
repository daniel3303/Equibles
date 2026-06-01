using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Mcp.Tools;
using Equibles.InsiderTrading.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Adversarial cover for the role label (<c>GetRole</c>) when an insider is flagged as an officer
/// but carries no usable officer title. The contract is that such a filer still reads as an
/// "Officer" — a blank or whitespace title must fall back to the generic label, never render an
/// empty Role cell. Existing role tests only seed officers with a concrete title (CEO/CFO), so the
/// blank-title fallback branch is otherwise unexercised.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderTradingToolsGetRoleOfficerBlankTitleTests : ParadeDbMcpTestBase
{
    public InsiderTradingToolsGetRoleOfficerBlankTitleTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetInsiderTransactions_OfficerWithBlankTitle_ShowsGenericOfficerRole()
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
            Name = "Untitled Officer",
            City = "New York",
            StateOrCountry = "NY",
            IsDirector = false,
            IsOfficer = true,
            // Whitespace title — must fall back to the generic "Officer" label.
            OfficerTitle = "   ",
            IsTenPercentOwner = false,
        };
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<InsiderOwner>().Add(owner);
        DbContext
            .Set<InsiderTransaction>()
            .Add(
                new InsiderTransaction
                {
                    CommonStockId = stock.Id,
                    CommonStock = stock,
                    InsiderOwnerId = owner.Id,
                    InsiderOwner = owner,
                    TransactionDate = new DateOnly(2024, 6, 14),
                    FilingDate = new DateOnly(2024, 6, 15),
                    TransactionCode = TransactionCode.Purchase,
                    Shares = 1_000,
                    PricePerShare = 150.00m,
                    AcquiredDisposed = AcquiredDisposed.Acquired,
                    SharesOwnedAfter = 5_000,
                    OwnershipNature = OwnershipNature.Direct,
                    SecurityTitle = "Common Stock",
                    AccessionNumber = "0001234567-24-000001",
                }
            );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var sut = new InsiderTradingTools(
            new InsiderTransactionRepository(DbContext),
            new InsiderOwnerRepository(DbContext),
            new Form144FilingRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<InsiderTradingTools>()
        );

        var result = await sut.GetInsiderTransactions("AAPL");

        // An officer with no usable title still reads as "Officer" — not a blank Role cell.
        result.Should().Contain("Officer");
    }
}
