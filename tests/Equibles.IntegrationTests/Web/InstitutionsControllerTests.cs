using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the /institutions index page: the controller composes the per-filer
/// snapshot aggregate at the universe-latest report date and the rendered
/// HTML carries the listing with the right names, position counts, search
/// filter, and sort order.
/// </summary>
[Collection(WebHostCollection.Name)]
public class InstitutionsControllerTests
{
    private readonly WebHostFixture _fixture;

    public InstitutionsControllerTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetIndex_NoHoldings_RendersEmptyStateWithNoQuarterNotice()
    {
        await _fixture.ResetAndSeedAsync(_ => Task.CompletedTask);

        var response = await _fixture.Client.GetAsync("/institutions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("no 13F data ingested yet");
        html.Should().Contain("institutions-table");
    }

    [Fact]
    public async Task GetIndex_HoldersWithoutHoldings_ListsThemWithZeroAggregates()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(new InstitutionalHolder { Cik = "0000001", Name = "Acme Capital" });
            db.Add(new InstitutionalHolder { Cik = "0000002", Name = "Berkshire Hathaway Inc" });
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/institutions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Acme Capital");
        html.Should().Contain("Berkshire Hathaway Inc");
        // Both filers appear with zero positions since no holdings exist yet.
        html.Should().Contain(">0<");
    }

    [Fact]
    public async Task GetIndex_DefaultSort_RanksByNameAscending()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(new InstitutionalHolder { Cik = "0000001", Name = "Zebra Asset Management" });
            db.Add(new InstitutionalHolder { Cik = "0000002", Name = "Alpha Capital" });
            db.Add(new InstitutionalHolder { Cik = "0000003", Name = "Mid Fund LLC" });
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/institutions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        var alphaIdx = html.IndexOf("Alpha Capital", StringComparison.Ordinal);
        var midIdx = html.IndexOf("Mid Fund LLC", StringComparison.Ordinal);
        var zebraIdx = html.IndexOf("Zebra Asset Management", StringComparison.Ordinal);
        alphaIdx.Should().BeGreaterThan(-1);
        midIdx.Should().BeGreaterThan(alphaIdx);
        zebraIdx.Should().BeGreaterThan(midIdx);
    }

    [Fact]
    public async Task GetIndex_SortByPositionsDesc_PutsHigherPositionCountFirst()
    {
        var quarter = new DateOnly(2024, 12, 31);
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var bigId = Guid.NewGuid();
        var smallId = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = aaplId,
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                }
            );
            db.Add(
                new CommonStock
                {
                    Id = msftId,
                    Ticker = "MSFT",
                    Name = "Microsoft Corp.",
                    Cik = "0000789019",
                }
            );

            db.Add(
                new InstitutionalHolder
                {
                    Id = bigId,
                    Cik = "0000010",
                    Name = "Big Fund LP",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = smallId,
                    Cik = "0000020",
                    Name = "Small Boutique LLC",
                }
            );

            // Big Fund holds two stocks; Small Boutique holds one.
            db.Add(MakeHolding(aaplId, bigId, quarter));
            db.Add(MakeHolding(msftId, bigId, quarter));
            db.Add(MakeHolding(aaplId, smallId, quarter));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/institutions?sort=PositionsDescending");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        // Big Fund (2 positions) should appear before Small Boutique (1 position).
        var bigIdx = html.IndexOf("Big Fund LP", StringComparison.Ordinal);
        var smallIdx = html.IndexOf("Small Boutique LLC", StringComparison.Ordinal);
        bigIdx.Should().BeGreaterThan(-1);
        smallIdx.Should().BeGreaterThan(bigIdx);
    }

    [Fact]
    public async Task GetIndex_SearchByName_FiltersToMatches()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(new InstitutionalHolder { Cik = "0000001", Name = "Vanguard Group" });
            db.Add(new InstitutionalHolder { Cik = "0000002", Name = "BlackRock Inc." });
            db.Add(new InstitutionalHolder { Cik = "0000003", Name = "Capital Group" });
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/institutions?search=vanguard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Vanguard Group");
        html.Should().NotContain("BlackRock Inc.");
        html.Should().NotContain("Capital Group");
    }

    private static InstitutionalHolding MakeHolding(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate
    ) =>
        new()
        {
            CommonStockId = stockId,
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(45),
            Shares = 1_000,
            Value = 1_000_000,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber =
                $"acc-{holderId:N}".Substring(0, 12) + $"-{stockId:N}".Substring(0, 8),
        };
}
