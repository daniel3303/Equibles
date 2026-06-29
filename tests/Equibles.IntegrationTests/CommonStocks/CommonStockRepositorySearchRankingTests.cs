using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.CommonStocks;

/// <summary>
/// Pins <see cref="CommonStockRepository.Search"/>'s ranking against real ParadeDB ILike: an exact
/// ticker hit must lead the results, then ticker prefix hits, then the alphabetical fallback.
/// Without it a typed symbol sorts purely alphabetically and falls behind earlier tickers that only
/// match the term in their name/description — so a per-group result cap (the ⌘K palette caps each
/// group) drops the exact match entirely (e.g. "ARE" buried behind ABXXF/ACHC/ACIW/…). Search uses
/// EF.Functions.ILike, which the in-memory provider can't translate, so this needs a real database.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CommonStockRepositorySearchRankingTests : ParadeDbMcpTestBase
{
    public CommonStockRepositorySearchRankingTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private static CommonStock Stock(string ticker, string name) =>
        new()
        {
            Ticker = ticker,
            Name = name,
            Cik = ticker,
        };

    [Fact]
    public async Task Search_ExactTickerMatch_RanksFirstEvenWhenAlphabeticallyLast()
    {
        // All four match the term "are" (ARE by exact ticker; the rest only in their name), and ARE
        // sorts alphabetically last — so a naive alphabetical order would bury it.
        DbContext.AddRange(
            Stock("ABXXF", "Abaxx Technologies Inc."),
            Stock("ACHC", "Acadia Healthcare Company, Inc."),
            Stock("ACRE", "Ares Commercial Real Estate Corp"),
            Stock("ARE", "Alexandria Real Estate Equities, Inc.")
        );
        await DbContext.SaveChangesAsync();

        var results = new CommonStockRepository(DbContext).Search("ARE").ToList();

        Assert.Equal("ARE", results[0].Ticker);
    }

    [Fact]
    public async Task Search_IsCaseInsensitive_ForTheExactTickerRanking()
    {
        DbContext.AddRange(
            Stock("ABXXF", "Abaxx Technologies Inc."),
            Stock("ARE", "Alexandria Real Estate Equities, Inc.")
        );
        await DbContext.SaveChangesAsync();

        var results = new CommonStockRepository(DbContext).Search("are").ToList();

        Assert.Equal("ARE", results[0].Ticker);
    }

    [Fact]
    public async Task Search_TickerPrefixMatches_RankAheadOfNameOnlyMatches()
    {
        // No exact ticker hit: "AR" should still surface the ticker that STARTS with it ahead of one
        // that only matches the term in its name.
        DbContext.AddRange(
            Stock("ABXXF", "Argent Holdings"), // name contains "ar", ticker does not start with it
            Stock("ARLP", "Alliance Resource Partners") // ticker starts with "AR"
        );
        await DbContext.SaveChangesAsync();

        var results = new CommonStockRepository(DbContext).Search("AR").ToList();

        Assert.Equal("ARLP", results[0].Ticker);
    }

    [Fact]
    public async Task Search_NoExactOrPrefixHit_FallsBackToAlphabeticalTickerOrder()
    {
        DbContext.AddRange(Stock("ZETA", "Zeta Group Holdings"), Stock("BETA", "Beta Group Inc."));
        await DbContext.SaveChangesAsync();

        // Both match only via their name token ("Group"); with no ticker exact/prefix hit they fall
        // back to alphabetical ticker order.
        var results = new CommonStockRepository(DbContext).Search("Group").ToList();

        Assert.Equal(new[] { "BETA", "ZETA" }, results.Select(s => s.Ticker).ToArray());
    }
}
