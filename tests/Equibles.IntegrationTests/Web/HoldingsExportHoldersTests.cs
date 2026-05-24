using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins /Holdings/Export/Holders end-to-end: route → controller → repository →
/// CsvExportService. Asserts content-type, filename, header row, RFC-4180 escaping for
/// holder names with commas, and the Download CSV link rendering on the Holdings tab.
/// </summary>
[Collection(WebHostCollection.Name)]
public class HoldingsExportHoldersTests
{
    private readonly WebHostFixture _fixture;

    public HoldingsExportHoldersTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ExportHolders_UnknownTicker_Returns404()
    {
        await _fixture.ResetAndSeedAsync();

        var response = await _fixture.Client.GetAsync("/Holdings/Export/Holders?ticker=ZZZZ");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExportHolders_NoHoldings_Returns404()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Ticker = "EMPT",
                    Name = "Empty Co.",
                    Cik = "0000099001",
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Holdings/Export/Holders?ticker=EMPT");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExportHolders_HappyPath_ReturnsCsvWithHeaderAndHolderRow()
    {
        var stockId = Guid.NewGuid();
        var holderId = Guid.NewGuid();
        var reportDate = new DateOnly(2024, 12, 31);

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
                    Cik = "0001067983",
                    Name = "Berkshire Hathaway",
                }
            );
            db.Add(MakeHolding(stockId, holderId, reportDate, 1_500_000, 250_000_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync(
            $"/Holdings/Export/Holders?ticker=AAPL&date={reportDate:yyyy-MM-dd}"
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response
            .Content.Headers.ContentDisposition!.FileName.Should()
            .Be("AAPL-13F-2024-12-31.csv");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();

        var body = await response.Content.ReadAsStringAsync();
        body.Should()
            .Contain(
                "Ticker,CompanyName,ReportDate,InstitutionalHolderName,InstitutionalHolderCik,"
                    + "PositionChange,CurrentShares,PreviousShares,DeltaShares,ChangePercent,"
                    + "OwnershipPercent,CurrentValue,DeltaValue,QuarterFirstOwned"
            );
        body.Should().Contain("AAPL,Apple Inc.,2024-12-31,Berkshire Hathaway,0001067983,");
        body.Should().Contain("1500000");
        body.Should().Contain("250000000");
    }

    [Fact]
    public async Task ExportHolders_HolderNameWithComma_IsQuotedPerRfc4180()
    {
        var stockId = Guid.NewGuid();
        var holderId = Guid.NewGuid();
        var reportDate = new DateOnly(2024, 12, 31);

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "MSFT",
                    Name = "Microsoft Corp.",
                    Cik = "0000789019",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = "0009999999",
                    // Name contains a comma — the writer must wrap in quotes.
                    Name = "Apex, Capital LLC",
                }
            );
            db.Add(MakeHolding(stockId, holderId, reportDate, 1, 1));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync(
            $"/Holdings/Export/Holders?ticker=MSFT&date={reportDate:yyyy-MM-dd}"
        );
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"Apex, Capital LLC\"");
    }

    [Fact]
    public async Task HoldingsTab_RendersDownloadCsvButton()
    {
        var stockId = Guid.NewGuid();
        var holderId = Guid.NewGuid();
        var reportDate = new DateOnly(2024, 12, 31);

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "NVDA",
                    Name = "NVIDIA Corp.",
                    Cik = "0001045810",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = "0001000099",
                    Name = "Test Holder",
                }
            );
            db.Add(MakeHolding(stockId, holderId, reportDate, 1, 1));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Stocks/NVDA/Holdings");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("data-testid=\"holdings-export-csv\"");
        html.ToLowerInvariant().Should().Contain("/holdings/export/holders");
    }

    [Fact]
    public async Task ExportHolders_MultipleHolders_OrdersByValueDescending()
    {
        // The Holders export orders rows by Value desc. None of the other tests exercise
        // multi-holder ordering — a regression flipping the sort direction would slip
        // past every .Contain assertion. WhaleFund's $5M position must appear before
        // MinnowFund's $1M position in the CSV body.
        var stockId = Guid.NewGuid();
        var bigHolderId = Guid.NewGuid();
        var smallHolderId = Guid.NewGuid();
        var reportDate = new DateOnly(2024, 12, 31);

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "GOOG",
                    Name = "Alphabet Inc.",
                    Cik = "0001652044",
                }
            );
            db.AddRange(
                new InstitutionalHolder
                {
                    Id = bigHolderId,
                    Cik = "0009800001",
                    Name = "WhaleFund",
                },
                new InstitutionalHolder
                {
                    Id = smallHolderId,
                    Cik = "0009800002",
                    Name = "MinnowFund",
                }
            );
            db.Add(MakeHolding(stockId, bigHolderId, reportDate, 50_000, 5_000_000));
            db.Add(MakeHolding(stockId, smallHolderId, reportDate, 10_000, 1_000_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync(
            $"/Holdings/Export/Holders?ticker=GOOG&date={reportDate:yyyy-MM-dd}"
        );
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var whaleIndex = body.IndexOf("WhaleFund", StringComparison.Ordinal);
        var minnowIndex = body.IndexOf("MinnowFund", StringComparison.Ordinal);
        whaleIndex.Should().BeGreaterThanOrEqualTo(0);
        minnowIndex.Should().BeGreaterThanOrEqualTo(0);
        whaleIndex
            .Should()
            .BeLessThan(minnowIndex, "rows are ordered by Value desc — $5M outranks $1M");
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
            AccessionNumber = $"acc-{stockId:N}".Substring(0, 12) + $"-{reportDate:yyyyMMdd}",
        };
}
