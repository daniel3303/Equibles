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
                Equibles.TestSupport.EquityIssuerSeed.Create(
                    Id: stockId,
                    Ticker: "QXO",
                    Name: "QXO, Inc.",
                    Cik: "0001236275",
                    Cusip: "82846H405"
                )
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
                    EquityIssuerId = stockId,
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
                    EquityIssuerId = stockId,
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

    // A Schedule 13G dated after the quarter end is one disclosed stake, not the filer's book.
    [Fact]
    public async Task GetIndex_NoFilingType_CountsEachFilersThirteenFBook()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var holderId = Guid.NewGuid();
        var quarterEnd = new DateOnly(2025, 3, 31);
        var eventDate = new DateOnly(2025, 4, 29);

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.AddRange(
                Equibles.TestSupport.EquityIssuerSeed.Create(
                    Id: firstId,
                    Ticker: "ONEX",
                    Name: "One Co.",
                    Cik: "0009700001"
                ),
                Equibles.TestSupport.EquityIssuerSeed.Create(
                    Id: secondId,
                    Ticker: "TWOX",
                    Name: "Two Co.",
                    Cik: "0009700002"
                )
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = "0009700101",
                    Name = "Index Giant",
                }
            );
            foreach (
                var (stockId, date, type) in new[]
                {
                    (firstId, quarterEnd, FilingType.Form13F),
                    (secondId, quarterEnd, FilingType.Form13F),
                    (firstId, eventDate, FilingType.Schedule13G),
                }
            )
            {
                db.Add(
                    new InstitutionalHolding
                    {
                        InstitutionalHolderId = holderId,
                        EquityIssuerId = stockId,
                        FilingDate = date,
                        ReportDate = date,
                        Shares = 1_000,
                        Value = 10_000,
                        ShareType = ShareType.Shares,
                        FilingType = type,
                    }
                );
            }
            await db.SaveChangesAsync();
        });

        var response = await _fixture.Client.GetAsync("/institutions?minPositions=2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Index Giant");
        html.Should().Contain("Mar 31, 2025");
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
