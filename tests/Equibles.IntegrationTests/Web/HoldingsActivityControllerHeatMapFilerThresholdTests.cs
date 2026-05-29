using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the conviction-heat-map's filer-count floor: the action keeps only
/// stocks with CurrentFilerCount >= 3 (noise reduction), so a thinly-held stock
/// with 2 filers must be excluded while a 3-filer stock appears. Exercises the
/// otherwise-untested heat-map body (activity query + score computation + render).
/// </summary>
[Collection(WebHostCollection.Name)]
public class HoldingsActivityControllerHeatMapFilerThresholdTests
{
    private readonly WebHostFixture _fixture;

    public HoldingsActivityControllerHeatMapFilerThresholdTests(WebHostFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task HeatMap_StockBelowThreeFilers_ExcludedWhileQualifyingStockShown()
    {
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.AddRange(
                new CommonStock
                {
                    Id = aaplId,
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                },
                new CommonStock
                {
                    Id = msftId,
                    Ticker = "MSFT",
                    Name = "Microsoft Corp.",
                    Cik = "0000789019",
                }
            );

            var holders = new List<InstitutionalHolder>();
            for (var i = 0; i < 3; i++)
            {
                var h = new InstitutionalHolder { Cik = $"h{i}", Name = $"Filer {i}" };
                holders.Add(h);
                db.Add(h);
            }

            // Two distinct quarters so the heat map has a prior to compare against.
            db.Add(MakeHolding(aaplId, holders[0].Id, prior, 100, 180_000));
            // Current quarter: AAPL held by 3 filers (qualifies), MSFT by 2 (below floor).
            for (var i = 0; i < 3; i++)
                db.Add(MakeHolding(aaplId, holders[i].Id, current, 100, 200_000));
            for (var i = 0; i < 2; i++)
                db.Add(MakeHolding(msftId, holders[i].Id, current, 50, 100_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/holdings/conviction-heat-map");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("AAPL"); // 3 filers -> kept
        html.Should().NotContain("MSFT"); // 2 filers -> below the >=3 floor, dropped
    }

    private static InstitutionalHolding MakeHolding(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate,
        long shares,
        long value
    ) =>
        new()
        {
            CommonStockId = stockId,
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(45),
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber =
                $"acc-{stockId:N}".Substring(0, 12)
                + $"-{holderId:N}".Substring(0, 8)
                + $"-{reportDate:yyyyMMdd}",
        };
}
