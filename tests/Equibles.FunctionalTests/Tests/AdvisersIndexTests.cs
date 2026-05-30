using Equibles.FunctionalTests.Fixtures;
using Equibles.Sec.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class AdvisersIndexTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public AdvisersIndexTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    private static Task SeedBnyMellon(Equibles.Data.EquiblesFinancialDbContext db)
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
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Index_GetWithSeededAdviser_RendersHeaderAndAdviserName()
    {
        await _web.ResetAndSeedAsync(SeedBnyMellon);
        var page = await _playwright.NewPageAsync(_web.BaseUrl);

        var response = await page.GotoAsync("/advisers");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Investment Advisers");
        await Assertions
            .Expect(page.Locator("body"))
            .ToContainTextAsync("BNY MELLON SECURITIES CORPORATION");
    }

    [Fact]
    public async Task Index_ClickAdviser_NavigatesToProfileWithAumAndSecNumber()
    {
        await _web.ResetAndSeedAsync(SeedBnyMellon);
        var page = await _playwright.NewPageAsync(_web.BaseUrl);

        await page.GotoAsync("/advisers");
        await page.GetByRole(AriaRole.Link, new() { Name = "BNY MELLON SECURITIES CORPORATION" })
            .First.ClickAsync();

        await page.WaitForURLAsync("**/advisers/231");
        await Assertions.Expect(page.Locator("body")).ToContainTextAsync("801-54739");
        await Assertions.Expect(page.Locator("body")).ToContainTextAsync("Percentage of AUM");
    }
}
