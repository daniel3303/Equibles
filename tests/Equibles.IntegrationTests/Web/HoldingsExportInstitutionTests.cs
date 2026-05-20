using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins /Holdings/Export/Institution end-to-end and the Download CSV link on the
/// Institution profile. The export reflects the same selected report date as the
/// profile page's latest snapshot.
/// </summary>
[Collection(WebHostCollection.Name)]
public class HoldingsExportInstitutionTests
{
    private readonly WebHostFixture _fixture;

    public HoldingsExportInstitutionTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ExportInstitution_UnknownCik_Returns404()
    {
        await _fixture.ResetAndSeedAsync();

        var response = await _fixture.Client.GetAsync(
            "/Holdings/Export/Institution?cik=0000000000"
        );

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExportInstitution_HappyPath_ReturnsCsvWithHoldings()
    {
        var holderId = Guid.NewGuid();
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var reportDate = new DateOnly(2024, 12, 31);

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = "0008900001",
                    Name = "Portfolio Holder",
                }
            );
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
            db.Add(MakeHolding(aaplId, holderId, reportDate, 100_000, 25_000_000));
            db.Add(MakeHolding(msftId, holderId, reportDate, 50_000, 10_000_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync(
            $"/Holdings/Export/Institution?cik=0008900001&date={reportDate:yyyy-MM-dd}"
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response
            .Content.Headers.ContentDisposition!.FileName.Should()
            .Be("0008900001-portfolio-2024-12-31.csv");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();

        var body = await response.Content.ReadAsStringAsync();
        body.Should()
            .Contain(
                "InstitutionalHolderName,InstitutionalHolderCik,ReportDate,Ticker,"
                    + "CompanyName,Shares,Value,ShareType,OptionType,AccessionNumber"
            );
        // AAPL comes first because it has the higher Value.
        body.Should().Contain("Portfolio Holder,0008900001,2024-12-31,AAPL,Apple Inc.,100000");
        body.Should().Contain("Microsoft Corp.,50000");
    }

    [Fact]
    public async Task InstitutionProfile_RendersDownloadCsvButton()
    {
        var holderId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var cik = "0008900002";
        var reportDate = new DateOnly(2024, 12, 31);

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = cik,
                    Name = "Buttoned Holder",
                }
            );
            db.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "TKR",
                    Name = "Test Inc.",
                    Cik = "0000099500",
                }
            );
            db.Add(MakeHolding(stockId, holderId, reportDate, 1, 1));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync($"/Institutions/{cik}");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("data-testid=\"institution-export-csv\"");
        html.ToLowerInvariant().Should().Contain("/holdings/export/institution");
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
