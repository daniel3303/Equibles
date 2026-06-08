using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the SEC filing-type filter on the /institutions index: selecting
/// Schedule 13D narrows the listed filers to those that filed a 13D, and the
/// filter control is rendered with all three types.
/// </summary>
[Collection(WebHostCollection.Name)]
public class InstitutionsControllerFilingTypeFilterTests
{
    private readonly WebHostFixture _fixture;

    public InstitutionsControllerFilingTypeFilterTests(WebHostFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task GetIndex_FilterBySchedule13D_ReturnsOnlyFilersThatFiledA13D()
    {
        var stockId = Guid.NewGuid();
        var activistId = Guid.NewGuid();
        var quantId = Guid.NewGuid();
        var reportDate = new DateOnly(2025, 4, 29);

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "QXO",
                    Name = "QXO, Inc.",
                    Cik = "0001236275",
                    Cusip = "82846H405",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = activistId,
                    Cik = "0000001",
                    Name = "Activist Capital",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = quantId,
                    Cik = "0000002",
                    Name = "Quant Fund",
                }
            );
            db.Add(
                new InstitutionalHolding
                {
                    InstitutionalHolderId = activistId,
                    CommonStockId = stockId,
                    FilingDate = reportDate,
                    ReportDate = reportDate,
                    Shares = 1_000_000,
                    Value = 10_000_000,
                    ShareType = ShareType.Shares,
                    FilingType = FilingType.Schedule13D,
                    PercentOfClass = 6.3m,
                }
            );
            db.Add(
                new InstitutionalHolding
                {
                    InstitutionalHolderId = quantId,
                    CommonStockId = stockId,
                    FilingDate = reportDate,
                    ReportDate = reportDate,
                    Shares = 500,
                    Value = 5_000,
                    ShareType = ShareType.Shares,
                    FilingType = FilingType.Form13F,
                }
            );
            await db.SaveChangesAsync();
        });

        var response = await _fixture.Client.GetAsync("/institutions?filingType=Schedule13D");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Activist Capital");
        html.Should().NotContain("Quant Fund");
    }

    [Fact]
    public async Task GetIndex_RendersFilingTypeFilterControl()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(new InstitutionalHolder { Cik = "0000001", Name = "Some Fund" });
            await db.SaveChangesAsync();
        });

        var response = await _fixture.Client.GetAsync("/institutions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("name=\"filingType\"");
        html.Should().Contain(">Schedule 13D<");
        html.Should().Contain(">Schedule 13G<");
    }
}
