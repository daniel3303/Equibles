using System.Globalization;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Mcp.Tools;
using Equibles.Finra.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Adversarial cover for <c>GetShortInterestSnapshot</c>'s no-results message. The message echoes
/// the caller's <c>minDaysToCover</c> threshold, and MCP markdown must render byte-identically on
/// every host locale (the established repo contract behind the sibling culture-invariance pins and
/// the InvariantCulture call sites). The threshold is interpolated as a bare decimal, which honours
/// the thread CurrentCulture — same defect class as GH-3058 (GetLargestShortVolume's no-results
/// minShortVolume), but on a different tool.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ShortDataToolsGetShortInterestSnapshotNoResultsCultureInvarianceTests
    : ParadeDbMcpTestBase
{
    private ShortDataTools Sut() =>
        new(
            new DailyShortVolumeRepository(DbContext),
            new ShortInterestRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<ShortDataTools>()
        );

    public ShortDataToolsGetShortInterestSnapshotNoResultsCultureInvarianceTests(
        ParadeDbFixture fixture
    )
        : base(fixture) { }

    [Fact(
        Skip = "GH-3068 — GetShortInterestSnapshot renders the no-results minDaysToCover threshold with host-locale separators, forking MCP output by culture"
    )]
    public async Task GetShortInterestSnapshot_NoResultsUnderNonInvariantCulture_RendersThresholdCultureInvariantly()
    {
        var stock = new CommonStock
        {
            Ticker = "GME",
            Name = "GameStop Corp",
            Cik = "0001326380",
        };
        DbContext.Set<CommonStock>().Add(stock);
        // One record at the latest settlement date with a low days-to-cover, so the
        // minDaysToCover filter below excludes it and the no-results branch is taken.
        DbContext
            .Set<ShortInterest>()
            .Add(
                new ShortInterest
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    SettlementDate = new DateOnly(2026, 3, 15),
                    CurrentShortPosition = 1_000,
                    PreviousShortPosition = 1_000,
                    ChangeInShortPosition = 0,
                    AverageDailyVolume = 100_000,
                    DaysToCover = 1.0m,
                }
            );
        await DbContext.SaveChangesAsync();

        var previous = CultureInfo.CurrentCulture;
        string result;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            result = await Sut().GetShortInterestSnapshot(minDaysToCover: 2.5m);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // The echoed threshold must use the invariant decimal point on every host locale;
        // de-DE would fork it to "2,5".
        result.Should().Contain("days to cover >= 2.5");
        result.Should().NotContain("2,5");
    }
}
