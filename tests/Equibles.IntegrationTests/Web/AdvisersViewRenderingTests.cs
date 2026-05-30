using System.Net;
using System.Text.Encodings.Web;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Renders the investment-advisers browse and detail views end-to-end through the real router and
/// Razor pipeline. Pins the populated path: a seeded adviser appears in the list with a resolved
/// <c>asp-action="Show"</c> link and formatted AUM, the name search filters the list, and the
/// detail page surfaces the firm's profile. An unknown CRD must 404 rather than render an empty
/// page.
/// </summary>
[Collection(WebHostCollection.Name)]
public class AdvisersViewRenderingTests
{
    private readonly WebHostFixture _fixture;

    public AdvisersViewRenderingTests(WebHostFixture fixture) => _fixture = fixture;

    private static Task Seed(EquiblesFinancialDbContext db)
    {
        db.Add(
            new FormAdvAdviser
            {
                Crd = 231,
                SecNumber = "801-54739",
                LegalName = "BNY MELLON SECURITIES CORPORATION",
                PrimaryBusinessName = "BNY MELLON",
                MainOfficeCity = "NEW YORK",
                MainOfficeState = "NY",
                MainOfficeCountry = "United States",
                NumberOfEmployees = 333,
                TotalRegulatoryAum = 2_481_367_832L,
                DiscretionaryAum = 829_845_109L,
                ChargesPercentageOfAum = true,
                ReportDate = new DateOnly(2022, 4, 1),
            }
        );
        db.Add(
            new FormAdvAdviser
            {
                Crd = 777,
                LegalName = "TINY CAPITAL PARTNERS",
                TotalRegulatoryAum = 5_000_000L,
                ReportDate = new DateOnly(2022, 4, 1),
            }
        );
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetAdvisers_WithSeededAdvisers_RendersRowWithResolvedLinkAndAum()
    {
        await _fixture.ResetAndSeedAsync(Seed);

        var response = await _fixture.Client.GetAsync("/advisers");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("BNY MELLON SECURITIES CORPORATION");
        html.Should()
            .Contain(
                "/advisers/231",
                "asp-action=\"Show\" must resolve to the (lowercased) Show route, not an empty href"
            );
        var expectedAum = HtmlEncoder.Default.Encode(2_481_367_832L.ToString("N0"));
        html.Should().Contain(expectedAum, "the total regulatory AUM must render formatted N0");
    }

    [Fact]
    public async Task GetAdvisers_WithNameQuery_FiltersTheList()
    {
        await _fixture.ResetAndSeedAsync(Seed);

        var response = await _fixture.Client.GetAsync("/advisers?q=mellon");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("BNY MELLON SECURITIES CORPORATION");
        html.Should()
            .NotContain("TINY CAPITAL PARTNERS", "the search term excludes non-matching advisers");
    }

    [Fact]
    public async Task GetAdviser_KnownCrd_RendersProfile()
    {
        await _fixture.ResetAndSeedAsync(Seed);

        var response = await _fixture.Client.GetAsync("/advisers/231");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("BNY MELLON SECURITIES CORPORATION");
        html.Should().Contain("801-54739");
        html.Should().Contain("Percentage of AUM");
    }

    [Fact]
    public async Task GetAdviser_UnknownCrd_Returns404()
    {
        await _fixture.ResetAndSeedAsync(Seed);

        var response = await _fixture.Client.GetAsync("/advisers/999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
