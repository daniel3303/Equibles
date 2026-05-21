using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the Holdings/MostHeld route end-to-end: the controller composes the
/// per-stock ranking from <c>GetMostHeld</c> + <c>GetUniqueFilerIds</c> and the
/// rendered HTML carries the table with the right stock order, the % of universe
/// header, and the prior-quarter delta. Each seed exercises a different facet —
/// empty state, single-quarter (no delta), and a multi-stock two-quarter ranking.
/// </summary>
[Collection(WebHostCollection.Name)]
public class HoldingsActivityControllerMostHeldTests
{
    private readonly WebHostFixture _fixture;

    public HoldingsActivityControllerMostHeldTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetMostHeld_NoHoldings_RendersEmptyState()
    {
        await _fixture.ResetAndSeedAsync(_ => Task.CompletedTask);

        var response = await _fixture.Client.GetAsync("/Holdings/MostHeld");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("No 13F data yet");
        html.Should().NotContain("most-held-table");
    }

    [Fact]
    public async Task GetMostHeld_SingleQuarter_RendersTableWithoutDeltaColumn()
    {
        var stockId = Guid.NewGuid();
        var holderId = Guid.NewGuid();
        var only = new DateOnly(2024, 12, 31);

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = "1",
                    Name = "Single Filer",
                }
            );
            db.Add(MakeHolding(stockId, holderId, only, shares: 10_000, value: 1_000_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Holdings/MostHeld");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("most-held-table");
        html.Should().Contain("AAPL");
        html.Should().Contain("no prior quarter");
    }

    [Fact]
    public async Task GetMostHeld_TwoQuartersThreeStocks_RanksByCurrentFilerCountDesc()
    {
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var nvdaId = Guid.NewGuid();
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
                },
                new CommonStock
                {
                    Id = nvdaId,
                    Ticker = "NVDA",
                    Name = "NVIDIA Corp.",
                    Cik = "0001045810",
                }
            );

            var holders = new List<InstitutionalHolder>();
            for (var i = 0; i < 5; i++)
            {
                var h = new InstitutionalHolder { Cik = $"h{i}", Name = $"Filer {i}" };
                holders.Add(h);
                db.Add(h);
            }
            // AAPL: 5 filers (most held). MSFT: 3 filers. NVDA: 1 filer.
            for (var i = 0; i < 5; i++)
                db.Add(MakeHolding(aaplId, holders[i].Id, current, shares: 100, value: 200_000));
            for (var i = 0; i < 3; i++)
                db.Add(MakeHolding(msftId, holders[i].Id, current, shares: 50, value: 100_000));
            db.Add(MakeHolding(nvdaId, holders[0].Id, current, shares: 10, value: 20_000));
            // Anchor prior quarter so PreviousDate isn't null.
            db.Add(MakeHolding(aaplId, holders[0].Id, prior, shares: 100, value: 180_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Holdings/MostHeld");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("most-held-table");
        // Order must be AAPL → MSFT → NVDA (ranked by filer count desc).
        var aaplIdx = html.IndexOf("AAPL", StringComparison.Ordinal);
        var msftIdx = html.IndexOf("MSFT", StringComparison.Ordinal);
        var nvdaIdx = html.IndexOf("NVDA", StringComparison.Ordinal);
        aaplIdx.Should().BeGreaterThan(-1);
        msftIdx.Should().BeGreaterThan(aaplIdx);
        nvdaIdx.Should().BeGreaterThan(msftIdx);
        // % of universe denominator is 5 distinct filers; AAPL → 100%, NVDA → 20%.
        // Allow both en (100.0) and de/fr/pt (100,0) decimal separators — the page
        // renders with the request's culture, not invariant.
        html.Should().MatchRegex(@"100[\.,]0%");
        html.Should().MatchRegex(@"20[\.,]0%");
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
