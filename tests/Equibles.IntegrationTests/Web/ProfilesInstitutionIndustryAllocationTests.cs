using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the Institution profile's sector-allocation card end-to-end: the controller
/// joins through CommonStock.Industry, the calculator's Unclassified bucket collapses
/// nullable IndustryIds, and the view renders one row per industry plus the
/// Unclassified row when the seed contains industry-less stocks.
/// </summary>
[Collection(WebHostCollection.Name)]
public class ProfilesInstitutionIndustryAllocationTests
{
    private readonly WebHostFixture _fixture;

    public ProfilesInstitutionIndustryAllocationTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetInstitution_WithMixedIndustriesAndUnclassified_RendersAllSlices()
    {
        var holderCik = "0001500001";
        var holderId = Guid.NewGuid();
        var softwareId = Guid.NewGuid();
        var energyId = Guid.NewGuid();
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var xomId = Guid.NewGuid();
        var unclassifiedId = Guid.NewGuid();
        var reportDate = new DateOnly(2024, 12, 31);

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.AddRange(
                new Industry { Id = softwareId, Name = "Software" },
                new Industry { Id = energyId, Name = "Energy" }
            );
            db.AddRange(
                new CommonStock
                {
                    Id = aaplId,
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                    IndustryId = softwareId,
                },
                new CommonStock
                {
                    Id = msftId,
                    Ticker = "MSFT",
                    Name = "Microsoft Corp.",
                    Cik = "0000789019",
                    IndustryId = softwareId,
                },
                new CommonStock
                {
                    Id = xomId,
                    Ticker = "XOM",
                    Name = "Exxon Mobil Corp.",
                    Cik = "0000034088",
                    IndustryId = energyId,
                },
                new CommonStock
                {
                    Id = unclassifiedId,
                    Ticker = "OBSCURE",
                    Name = "Obscure Inc.",
                    Cik = "0009999999",
                    IndustryId = null,
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = holderCik,
                    Name = "Allocator LP",
                }
            );
            db.Add(MakeHolding(aaplId, holderId, reportDate, value: 2_000_000));
            db.Add(MakeHolding(msftId, holderId, reportDate, value: 1_000_000));
            db.Add(MakeHolding(xomId, holderId, reportDate, value: 500_000));
            db.Add(MakeHolding(unclassifiedId, holderId, reportDate, value: 250_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync($"/Institutions/{holderCik}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"institution-industry-allocation\"");
        html.Should().Contain("Sector allocation");
        html.Should().Contain("Software");
        html.Should().Contain("Energy");
        html.Should().Contain("Unclassified");
        html.Should().Contain("no industry"); // the badge attached to the Unclassified row

        // Software's 3M leads, then Energy's 500k, then Unclassified's 250k (always last).
        // Anchor the substring search inside the allocation card so unrelated occurrences
        // (e.g. industry names embedded in JS bundle paths) don't confuse the order check.
        var cardStart = html.IndexOf(
            "data-testid=\"institution-industry-allocation\"",
            StringComparison.Ordinal
        );
        var cardEnd = html.IndexOf("Recent holdings", cardStart, StringComparison.Ordinal);
        var card = html.Substring(cardStart, cardEnd - cardStart);
        var softwareIdx = card.IndexOf("Software", StringComparison.Ordinal);
        var energyIdx = card.IndexOf("Energy", StringComparison.Ordinal);
        var unclassifiedIdx = card.IndexOf("Unclassified", StringComparison.Ordinal);
        softwareIdx.Should().BeLessThan(energyIdx);
        energyIdx.Should().BeLessThan(unclassifiedIdx);
    }

    [Fact]
    public async Task GetInstitution_NoHoldings_DoesNotRenderAllocationCard()
    {
        var holderCik = "0001500002";
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(new InstitutionalHolder { Cik = holderCik, Name = "Empty LP" });
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync($"/Institutions/{holderCik}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().NotContain("data-testid=\"institution-industry-allocation\"");
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
            AccessionNumber = $"acc-{stockId:N}".Substring(0, 12) + $"-{reportDate:yyyyMMdd}",
        };
}
