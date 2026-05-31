using Equibles.FunctionalTests.Fixtures;
using Equibles.Sec.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class AdvisersIndexPageOverflowTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public AdvisersIndexPageOverflowTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact(Skip = "GH-2924 — max-int page overflows Page() offset to negative, 500s")]
    public async Task Index_GetWithMaxIntPage_DoesNotReturnServerError()
    {
        // Contract: Pagination.ClampPage exists precisely so a client-supplied page value can
        // never surface as HTTP 500 — its doc-comment states a bad page "yields a negative
        // OFFSET ... which PostgreSQL rejects (22023) and surfaces as HTTP 500", and guards it.
        // ClampPage only floors page < 1. A maximal positive page (int.MaxValue) binds fine,
        // survives the floor unchanged, then overflows Page()'s (page-1)*pageSize to a negative
        // OFFSET — the exact failure mode ClampPage promises to prevent. So no page value should
        // ever produce a 5xx.
        await _web.ResetAndSeedAsync(SeedOneAdviser);
        var page = await _playwright.NewPageAsync(_web.BaseUrl);

        var response = await page.GotoAsync("/advisers?page=2147483647");

        response.Should().NotBeNull();
        response!
            .Status.Should()
            .BeLessThan(500, "a client-supplied page value must never cause a server error");
    }

    private static Task SeedOneAdviser(Equibles.Data.EquiblesFinancialDbContext db)
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
}
