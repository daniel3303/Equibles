using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the /Holdings/Screener/Export.csv route end-to-end: route → controller →
/// repository → CSV writer. Asserts content-type, filename, header row, and that the
/// same filters that apply to the form also apply to the export.
/// </summary>
[Collection(WebHostCollection.Name)]
public class HoldingsScreenerExportCsvTests
{
    private readonly WebHostFixture _fixture;

    public HoldingsScreenerExportCsvTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ExportCsv_WithLessThanTwoQuarters_Returns404()
    {
        await _fixture.ResetAndSeedAsync();

        var response = await _fixture.Client.GetAsync("/Holdings/Screener/Export.csv");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExportCsv_TwoQuartersOfHoldings_ReturnsCsvWithHeaderAndDataRow()
    {
        var q1 = new DateOnly(2024, 9, 30);
        var q2 = new DateOnly(2024, 12, 31);
        var holderId = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = "0008000001",
                    Name = "Export Holder",
                }
            );
            var stock = new CommonStock
            {
                Ticker = "EXPT",
                Name = "Export Corp.",
                Cik = "0000099991",
            };
            db.Add(stock);
            db.Add(MakeHolding(stock.Id, holderId, q1, 100, 100_000));
            db.Add(MakeHolding(stock.Id, holderId, q2, 150, 150_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Holdings/Screener/Export.csv");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition!.FileName.Should().EndWith(".csv");
        var body = await response.Content.ReadAsStringAsync();
        body.Should()
            .Contain(
                "Ticker,Name,Industry,CurrentFilerCount,PreviousFilerCount,DeltaFilerCount,"
                    + "CurrentValue,PreviousValue,DeltaValue,NewFilerCount,SoldOutFilerCount,PercentOfFloat"
            );
        body.Should().Contain("EXPT,Export Corp.,");
        // Δ value = 150_000 - 100_000 = 50_000 — culture-invariant integer with no separator.
        body.Should().Contain("50000");
    }

    [Fact]
    public async Task ExportCsv_NameWithComma_IsQuotedAndDoubleQuotesAreEscaped()
    {
        var q1 = new DateOnly(2024, 9, 30);
        var q2 = new DateOnly(2024, 12, 31);
        var holderId = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = "0008000002",
                    Name = "Comma Holder",
                }
            );
            var stock = new CommonStock
            {
                Ticker = "CMA",
                // Company name contains a comma + a double quote — both must be escaped.
                Name = "Acme, \"Special\" Co.",
                Cik = "0000099992",
            };
            db.Add(stock);
            db.Add(MakeHolding(stock.Id, holderId, q1, 1, 1));
            db.Add(MakeHolding(stock.Id, holderId, q2, 1, 1));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Holdings/Screener/Export.csv");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Field gets wrapped in quotes; embedded double-quote is doubled per RFC 4180.
        body.Should().Contain("\"Acme, \"\"Special\"\" Co.\"");
    }

    [Fact]
    public async Task ExportCsv_NameWithNewline_IsQuotedToPreserveRowStructure()
    {
        // RFC 4180 says: wrap a field in quotes when it contains a quote, comma, OR newline.
        // The existing comma/quote pin would still pass if `\n` were dropped from the escape
        // trigger; an unwrapped newline silently splits the row in two for downstream parsers.
        var q1 = new DateOnly(2024, 9, 30);
        var q2 = new DateOnly(2024, 12, 31);
        var holderId = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = "0008000004",
                    Name = "Newline Holder",
                }
            );
            var stock = new CommonStock
            {
                Ticker = "NLN",
                Name = "Acme\nNew Line Inc.",
                Cik = "0000099995",
            };
            db.Add(stock);
            db.Add(MakeHolding(stock.Id, holderId, q1, 1, 1));
            db.Add(MakeHolding(stock.Id, holderId, q2, 1, 1));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Holdings/Screener/Export.csv");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"Acme\nNew Line Inc.\"");
    }

    [Fact]
    public async Task ExportCsv_FiltersApplyToDownloadSameAsForm()
    {
        var q1 = new DateOnly(2024, 9, 30);
        var q2 = new DateOnly(2024, 12, 31);
        var holderId = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = "0008000003",
                    Name = "Filter Export Holder",
                }
            );
            var big = new CommonStock
            {
                Ticker = "EXBIG",
                Name = "Export Big",
                Cik = "0000099993",
            };
            var small = new CommonStock
            {
                Ticker = "EXSML",
                Name = "Export Small",
                Cik = "0000099994",
            };
            db.AddRange(big, small);
            db.Add(MakeHolding(big.Id, holderId, q1, 1, 9_000_000));
            db.Add(MakeHolding(big.Id, holderId, q2, 1, 10_000_000));
            db.Add(MakeHolding(small.Id, holderId, q1, 1, 1_000));
            db.Add(MakeHolding(small.Id, holderId, q2, 1, 1_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync(
            "/Holdings/Screener/Export.csv?MinTotalValue=5000000"
        );
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("EXBIG");
        body.Should().NotContain("EXSML");
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
