using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Data.Models;
using Equibles.Congress.Mcp.Tools;
using Equibles.Congress.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Sibling to <see cref="CongressToolsTests"/>'s
/// <c>GetCongressionalTrades_FiltersByTransactionType</c>, which passes the
/// canonical "Sale" — that parse succeeds under both case-sensitive and
/// case-insensitive <c>Enum.TryParse</c>, so the existing pin doesn't
/// discriminate the case-insensitive contract introduced by the new
/// <c>ApplyTransactionTypeFilter</c> helper. Lowercase "sale" is the
/// discriminating case: a regression that flipped <c>ignoreCase: true</c> to
/// <c>false</c> would silently make the parse fail and the helper fall
/// through to the unfiltered query, returning ALL trades regardless of type.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CongressToolsLowercaseTransactionTypeTests : ParadeDbMcpTestBase
{
    private CongressTools Sut() =>
        new(
            new CongressionalTradeRepository(DbContext),
            new CongressMemberRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<CongressTools>()
        );

    public CongressToolsLowercaseTransactionTypeTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetCongressionalTrades_LowercaseTransactionType_FiltersCaseInsensitively()
    {
        var stock = new CommonStock
        {
            Ticker = "NVDA",
            Name = "NVIDIA Corporation",
            Cik = "0001045810",
        };
        var pelosi = new CongressMember
        {
            Name = "Nancy Pelosi",
            Position = CongressPosition.Representative,
        };
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<CongressMember>().Add(pelosi);
        DbContext
            .Set<CongressionalTrade>()
            .AddRange(
                new CongressionalTrade
                {
                    CongressMember = pelosi,
                    CommonStock = stock,
                    TransactionDate = new DateOnly(2026, 3, 10),
                    FilingDate = new DateOnly(2026, 4, 9),
                    TransactionType = CongressTransactionType.Purchase,
                    OwnerType = "Self",
                    AssetName = "Common Stock Purchase",
                    AmountFrom = 1_000,
                    AmountTo = 15_000,
                },
                new CongressionalTrade
                {
                    CongressMember = pelosi,
                    CommonStock = stock,
                    TransactionDate = new DateOnly(2026, 3, 20),
                    FilingDate = new DateOnly(2026, 4, 19),
                    TransactionType = CongressTransactionType.Sale,
                    OwnerType = "Self",
                    AssetName = "Common Stock Sale",
                    AmountFrom = 50_000,
                    AmountTo = 100_000,
                }
            );
        await DbContext.SaveChangesAsync();

        // Lowercase "sale" — discriminates the case-insensitive parse contract.
        var result = await Sut()
            .GetCongressionalTrades(
                "NVDA",
                transactionType: "sale",
                startDate: "2026-01-01",
                endDate: "2026-04-30"
            );

        result.Should().Contain("$50,000");
        result.Should().NotContain("$1,000–$15,000");
    }
}
