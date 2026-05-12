using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Data.Models;
using Equibles.Congress.Mcp.Tools;
using Equibles.Congress.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class CongressToolsTests : ParadeDbMcpTestBase {
    private CongressTools Sut() => new(
        new CongressionalTradeRepository(DbContext),
        new CongressMemberRepository(DbContext),
        new CommonStockRepository(DbContext),
        ErrorManager,
        NullLogger<CongressTools>());

    public CongressToolsTests(ParadeDbFixture fixture) : base(fixture) { }

    private static CommonStock NvdaStock() => new() {
        Ticker = "NVDA", Name = "NVIDIA Corporation", Cik = "0001045810",
    };

    private static CongressMember PelosiMember() => new() {
        Name = "Nancy Pelosi", Position = CongressPosition.Representative,
    };

    private static CongressMember CrenshawMember() => new() {
        Name = "Dan Crenshaw", Position = CongressPosition.Representative,
    };

    private static CongressionalTrade TradeFor(CongressMember member, CommonStock stock,
        DateOnly transactionDate, CongressTransactionType type = CongressTransactionType.Purchase,
        long amountFrom = 1_000, long amountTo = 15_000, string assetName = "Common Stock"
    ) => new() {
        CongressMember = member, CongressMemberId = member.Id,
        CommonStock = stock, CommonStockId = stock.Id,
        TransactionDate = transactionDate,
        FilingDate = transactionDate.AddDays(30),
        TransactionType = type,
        OwnerType = "Self",
        AssetName = assetName,
        AmountFrom = amountFrom, AmountTo = amountTo,
    };

    // ── GetCongressionalTrades ───────────────────────────────────────────

    [Fact]
    public async Task GetCongressionalTrades_UnknownTicker_ReturnsNotFoundMessage() {
        var result = await Sut().GetCongressionalTrades("ZZZZ");

        result.Should().Be("Stock 'ZZZZ' not found.");
    }

    [Fact]
    public async Task GetCongressionalTrades_NoTrades_ReturnsEmptyRangeMessage() {
        DbContext.Set<CommonStock>().Add(NvdaStock());
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetCongressionalTrades("NVDA",
            startDate: "2026-01-01", endDate: "2026-04-30");

        result.Should().Contain("No congressional trades found for NVDA");
    }

    [Fact]
    public async Task GetCongressionalTrades_RendersTradeRowsWithMemberName() {
        var stock = NvdaStock();
        var pelosi = PelosiMember();
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<CongressMember>().Add(pelosi);
        DbContext.Set<CongressionalTrade>().Add(
            TradeFor(pelosi, stock, new DateOnly(2026, 3, 15), amountFrom: 1_000_001, amountTo: 5_000_000));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetCongressionalTrades("NVDA",
            startDate: "2026-01-01", endDate: "2026-04-30");

        result.Should().Contain("Congressional trades for NVDA (NVIDIA Corporation)");
        result.Should().Contain("Nancy Pelosi");
        result.Should().Contain("Representative");
        result.Should().Contain("Purchase");
        result.Should().Contain("$1,000,001");
    }

    [Fact]
    public async Task GetCongressionalTrades_FiltersByTransactionType() {
        var stock = NvdaStock();
        var pelosi = PelosiMember();
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<CongressMember>().Add(pelosi);
        DbContext.Set<CongressionalTrade>().AddRange(
            TradeFor(pelosi, stock, new DateOnly(2026, 3, 10),
                CongressTransactionType.Purchase, amountFrom: 1_000, amountTo: 15_000, assetName: "Common Stock"),
            TradeFor(pelosi, stock, new DateOnly(2026, 3, 20),
                CongressTransactionType.Sale, amountFrom: 50_000, amountTo: 100_000, assetName: "Common Stock Sale"));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetCongressionalTrades("NVDA",
            transactionType: "Sale", startDate: "2026-01-01", endDate: "2026-04-30");

        result.Should().Contain("Sale");
        result.Should().Contain("$50,000");
        result.Should().NotContain("$1,000–$15,000");
    }

    [Fact]
    public async Task GetCongressionalTrades_OrdersByTransactionDateDescending() {
        var stock = NvdaStock();
        var pelosi = PelosiMember();
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<CongressMember>().Add(pelosi);
        DbContext.Set<CongressionalTrade>().AddRange(
            TradeFor(pelosi, stock, new DateOnly(2026, 1, 10), assetName: "Older Position"),
            TradeFor(pelosi, stock, new DateOnly(2026, 3, 15), assetName: "Newer Position"));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetCongressionalTrades("NVDA",
            startDate: "2026-01-01", endDate: "2026-04-30");

        // Newest first — the March row must precede the January row in the rendered output.
        result.IndexOf("2026-03-15").Should().BeLessThan(result.IndexOf("2026-01-10"));
    }

    // ── GetMemberTrades ──────────────────────────────────────────────────

    [Fact]
    public async Task GetMemberTrades_UnknownMember_ReturnsNotFoundMessageWithSearchHint() {
        var result = await Sut().GetMemberTrades("Nonexistent Person");

        result.Should().Be("Member 'Nonexistent Person' not found. Use SearchCongressMembers to find the exact name.");
    }

    [Fact]
    public async Task GetMemberTrades_NoTrades_ReturnsEmptyRangeMessage() {
        DbContext.Set<CongressMember>().Add(PelosiMember());
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetMemberTrades("Nancy Pelosi",
            startDate: "2026-01-01", endDate: "2026-04-30");

        result.Should().Contain("No trades found for Nancy Pelosi (Representative)");
    }

    [Fact]
    public async Task GetMemberTrades_RendersTradeRowsForMember() {
        var stock = NvdaStock();
        var pelosi = PelosiMember();
        var crenshaw = CrenshawMember();
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<CongressMember>().AddRange(pelosi, crenshaw);
        DbContext.Set<CongressionalTrade>().AddRange(
            TradeFor(pelosi, stock, new DateOnly(2026, 3, 15), assetName: "Pelosi Trade"),
            TradeFor(crenshaw, stock, new DateOnly(2026, 3, 16), assetName: "Crenshaw Trade"));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetMemberTrades("Nancy Pelosi",
            startDate: "2026-01-01", endDate: "2026-04-30");

        result.Should().Contain("Trades by Nancy Pelosi (Representative)");
        result.Should().Contain("NVDA");
        result.Should().Contain("Pelosi Trade");
        result.Should().NotContain("Crenshaw Trade");
    }

    [Fact]
    public async Task GetMemberTrades_FiltersByDateRange() {
        var stock = NvdaStock();
        var pelosi = PelosiMember();
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<CongressMember>().Add(pelosi);
        DbContext.Set<CongressionalTrade>().AddRange(
            TradeFor(pelosi, stock, new DateOnly(2025, 6, 1), assetName: "In range"),
            TradeFor(pelosi, stock, new DateOnly(2026, 3, 1), assetName: "Outside range"));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetMemberTrades("Nancy Pelosi",
            startDate: "2025-01-01", endDate: "2025-12-31");

        result.Should().Contain("In range");
        result.Should().NotContain("Outside range");
    }

    // ── SearchCongressMembers ────────────────────────────────────────────

    [Fact]
    public async Task SearchCongressMembers_NoMatches_ReturnsNotFoundMessage() {
        DbContext.Set<CongressMember>().Add(PelosiMember());
        await DbContext.SaveChangesAsync();

        var result = await Sut().SearchCongressMembers("Smith");

        result.Should().Be("No congress members found matching 'Smith'.");
    }

    [Fact]
    public async Task SearchCongressMembers_MatchesByPartialName_ReturnsMembers() {
        // Real Postgres ILike is what's being exercised here — InMemory provider doesn't
        // implement ILike at all (the production code uses EF.Functions.ILike), so this
        // assertion only holds against a real Postgres backend.
        DbContext.Set<CongressMember>().AddRange(PelosiMember(), CrenshawMember(),
            new CongressMember { Name = "Ted Cruz", Position = CongressPosition.Senator });
        await DbContext.SaveChangesAsync();

        var result = await Sut().SearchCongressMembers("Cr");

        result.Should().Contain("Dan Crenshaw");
        result.Should().Contain("Ted Cruz");
        result.Should().NotContain("Nancy Pelosi");
    }

    [Fact]
    public async Task SearchCongressMembers_IsCaseInsensitive() {
        DbContext.Set<CongressMember>().Add(PelosiMember());
        await DbContext.SaveChangesAsync();

        var result = await Sut().SearchCongressMembers("pelosi");

        result.Should().Contain("Nancy Pelosi");
    }
}
