using Equibles.CommonStocks.Data;
using Equibles.Data;
using Equibles.Finra.Data;
using Equibles.Finra.Repositories;
using Equibles.Media.Data;
using Equibles.Sec.Data;
using Equibles.Sec.Repositories;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Data;

/// <summary>
/// Pins the market-wide "latest date" lookups to one ORDER BY ... DESC LIMIT over the date index.
/// A DISTINCT subquery in front of the ORDER BY made Postgres scan all 14M short-volume rows,
/// so the largest-short-volume and short-interest snapshot answers timed out.
/// </summary>
public class LatestDateQueryTranslationTests
{
    private static EquiblesFinancialDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseNpgsql("Host=localhost;Database=translation-only", o => o.UseVector())
            .EnableServiceProviderCaching(false)
            .Options;
        return new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new MediaModuleConfiguration(),
                new SecModuleConfiguration(),
                new FinraModuleConfiguration(),
                new YahooModuleConfiguration(),
            }
        );
    }

    public static TheoryData<string> Lookups() =>
        [
            "daily-short-volume",
            "short-interest",
            "off-exchange-volume",
            "fails-to-deliver",
            "prices",
        ];

    [Theory]
    [MemberData(nameof(Lookups))]
    public void LatestDateLookup_IsOneOrderedProbe_NeverADistinctScan(string lookup)
    {
        using var ctx = CreateContext();
        var query = lookup switch
        {
            "daily-short-volume" => new DailyShortVolumeRepository(ctx).GetLatestDate(),
            "short-interest" => new ShortInterestRepository(ctx).GetLatestSettlementDate(),
            "off-exchange-volume" => new OffExchangeVolumeRepository(ctx).GetLatestWeek(),
            "fails-to-deliver" => new FailToDeliverRepository(ctx).GetLatestDate(),
            _ => new EquityDailyStockPriceRepository(ctx).GetLatestDateAcrossAllStocks(),
        };

        var sql = query.ToQueryString().ToUpperInvariant();

        sql.Should().NotContain("DISTINCT");
        sql.Should().Contain("DESC");
        sql.Should().Contain("LIMIT");
    }
}
