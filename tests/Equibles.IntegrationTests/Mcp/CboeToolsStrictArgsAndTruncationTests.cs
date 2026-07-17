using Equibles.Cboe.Data.Models;
using Equibles.Cboe.Mcp.Tools;
using Equibles.Cboe.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Pins the MCP-audit fixes on the CBOE tools: strict yyyy-MM-dd date parsing (no
/// silent fallback to the default window), inverted-range errors, and the
/// newest-kept truncation note when maxResults cuts the requested range.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CboeToolsStrictArgsAndTruncationTests : ParadeDbMcpTestBase
{
    private CboeTools Sut() =>
        new(
            new CboePutCallRatioRepository(DbContext),
            new CboeVixDailyRepository(DbContext),
            ErrorManager,
            NullLogger<CboeTools>()
        );

    public CboeToolsStrictArgsAndTruncationTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private static CboeVixDaily VixFor(DateOnly date) =>
        new()
        {
            Date = date,
            Open = 14.20m,
            High = 15.30m,
            Low = 13.80m,
            Close = 14.95m,
        };

    private static CboePutCallRatio ReconcilableRatioFor(DateOnly date) =>
        new()
        {
            RatioType = CboePutCallRatioType.Equity,
            Date = date,
            CallVolume = 1_200_000,
            PutVolume = 800_000,
            TotalVolume = 2_000_000,
            PutCallRatio = 0.67m,
        };

    [Fact]
    public async Task GetVixHistory_MalformedStartDate_ReturnsInvalidArgumentError()
    {
        // ParseDateOr used to silently replace an unparseable date with "3 months ago",
        // returning plausible recent data as if the request had been honored.
        var result = await Sut().GetVixHistory(startDate: "2020-13-01");

        result.Should().Be("Unknown startDate '2020-13-01'. Accepted: yyyy-MM-dd.");
    }

    [Fact]
    public async Task GetVixHistory_InvertedRange_ReturnsSwapErrorNotEmptyRange()
    {
        var result = await Sut().GetVixHistory(startDate: "2026-07-01", endDate: "2026-01-01");

        result
            .Should()
            .Be("startDate (2026-07-01) is after endDate (2026-01-01) - swap the dates.");
    }

    [Fact]
    public async Task GetVixHistory_RangeExceedsMaxResults_AppendsNewestKeptNote()
    {
        // The query keeps the newest maxResults rows but renders oldest→newest, so a
        // truncated March-2020 style query silently dropped the spike the caller asked
        // about. The note must name both counts and say the NEWEST rows were kept.
        for (var day = 1; day <= 5; day++)
            DbContext.Set<CboeVixDaily>().Add(VixFor(new DateOnly(2026, 4, day)));
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetVixHistory(startDate: "2026-04-01", endDate: "2026-04-30", maxResults: 2);

        result.Should().Contain("Showing the newest 2 of 5 records");
        result.Should().Contain("2026-04-05");
        result.Should().NotContain("2026-04-03");
    }

    [Fact]
    public async Task GetVixHistory_AllRowsShown_OmitsTruncationNote()
    {
        DbContext.Set<CboeVixDaily>().Add(VixFor(new DateOnly(2026, 4, 1)));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetVixHistory(startDate: "2026-04-01", endDate: "2026-04-30");

        result.Should().NotContain("Showing the newest");
    }

    [Fact]
    public async Task GetPutCallRatios_MalformedEndDate_ReturnsInvalidArgumentError()
    {
        var result = await Sut().GetPutCallRatios(endDate: "last month");

        result.Should().Be("Unknown endDate 'last month'. Accepted: yyyy-MM-dd.");
    }

    [Fact]
    public async Task GetPutCallRatios_InvertedRange_ReturnsSwapErrorNotEmptyRange()
    {
        var result = await Sut().GetPutCallRatios(startDate: "2026-07-10", endDate: "2026-06-01");

        result
            .Should()
            .Be("startDate (2026-07-10) is after endDate (2026-06-01) - swap the dates.");
    }

    [Fact]
    public async Task GetPutCallRatios_RangeExceedsMaxResults_AppendsNewestKeptNote()
    {
        for (var day = 1; day <= 5; day++)
            DbContext.Set<CboePutCallRatio>().Add(ReconcilableRatioFor(new DateOnly(2026, 4, day)));
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetPutCallRatios(
                type: "Equity",
                startDate: "2026-04-01",
                endDate: "2026-04-30",
                maxResults: 2
            );

        result.Should().Contain("Showing the newest 2 of 5 records");
        result.Should().Contain("2026-04-05");
        result.Should().NotContain("2026-04-03");
    }
}
