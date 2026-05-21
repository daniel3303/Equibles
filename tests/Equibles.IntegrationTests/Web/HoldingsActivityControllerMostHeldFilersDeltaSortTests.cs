using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Sibling pin to <see cref="HoldingsActivityControllerMostHeldTests"/>
/// (default filers arm) and <see cref="HoldingsActivityControllerMostHeldValueSortTests"/>
/// (value arm). The <c>filersDelta</c> arm of the controller's three-arm sort
/// switch is the QoQ heat-map view; without an explicit pin, a regression
/// dropping the <c>SortFilersDelta</c> case would fall through to the default
/// <c>filers</c> arm and silently swap the warmest-stock-first ranking for
/// the highest-breadth-first ranking. The seed makes the two arms disagree:
/// AAPL has the higher absolute filer count (5) but a smaller delta (+1);
/// MSFT has fewer filers (5) overall but a much larger delta (+4).
/// </summary>
[Collection(WebHostCollection.Name)]
public class HoldingsActivityControllerMostHeldFilersDeltaSortTests
{
    private readonly WebHostFixture _fixture;

    public HoldingsActivityControllerMostHeldFilersDeltaSortTests(WebHostFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task GetMostHeld_SortFilersDeltaQueryParam_RanksByDeltaFilersDescending()
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
            for (var i = 0; i < 6; i++)
            {
                var h = new InstitutionalHolder { Cik = $"h{i}", Name = $"Filer {i}" };
                holders.Add(h);
                db.Add(h);
            }
            // AAPL: prior 5 filers → current 6 filers (Δ +1; high absolute count, small delta).
            for (var i = 0; i < 5; i++)
                db.Add(MakeHolding(aaplId, holders[i].Id, prior, shares: 100, value: 100_000));
            for (var i = 0; i < 6; i++)
                db.Add(MakeHolding(aaplId, holders[i].Id, current, shares: 100, value: 100_000));
            // MSFT: prior 1 filer → current 5 filers (Δ +4; smaller count, much larger delta).
            db.Add(MakeHolding(msftId, holders[0].Id, prior, shares: 50, value: 50_000));
            for (var i = 0; i < 5; i++)
                db.Add(MakeHolding(msftId, holders[i].Id, current, shares: 50, value: 50_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Holdings/MostHeld?sort=filersDelta");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("most-held-table");
        // filersDelta sort: MSFT (Δ +4) must precede AAPL (Δ +1) — opposite of
        // the default filers arm, which would put AAPL (6 filers) first.
        var aaplIdx = html.IndexOf("AAPL", StringComparison.Ordinal);
        var msftIdx = html.IndexOf("MSFT", StringComparison.Ordinal);
        msftIdx.Should().BeGreaterThan(-1);
        aaplIdx.Should().BeGreaterThan(msftIdx);
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
