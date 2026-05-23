using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

[Collection(WebHostCollection.Name)]
public class ProfilesOverlapMatrixCommaSeparatedCiksTests
{
    private readonly WebHostFixture _fixture;

    public ProfilesOverlapMatrixCommaSeparatedCiksTests(WebHostFixture fixture) =>
        _fixture = fixture;

    // The OverlapMatrix action splits CIKs on comma AND space so users can
    // paste a comma-separated list into a single query parameter
    // (?ciks=A,B) instead of repeating ?ciks=A&ciks=B. The sibling test
    // passes separate params; this pins the comma-split path. A refactor
    // that removes the SelectMany(Split) would silently pass the sibling
    // test but break clipboard-paste usage — the full "A,B" string would
    // fail lookup and the page would render "no data."
    [Fact]
    public async Task GetOverlapMatrix_CommaSeparatedCiksInSingleParam_RendersMatrixTable()
    {
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var fundACik = "0003100001";
        var fundBCik = "0003100002";
        var fundAId = Guid.NewGuid();
        var fundBId = Guid.NewGuid();
        var reportDate = new DateOnly(2024, 12, 31);

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
            db.AddRange(
                new InstitutionalHolder
                {
                    Id = fundAId,
                    Cik = fundACik,
                    Name = "Comma Fund A LP",
                },
                new InstitutionalHolder
                {
                    Id = fundBId,
                    Cik = fundBCik,
                    Name = "Comma Fund B LP",
                }
            );
            db.Add(MakeHolding(aaplId, fundAId, reportDate, value: 1_000_000));
            db.Add(MakeHolding(aaplId, fundBId, reportDate, value: 800_000));
            await Task.CompletedTask;
        });

        // Single ciks= param with comma-separated values, not &ciks=A&ciks=B
        var response = await _fixture.Client.GetAsync(
            $"/Institutions/Overlap?ciks={fundACik},{fundBCik}"
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"overlap-matrix-table\"");
        html.Should().Contain("Comma Fund A LP");
        html.Should().Contain("Comma Fund B LP");
    }

    private static InstitutionalHolding MakeHolding(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate,
        long value
    ) =>
        new()
        {
            CommonStockId = stockId,
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(45),
            Shares = value / 100,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{stockId:N}"[..12] + $"-{reportDate:yyyyMMdd}",
        };
}
