using Equibles.Cftc.Data.Models;
using Equibles.Cftc.Mcp.Tools;
using Equibles.Cftc.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class CftcToolsGetCftcPositioningNegativeMaxResultsTests : ParadeDbMcpTestBase
{
    public CftcToolsGetCftcPositioningNegativeMaxResultsTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private CftcTools Sut() =>
        new(
            new CftcContractRepository(DbContext),
            new CftcPositionReportRepository(DbContext),
            ErrorManager,
            NullLogger<CftcTools>()
        );

    [Fact(
        Skip = "GH-2939 — negative maxResults becomes a negative SQL LIMIT and surfaces the internal-error sentinel"
    )]
    public async Task GetCftcPositioning_NegativeMaxResults_DegradesGracefullyWithoutInternalError()
    {
        // Contract: maxResults is documented as "Maximum number of reports to return" — a
        // ceiling on the row count, so a non-positive client value can only ever mean
        // "return no rows". The tool must therefore degrade gracefully (its own no-data
        // path already returns a friendly message), never surface the generic internal-
        // failure sentinel. Sibling MCP tools route maxResults through a guard that yields
        // zero rows on a non-positive value (GH-2931); GetCftcPositioning instead passes it
        // straight into EF Core's .Take(maxResults), so a negative value becomes a negative
        // SQL LIMIT that PostgreSQL rejects and the caller sees an internal error.
        var crude = new CftcContract
        {
            MarketCode = "067651",
            MarketName = "CRUDE OIL, LIGHT SWEET-WTI",
            Category = CftcContractCategory.Energy,
        };
        DbContext.Set<CftcContract>().Add(crude);
        DbContext
            .Set<CftcPositionReport>()
            .Add(
                new CftcPositionReport
                {
                    CftcContract = crude,
                    CftcContractId = crude.Id,
                    ReportDate = new DateOnly(2026, 4, 1),
                    OpenInterest = 1_500_000,
                    CommLong = 900_000,
                    CommShort = 40_000,
                    NonCommLong = 30_000,
                    NonCommShort = 25_000,
                    NonCommSpreads = 5_000,
                    TotalRptLong = 930_000,
                    TotalRptShort = 65_000,
                    NonRptLong = 5_000,
                    NonRptShort = 5_000,
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetCftcPositioning(
                "067651",
                startDate: "2026-03-01",
                endDate: "2026-04-30",
                maxResults: -1
            );

        result
            .Should()
            .NotContain(
                "An error occurred while executing GetCftcPositioning",
                "a client-supplied maxResults must never surface the internal-error sentinel"
            );
    }
}
