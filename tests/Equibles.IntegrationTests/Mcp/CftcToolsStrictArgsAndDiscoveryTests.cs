using Equibles.Cftc.Data.Models;
using Equibles.Cftc.Mcp.Tools;
using Equibles.Cftc.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Pins the MCP-audit fixes on the CFTC tools: strict category validation on
/// GetLatestCftcData (no silent all-categories fallback, display spellings accepted,
/// numeric strings rejected), the market-code column and units footer, the
/// query-optional list-all mode and truncation note on SearchCftcMarkets, and strict
/// dates plus the truncation note on GetCftcPositioning.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CftcToolsStrictArgsAndDiscoveryTests : ParadeDbMcpTestBase
{
    private CftcTools Sut() =>
        new(
            new CftcContractRepository(DbContext),
            new CftcPositionReportRepository(DbContext),
            ErrorManager,
            NullLogger<CftcTools>()
        );

    public CftcToolsStrictArgsAndDiscoveryTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private static CftcContract Contract(string code, string name, CftcContractCategory category) =>
        new()
        {
            MarketCode = code,
            MarketName = name,
            Category = category,
        };

    private static CftcPositionReport ReportFor(CftcContract contract, DateOnly date) =>
        new()
        {
            CftcContract = contract,
            CftcContractId = contract.Id,
            ReportDate = date,
            OpenInterest = 100_000,
            CommLong = 60_000,
            CommShort = 40_000,
            NonCommLong = 30_000,
            NonCommShort = 25_000,
            NonCommSpreads = 5_000,
            TotalRptLong = 90_000,
            TotalRptShort = 65_000,
            NonRptLong = 5_000,
            NonRptShort = 5_000,
        };

    // ── GetLatestCftcData category validation ────────────────────────────

    [Fact]
    public async Task GetLatestCftcData_UnknownCategory_ReturnsErrorListingAccepted()
    {
        // An invalid category used to silently fall back to ALL categories — the caller
        // got the full 30+ row table and could misreport it as the filtered set.
        DbContext
            .Set<CftcContract>()
            .Add(Contract("067651", "Crude Oil, Light Sweet (NYMEX)", CftcContractCategory.Energy));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetLatestCftcData(category: "ZZZZZT");

        result
            .Should()
            .Be(
                "Unknown category 'ZZZZZT'. Accepted: Agriculture, Energy, Metals, EquityIndices, InterestRates, Currencies."
            );
    }

    [Fact]
    public async Task GetLatestCftcData_DisplaySpellingWithSpace_IsAccepted()
    {
        // The tool's own output prints display names ("Interest Rates"), so a caller
        // copying that spelling back must not be rejected: whitespace folds before
        // the enum-name match.
        DbContext
            .Set<CftcContract>()
            .AddRange(
                Contract("043602", "10-Year T-Notes (CBOT)", CftcContractCategory.InterestRates),
                Contract("088691", "Gold (COMEX)", CftcContractCategory.Metals)
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetLatestCftcData(category: "Interest Rates");

        result.Should().Contain("10-Year T-Notes");
        result.Should().NotContain("Gold");
    }

    [Fact]
    public async Task GetLatestCftcData_NumericCategory_IsRejected()
    {
        // Enum.TryParse accepts numeric strings (even undefined ones); the name-based
        // match must reject them so "999" can't slip into a bogus filtered query.
        DbContext
            .Set<CftcContract>()
            .Add(Contract("088691", "Gold (COMEX)", CftcContractCategory.Metals));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetLatestCftcData(category: "999");

        result.Should().StartWith("Unknown category '999'");
    }

    // ── GetLatestCftcData market-code handoff ────────────────────────────

    [Fact]
    public async Task GetLatestCftcData_RowsCarryMarketCodeForPositioningHandoff()
    {
        // GetCftcPositioning requires a market code; the snapshot must include it so a
        // consumer doesn't need an extra SearchCftcMarkets round-trip per drill-down.
        var gold = Contract("088691", "Gold (COMEX)", CftcContractCategory.Metals);
        DbContext.Set<CftcContract>().Add(gold);
        DbContext.Set<CftcPositionReport>().Add(ReportFor(gold, new DateOnly(2026, 7, 7)));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetLatestCftcData();

        result.Should().Contain("| Gold (COMEX) | 088691 |");
        result.Should().Contain("GetCftcPositioning(marketCode)");
        result.Should().Contain("contract counts");
    }

    // ── SearchCftcMarkets discovery ──────────────────────────────────────

    [Fact]
    public async Task SearchCftcMarkets_NoQuery_ListsAllTrackedContracts()
    {
        // With a ~35-contract curated universe, enumerating everything is the natural
        // discovery call; it used to require exploiting the Contains search with "(".
        DbContext
            .Set<CftcContract>()
            .AddRange(
                Contract("088691", "Gold (COMEX)", CftcContractCategory.Metals),
                Contract("067651", "Crude Oil, Light Sweet (NYMEX)", CftcContractCategory.Energy)
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().SearchCftcMarkets();

        result.Should().Contain("Tracked CFTC contracts:");
        result.Should().Contain("Gold (COMEX)");
        result.Should().Contain("Crude Oil");
    }

    [Fact]
    public async Task SearchCftcMarkets_MoreMatchesThanMaxResults_AppendsTruncationNote()
    {
        // Results are grouped by category, so silent truncation used to drop whole
        // trailing categories with no signal that more contracts exist.
        DbContext
            .Set<CftcContract>()
            .AddRange(
                Contract("001602", "Wheat-SRW (CBOT)", CftcContractCategory.Agriculture),
                Contract("002602", "Corn (CBOT)", CftcContractCategory.Agriculture),
                Contract("099741", "Euro FX (CME)", CftcContractCategory.Currencies)
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().SearchCftcMarkets(maxResults: 2);

        result.Should().Contain("Showing first 2 of 3 results");
    }

    // ── GetCftcPositioning strict dates + truncation ─────────────────────

    [Fact]
    public async Task GetCftcPositioning_MalformedStartDate_ReturnsInvalidArgumentError()
    {
        DbContext
            .Set<CftcContract>()
            .Add(Contract("088691", "Gold (COMEX)", CftcContractCategory.Metals));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetCftcPositioning("088691", startDate: "last year");

        result.Should().Be("Unknown startDate 'last year'. Accepted: yyyy-MM-dd.");
    }

    [Fact]
    public async Task GetCftcPositioning_InvertedRange_ReturnsSwapErrorNotEmptyRange()
    {
        DbContext
            .Set<CftcContract>()
            .Add(Contract("088691", "Gold (COMEX)", CftcContractCategory.Metals));
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetCftcPositioning("088691", startDate: "2026-07-01", endDate: "2026-01-01");

        result
            .Should()
            .Be("startDate (2026-07-01) is after endDate (2026-01-01) - swap the dates.");
    }

    [Fact]
    public async Task GetCftcPositioning_RangeExceedsMaxResults_AppendsNewestKeptNote()
    {
        var gold = Contract("088691", "Gold (COMEX)", CftcContractCategory.Metals);
        DbContext.Set<CftcContract>().Add(gold);
        for (var week = 1; week <= 3; week++)
            DbContext
                .Set<CftcPositionReport>()
                .Add(ReportFor(gold, new DateOnly(2026, 4, week * 7)));
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetCftcPositioning(
                "088691",
                startDate: "2026-04-01",
                endDate: "2026-04-30",
                maxResults: 2
            );

        result.Should().Contain("Showing the newest 2 of 3 reports");
        result.Should().Contain("2026-04-21");
        result.Should().NotContain("2026-04-07");
    }
}
