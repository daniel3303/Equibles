using Equibles.Cftc.Data.Models;
using Equibles.Cftc.Mcp.Tools;
using Equibles.Cftc.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class CftcToolsTests : ParadeDbMcpTestBase {
    private CftcTools Sut() => new(
        new CftcContractRepository(DbContext),
        new CftcPositionReportRepository(DbContext),
        ErrorManager,
        NullLogger<CftcTools>());

    public CftcToolsTests(ParadeDbFixture fixture) : base(fixture) { }

    private static CftcContract CrudeOilContract() => new() {
        MarketCode = "067651",
        MarketName = "CRUDE OIL, LIGHT SWEET-WTI",
        Category = CftcContractCategory.Energy,
    };

    private static CftcContract GoldContract() => new() {
        MarketCode = "088691",
        MarketName = "GOLD",
        Category = CftcContractCategory.Metals,
    };

    private static CftcPositionReport ReportFor(CftcContract contract, DateOnly date,
        long openInterest = 100_000, long commLong = 60_000, long commShort = 40_000,
        long nonCommLong = 30_000, long nonCommShort = 25_000, long nonCommSpreads = 5_000
    ) => new() {
        CftcContract = contract,
        CftcContractId = contract.Id,
        ReportDate = date,
        OpenInterest = openInterest,
        CommLong = commLong, CommShort = commShort,
        NonCommLong = nonCommLong, NonCommShort = nonCommShort, NonCommSpreads = nonCommSpreads,
        TotalRptLong = commLong + nonCommLong, TotalRptShort = commShort + nonCommShort,
        NonRptLong = 5_000, NonRptShort = 5_000,
    };

    // ── GetCftcPositioning ───────────────────────────────────────────────

    [Fact]
    public async Task GetCftcPositioning_UnknownContract_ReturnsNotFoundMessageWithSearchHint() {
        // The unknown-contract message specifically points at SearchCftcMarkets so an MCP
        // client knows how to discover the right market code when the supplied one doesn't
        // match. CFTC codes are opaque (e.g. "067651" = Crude Oil) — without the cross-
        // reference, an MCP client has no path to recover from a typo.
        var result = await Sut().GetCftcPositioning("999999");

        result.Should().Be("Contract '999999' not found. Use SearchCftcMarkets to find available contracts.");
    }

    [Fact]
    public async Task GetCftcPositioning_ContractFoundNoReportsInRange_ReturnsNoReportsMessage() {
        DbContext.Set<CftcContract>().Add(CrudeOilContract());
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetCftcPositioning("067651",
            startDate: "2026-04-01", endDate: "2026-04-30");

        result.Should().Contain("No COT reports found for CRUDE OIL, LIGHT SWEET-WTI (067651)");
    }

    [Fact]
    public async Task GetCftcPositioning_RendersPositionRowsForRequestedContract() {
        var crude = CrudeOilContract();
        var gold = GoldContract();
        DbContext.Set<CftcContract>().AddRange(crude, gold);
        DbContext.Set<CftcPositionReport>().AddRange(
            ReportFor(crude, new DateOnly(2026, 4, 1), openInterest: 1_500_000, commLong: 900_000),
            ReportFor(crude, new DateOnly(2026, 4, 8), openInterest: 1_600_000, commLong: 950_000),
            // Different contract — must NOT appear in the crude report.
            ReportFor(gold, new DateOnly(2026, 4, 1), openInterest: 500_000));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetCftcPositioning("067651",
            startDate: "2026-03-01", endDate: "2026-04-30");

        result.Should().Contain("CRUDE OIL, LIGHT SWEET-WTI (067651) — Energy");
        result.Should().Contain("2026-04-01");
        result.Should().Contain("2026-04-08");
        result.Should().Contain("1,500,000");
        result.Should().Contain("1,600,000");
        // Sanity: no other contract's market name should appear in a single-contract render.
        result.Should().NotContain("GOLD");
        result.IndexOf("2026-04-01").Should().BeLessThan(result.IndexOf("2026-04-08"));
    }

    [Fact]
    public async Task GetCftcPositioning_TrimsMarketCodeWhitespace() {
        DbContext.Set<CftcContract>().Add(CrudeOilContract());
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetCftcPositioning("  067651  ",
            startDate: "2026-03-01", endDate: "2026-04-30");

        result.Should().NotContain("not found");
    }

    // ── GetLatestCftcData ────────────────────────────────────────────────

    [Fact]
    public async Task GetLatestCftcData_EmptyDatabase_ReturnsNoDataMessage() {
        var result = await Sut().GetLatestCftcData();

        result.Should().Be("No CFTC contracts found in the database.");
    }

    [Fact]
    public async Task GetLatestCftcData_GroupsContractsByCategory_AndUsesLatestReport() {
        var crude = CrudeOilContract();
        var gold = GoldContract();
        DbContext.Set<CftcContract>().AddRange(crude, gold);
        DbContext.Set<CftcPositionReport>().AddRange(
            ReportFor(crude, new DateOnly(2026, 4, 1), openInterest: 1_000_000),
            // The newer one — must win in GetLatestPerContract.
            ReportFor(crude, new DateOnly(2026, 4, 8), openInterest: 1_500_000),
            ReportFor(gold, new DateOnly(2026, 4, 8), openInterest: 500_000));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetLatestCftcData();

        result.Should().Contain("**Energy**");
        result.Should().Contain("**Metals**");
        result.Should().Contain("CRUDE OIL");
        result.Should().Contain("GOLD");
        result.Should().Contain("1,500,000"); // Latest crude OI, not the older 1,000,000.
        result.Should().NotContain("1,000,000");
    }

    [Fact]
    public async Task GetLatestCftcData_FiltersByCategory() {
        DbContext.Set<CftcContract>().AddRange(CrudeOilContract(), GoldContract());
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetLatestCftcData(category: "Energy");

        result.Should().Contain("CRUDE OIL");
        result.Should().NotContain("GOLD");
    }

    [Fact]
    public async Task GetLatestCftcData_ContractWithoutReport_RendersEmDashes() {
        DbContext.Set<CftcContract>().Add(CrudeOilContract());
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetLatestCftcData();

        result.Should().Contain("CRUDE OIL");
        result.Should().Contain("| — | — |");
    }

    // ── SearchCftcMarkets ────────────────────────────────────────────────

    [Fact]
    public async Task SearchCftcMarkets_NoMatches_ReturnsNotFoundMessage() {
        DbContext.Set<CftcContract>().Add(CrudeOilContract());
        await DbContext.SaveChangesAsync();

        var result = await Sut().SearchCftcMarkets("PLATINUM");

        result.Should().Be("No contracts found matching 'PLATINUM'.");
    }

    [Fact]
    public async Task SearchCftcMarkets_MatchesByMarketName_ReturnsContract() {
        DbContext.Set<CftcContract>().AddRange(CrudeOilContract(), GoldContract());
        await DbContext.SaveChangesAsync();

        var result = await Sut().SearchCftcMarkets("crude");

        result.Should().Contain("CRUDE OIL, LIGHT SWEET-WTI");
        result.Should().Contain("067651");
        result.Should().NotContain("GOLD");
    }

    [Fact]
    public async Task SearchCftcMarkets_MatchesByMarketCode_ReturnsContract() {
        DbContext.Set<CftcContract>().AddRange(CrudeOilContract(), GoldContract());
        await DbContext.SaveChangesAsync();

        var result = await Sut().SearchCftcMarkets("088691");

        result.Should().Contain("GOLD");
        result.Should().NotContain("CRUDE OIL");
    }

    [Fact]
    public async Task SearchCftcMarkets_RespectsMaxResults() {
        DbContext.Set<CftcContract>().AddRange(
            new CftcContract { MarketCode = "001", MarketName = "WHEAT", Category = CftcContractCategory.Agriculture },
            new CftcContract { MarketCode = "002", MarketName = "CORN", Category = CftcContractCategory.Agriculture },
            new CftcContract { MarketCode = "003", MarketName = "OATS", Category = CftcContractCategory.Agriculture });
        await DbContext.SaveChangesAsync();

        // All three names happen to be 4 letters; we pick something that matches all of them.
        // CFTC marketName is uppercase, but Search lowercases query for case-insensitive match.
        var result = await Sut().SearchCftcMarkets("a", maxResults: 2);

        // Two rows in the result table — count "WHEAT" + "OATS" but not all three.
        var matchCount = new[] { "WHEAT", "CORN", "OATS" }.Count(name => result.Contains(name));
        matchCount.Should().Be(2);
    }
}
