using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the Institution profile (<c>/Institutions/{cik}</c>) summary header end-to-end:
/// the controller composes the calculator-driven summary, the view renders the
/// `institution-summary` card with AUM and position-count cells, and the
/// `quarters-reported` badge reflects the distinct-report-date count.
/// </summary>
[Collection(WebHostCollection.Name)]
public class ProfilesInstitutionSummaryTests
{
    private readonly WebHostFixture _fixture;

    public ProfilesInstitutionSummaryTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetInstitution_NoHoldings_RendersWithoutSummaryStrip()
    {
        var holderCik = "0001000001";
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(new InstitutionalHolder { Cik = holderCik, Name = "Empty Capital" });
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync($"/Institutions/{holderCik}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().NotContain("data-testid=\"institution-summary\"");
    }

    [Fact]
    public async Task GetInstitution_WithHoldings_RendersSummaryCardWithAumAndPositionCount()
    {
        var holderCik = "0001000002";
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var holderId = Guid.NewGuid();
        var current = new DateOnly(2024, 12, 31);
        var prior = new DateOnly(2024, 9, 30);

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
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = holderCik,
                    Name = "Big Fund LP",
                }
            );
            // Prior + current quarters.
            db.Add(MakeHolding(aaplId, holderId, prior, shares: 1_000, value: 1_000_000));
            db.Add(MakeHolding(msftId, holderId, prior, shares: 500, value: 500_000));
            db.Add(MakeHolding(aaplId, holderId, current, shares: 1_500, value: 1_500_000));
            db.Add(MakeHolding(msftId, holderId, current, shares: 500, value: 500_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync($"/Institutions/{holderCik}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"institution-summary\"");
        html.Should().Contain("Reported AUM");
        html.Should().Contain("# Positions");
        html.Should().Contain("Quarters reported: 2");
        html.Should().Contain("Top 10 concentration");
        html.Should().Contain("QoQ turnover");
        // AUM rendering depends on the runtime culture's N0 formatter (US `,`, others
        // space / NBSP / `.`); some cultures emit NBSP as the `&#xA0;` entity which
        // contains its own `0` digits, so a pure digit-strip would over-count. Anchor on
        // the `$` prefix used in the view and take digits from there, dropping anything
        // matching the NBSP entity body.
        var dollarIndex = html.IndexOf(
            "$",
            html.IndexOf("Reported AUM", StringComparison.Ordinal),
            StringComparison.Ordinal
        );
        dollarIndex.Should().BeGreaterThan(-1);
        var aumSegment = html.Substring(dollarIndex, 40).Replace("&#xA0;", " ");
        new string(aumSegment.Where(char.IsDigit).Take(7).ToArray()).Should().Be("2000000");
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
