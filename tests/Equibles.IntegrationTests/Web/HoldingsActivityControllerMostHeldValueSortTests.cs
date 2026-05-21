using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Sibling pin to <see cref="HoldingsActivityControllerMostHeldTests"/> — the
/// default <c>filers</c> arm is covered there; the <c>value</c> arm of the
/// controller's three-arm sort switch is uncovered. A regression dropping the
/// <c>SortValue</c> case would silently fall through to the default
/// <c>filers</c> arm and reorder the table. The seed inverts the rankings —
/// AAPL has the highest dollar value but the lowest filer count, MSFT the
/// opposite — so the value vs filers arms produce opposite orderings.
/// </summary>
[Collection(WebHostCollection.Name)]
public class HoldingsActivityControllerMostHeldValueSortTests
{
    private readonly WebHostFixture _fixture;

    public HoldingsActivityControllerMostHeldValueSortTests(WebHostFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task GetMostHeld_SortValueQueryParam_RanksByCurrentValueDescending()
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
            for (var i = 0; i < 4; i++)
            {
                var h = new InstitutionalHolder { Cik = $"h{i}", Name = $"Filer {i}" };
                holders.Add(h);
                db.Add(h);
            }
            // AAPL: 2 filers × $1,000,000 = $2M total (low breadth, high value).
            for (var i = 0; i < 2; i++)
                db.Add(
                    MakeHolding(aaplId, holders[i].Id, current, shares: 1_000, value: 1_000_000)
                );
            // MSFT: 4 filers × $100,000 = $400k total (high breadth, low value).
            for (var i = 0; i < 4; i++)
                db.Add(MakeHolding(msftId, holders[i].Id, current, shares: 100, value: 100_000));
            // Anchor a prior quarter so PreviousDate isn't null.
            db.Add(MakeHolding(aaplId, holders[0].Id, prior, shares: 1_000, value: 900_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Holdings/MostHeld?sort=value");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("most-held-table");
        // Value sort must list AAPL ($2M) before MSFT ($400k) — opposite of the
        // default filers arm, which would put MSFT (4 filers) first.
        var aaplIdx = html.IndexOf("AAPL", StringComparison.Ordinal);
        var msftIdx = html.IndexOf("MSFT", StringComparison.Ordinal);
        aaplIdx.Should().BeGreaterThan(-1);
        msftIdx.Should().BeGreaterThan(aaplIdx);
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
