using System.Net;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Adversarial end-to-end probe of the adviser name search. <c>FormAdvAdviserRepository.Search</c>
/// documents that it matches advisers whose name <i>contains</i> the term, but it splices the raw
/// term into an ILIKE pattern (<c>%{term}%</c>) without escaping LIKE metacharacters. A bare
/// underscore is a SQL LIKE single-character wildcard, so a search for "_" — a character none of
/// the seeded advisers' names contain — must return no rows under the documented contract. If "_"
/// is honoured as a wildcard it matches every non-empty name, leaking advisers the contract says
/// should be excluded (and turning "%" into a full-table dump). Driven through the real router →
/// controller → ParadeDB → Razor view; the existing q=mellon test proves non-matching advisers do
/// not otherwise appear on the page, so their presence here is the wildcard leak itself.
/// </summary>
[Collection(WebHostCollection.Name)]
public class AdvisersSearchWildcardEscapingTests
{
    private readonly WebHostFixture _fixture;

    public AdvisersSearchWildcardEscapingTests(WebHostFixture fixture) => _fixture = fixture;

    private static Task Seed(EquiblesFinancialDbContext db)
    {
        db.Add(
            new FormAdvAdviser
            {
                Crd = 231,
                LegalName = "BNY MELLON SECURITIES CORPORATION",
                PrimaryBusinessName = "BNY MELLON",
                TotalRegulatoryAum = 2_481_367_832L,
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

    [Fact(
        Skip = "GH-2905 — adviser search treats LIKE wildcards (_, %) as wildcards, not literals"
    )]
    public async Task GetAdvisers_QueryIsBareUnderscore_DoesNotWildcardMatchNamesWithoutUnderscore()
    {
        await _fixture.ResetAndSeedAsync(Seed);

        // Contract: name CONTAINS the term. Neither seeded name has a literal "_", so a correct
        // search returns nothing; an unescaped "_" wildcard would match both non-empty names.
        var response = await _fixture.Client.GetAsync("/advisers?q=_");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should()
            .NotContain(
                "BNY MELLON SECURITIES CORPORATION",
                "an unescaped '_' must not wildcard-match a name with no literal underscore"
            );
        html.Should()
            .NotContain(
                "TINY CAPITAL PARTNERS",
                "an unescaped '_' must not wildcard-match a name with no literal underscore"
            );
    }
}
