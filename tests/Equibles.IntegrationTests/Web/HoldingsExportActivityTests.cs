using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins /Holdings/Export/Activity end-to-end and the Download CSV link on the
/// Holdings/Activity page. The export bundles all four boards (TopBuys / TopSells /
/// NewPositions / SoldOutPositions) into a single CSV keyed by the selected report date.
/// </summary>
[Collection(WebHostCollection.Name)]
public class HoldingsExportActivityTests
{
    private readonly WebHostFixture _fixture;

    public HoldingsExportActivityTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ExportActivity_OnlyOneQuarter_Returns404()
    {
        var stockId = Guid.NewGuid();
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "ONE",
                    Name = "One Quarter Co.",
                    Cik = "0000700001",
                }
            );
            var holder = new InstitutionalHolder { Cik = "0009000010", Name = "Solo" };
            db.Add(holder);
            db.Add(MakeHoldingById(stockId, holder.Id, new DateOnly(2024, 12, 31), 100, 100));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Holdings/Export/Activity");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExportActivity_HappyPath_BundlesAllFourBoardsIntoOneCsv()
    {
        var q1 = new DateOnly(2024, 9, 30);
        var q2 = new DateOnly(2024, 12, 31);
        var holderA = Guid.NewGuid();
        var holderB = Guid.NewGuid();
        var buyerStock = Guid.NewGuid();
        var sellerStock = Guid.NewGuid();
        var newStock = Guid.NewGuid();
        var soldStock = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.AddRange(
                new InstitutionalHolder
                {
                    Id = holderA,
                    Cik = "0009000020",
                    Name = "Holder A",
                },
                new InstitutionalHolder
                {
                    Id = holderB,
                    Cik = "0009000021",
                    Name = "Holder B",
                }
            );
            db.AddRange(
                new CommonStock
                {
                    Id = buyerStock,
                    Ticker = "BUYR",
                    Name = "Buyer Co.",
                    Cik = "0007710001",
                },
                new CommonStock
                {
                    Id = sellerStock,
                    Ticker = "SELL",
                    Name = "Seller Co.",
                    Cik = "0007710002",
                },
                new CommonStock
                {
                    Id = newStock,
                    Ticker = "NEW1",
                    Name = "Newcomer Co.",
                    Cik = "0007710003",
                },
                new CommonStock
                {
                    Id = soldStock,
                    Ticker = "OUT1",
                    Name = "Exited Co.",
                    Cik = "0007710004",
                }
            );

            // Buyer: holder A holds 100, then 200 (Δ shares +100, Δ value +50).
            db.Add(MakeHoldingById(buyerStock, holderA, q1, 100, 50));
            db.Add(MakeHoldingById(buyerStock, holderA, q2, 200, 100));
            // Seller: holder A holds 500 then 100 (Δ shares -400, Δ value -40).
            db.Add(MakeHoldingById(sellerStock, holderA, q1, 500, 50));
            db.Add(MakeHoldingById(sellerStock, holderA, q2, 100, 10));
            // Newcomer: holder B first appears in Q2 only.
            db.Add(MakeHoldingById(newStock, holderB, q2, 1, 1));
            // Exited: holder B appears in Q1 only.
            db.Add(MakeHoldingById(soldStock, holderB, q1, 1, 1));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync(
            $"/Holdings/Export/Activity?date={q2:yyyy-MM-dd}"
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response
            .Content.Headers.ContentDisposition!.FileName.Should()
            .Be("13F-activity-2024-12-31.csv");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();

        var body = await response.Content.ReadAsStringAsync();
        body.Should()
            .Contain(
                "Board,ReportDate,ComparisonDate,Ticker,CompanyName,CurrentFilerCount,"
                    + "PreviousFilerCount,DeltaShares,DeltaValue,NewFilerCount,SoldOutFilerCount"
            );
        body.Should().Contain("TopBuys,2024-12-31,2024-09-30,BUYR,Buyer Co.");
        body.Should().Contain("TopSells,2024-12-31,2024-09-30,SELL,Seller Co.");
        body.Should().Contain("NewPositions,2024-12-31,2024-09-30,NEW1,Newcomer Co.");
        body.Should().Contain("SoldOutPositions,2024-12-31,2024-09-30,OUT1,Exited Co.");
    }

    [Fact]
    public async Task ActivityPage_RendersDownloadCsvButton_WhenPriorQuarterExists()
    {
        var q1 = new DateOnly(2024, 9, 30);
        var q2 = new DateOnly(2024, 12, 31);
        var holderId = Guid.NewGuid();
        var stockId = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = "0009000030",
                    Name = "Test",
                }
            );
            db.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "TKB",
                    Name = "Button Co.",
                    Cik = "0007777001",
                }
            );
            db.Add(MakeHoldingById(stockId, holderId, q1, 1, 1));
            db.Add(MakeHoldingById(stockId, holderId, q2, 2, 2));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Holdings/Activity");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("data-testid=\"activity-export-csv\"");
        html.ToLowerInvariant().Should().Contain("/holdings/export/activity");
    }

    private static InstitutionalHolding MakeHoldingById(
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
            AccessionNumber = $"acc-{stockId:N}".Substring(0, 12) + $"-{reportDate:yyyyMMdd}",
        };
}
