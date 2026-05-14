using Equibles.Cboe.Data.Models;
using Equibles.Cboe.Mcp.Tools;
using Equibles.Cboe.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class CboeToolsTests : ParadeDbMcpTestBase
{
    private CboeTools Sut() =>
        new(
            new CboePutCallRatioRepository(DbContext),
            new CboeVixDailyRepository(DbContext),
            ErrorManager,
            NullLogger<CboeTools>()
        );

    public CboeToolsTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // ── GetPutCallRatios ─────────────────────────────────────────────────

    [Fact]
    public async Task GetPutCallRatios_InvalidType_ReturnsErrorWithValidTypesList()
    {
        // The invalid-type message hard-codes the list of accepted enum values
        // ("Total, Equity, Index, Vix, Etp"). When CboePutCallRatioType gains a
        // new variant, this string must be updated in lockstep — without a pin
        // an MCP client sees a stale enumeration and can't discover the new type.
        var result = await Sut().GetPutCallRatios(type: "Bogus");

        result.Should().Be("Invalid type 'Bogus'. Valid types: Total, Equity, Index, Vix, Etp");
    }

    [Fact]
    public async Task GetPutCallRatios_EmptyDatabase_ReturnsNoDataMessage()
    {
        var result = await Sut().GetPutCallRatios("Equity");

        result.Should().StartWith("No put/call ratio data found for Equity");
    }

    [Fact]
    public async Task GetPutCallRatios_ReturnsAscendingDateTableForRequestedType()
    {
        var equityFirst = new CboePutCallRatio
        {
            RatioType = CboePutCallRatioType.Equity,
            Date = new DateOnly(2026, 4, 1),
            CallVolume = 1_200_000,
            PutVolume = 800_000,
            TotalVolume = 2_000_000,
            PutCallRatio = 0.67m,
        };
        var equitySecond = new CboePutCallRatio
        {
            RatioType = CboePutCallRatioType.Equity,
            Date = new DateOnly(2026, 4, 2),
            CallVolume = 1_500_000,
            PutVolume = 1_100_000,
            TotalVolume = 2_600_000,
            PutCallRatio = 0.73m,
        };
        var indexNoise = new CboePutCallRatio
        {
            RatioType = CboePutCallRatioType.Index,
            Date = new DateOnly(2026, 4, 1),
            CallVolume = 9_999,
            PutVolume = 9_999,
            TotalVolume = 19_998,
            PutCallRatio = 1.0m,
        };
        DbContext.Set<CboePutCallRatio>().AddRange(equityFirst, equitySecond, indexNoise);
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetPutCallRatios(type: "equity", startDate: "2026-03-01", endDate: "2026-04-30");

        result.Should().Contain("CBOE Equity Put/Call Ratios");
        result.Should().Contain("2026-04-01");
        result.Should().Contain("2026-04-02");
        result.Should().Contain("1,200,000");
        result.Should().Contain("0.67");
        result.Should().Contain("0.73");
        // Sanity: index-type row must not leak into an equity-typed query.
        result.Should().NotContain("9,999");
        // Ascending sort: first date appears before second in the rendered string.
        result.IndexOf("2026-04-01").Should().BeLessThan(result.IndexOf("2026-04-02"));
    }

    [Fact]
    public async Task GetPutCallRatios_MaxResultsLimitsRows()
    {
        var rows = Enumerable
            .Range(1, 5)
            .Select(i => new CboePutCallRatio
            {
                RatioType = CboePutCallRatioType.Total,
                Date = new DateOnly(2026, 4, i),
                CallVolume = 1_000_000 * i,
                PutVolume = 500_000 * i,
                TotalVolume = 1_500_000 * i,
                PutCallRatio = 0.5m,
            });
        DbContext.Set<CboePutCallRatio>().AddRange(rows);
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetPutCallRatios(
                type: "Total",
                startDate: "2026-04-01",
                endDate: "2026-04-30",
                maxResults: 2
            );

        // maxResults applies to the newest-first slice, so the two retained dates are 4 and 5.
        result.Should().Contain("2026-04-04");
        result.Should().Contain("2026-04-05");
        result.Should().NotContain("2026-04-03");
    }

    [Fact]
    public async Task GetPutCallRatios_NullVolumes_RenderEmDash()
    {
        DbContext
            .Set<CboePutCallRatio>()
            .Add(
                new CboePutCallRatio
                {
                    RatioType = CboePutCallRatioType.Vix,
                    Date = new DateOnly(2026, 4, 1),
                    CallVolume = null,
                    PutVolume = null,
                    TotalVolume = null,
                    PutCallRatio = null,
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetPutCallRatios(type: "Vix", startDate: "2026-03-01", endDate: "2026-04-30");

        result.Should().Contain("2026-04-01");
        result.Should().Contain("| — | — | — | — |");
    }

    // ── GetVixHistory ────────────────────────────────────────────────────

    [Fact]
    public async Task GetVixHistory_EmptyDatabase_ReturnsNoDataMessage()
    {
        var result = await Sut().GetVixHistory();

        result.Should().Be("No VIX data found in the specified date range.");
    }

    [Fact]
    public async Task GetVixHistory_ReturnsOhlcTableSortedAscending()
    {
        var first = new CboeVixDaily
        {
            Date = new DateOnly(2026, 4, 1),
            Open = 14.20m,
            High = 15.30m,
            Low = 13.80m,
            Close = 14.95m,
        };
        var second = new CboeVixDaily
        {
            Date = new DateOnly(2026, 4, 2),
            Open = 14.95m,
            High = 16.10m,
            Low = 14.70m,
            Close = 15.55m,
        };
        DbContext.Set<CboeVixDaily>().AddRange(first, second);
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetVixHistory(startDate: "2026-03-01", endDate: "2026-04-30");

        result.Should().Contain("CBOE Volatility Index (VIX)");
        result.Should().Contain("2026-04-01");
        result.Should().Contain("14.20");
        result.Should().Contain("15.55");
        result.IndexOf("2026-04-01").Should().BeLessThan(result.IndexOf("2026-04-02"));
    }

    [Fact]
    public async Task GetVixHistory_DateRangeExcludesOutsideRows()
    {
        DbContext
            .Set<CboeVixDaily>()
            .AddRange(
                new CboeVixDaily
                {
                    Date = new DateOnly(2026, 1, 15),
                    Open = 1m,
                    High = 1m,
                    Low = 1m,
                    Close = 1m,
                },
                new CboeVixDaily
                {
                    Date = new DateOnly(2026, 4, 15),
                    Open = 20m,
                    High = 21m,
                    Low = 19m,
                    Close = 20.5m,
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetVixHistory(startDate: "2026-04-01", endDate: "2026-04-30");

        result.Should().Contain("2026-04-15");
        result.Should().NotContain("2026-01-15");
    }
}
