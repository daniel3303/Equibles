using Equibles.Cboe.Data.Models;
using Equibles.Cboe.Mcp.Tools;
using Equibles.Cboe.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class CboeToolsGetVixHistoryMaxResultsTests : ParadeDbMcpTestBase
{
    private CboeTools Sut() =>
        new(
            new CboePutCallRatioRepository(DbContext),
            new CboeVixDailyRepository(DbContext),
            ErrorManager,
            NullLogger<CboeTools>()
        );

    public CboeToolsGetVixHistoryMaxResultsTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // Contract: maxResults caps the result "newest first" (per the tool's parameter
    // doc). With 5 daily rows and maxResults=2 the retained dates must be the two
    // NEWEST (04-04, 04-05), never the oldest — a sibling pin exists for
    // GetPutCallRatios but not GetVixHistory, whose own .Take slice is unguarded.
    [Fact]
    public async Task GetVixHistory_MaxResults_RetainsNewestRowsNotOldest()
    {
        var rows = Enumerable
            .Range(1, 5)
            .Select(i => new CboeVixDaily
            {
                Date = new DateOnly(2026, 4, i),
                Open = 14m + i,
                High = 19m + i,
                Low = 13m + i,
                Close = 18m + i,
            });
        DbContext.Set<CboeVixDaily>().AddRange(rows);
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetVixHistory(startDate: "2026-04-01", endDate: "2026-04-30", maxResults: 2);

        result.Should().Contain("2026-04-04");
        result.Should().Contain("2026-04-05");
        result.Should().NotContain("2026-04-03");
    }
}
