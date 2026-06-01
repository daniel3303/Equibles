using System.Net;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Adversarial end-to-end probe of the /institutions name search. <c>InstitutionalHolderRepository.Search</c>
/// splices the raw term into an ILIKE pattern (<c>%{search}%</c>) without escaping LIKE metacharacters,
/// while its sibling <c>SearchNameOrCik</c> in the same class deliberately escapes them (and documents
/// why). A bare underscore is a SQL LIKE single-character wildcard, so a search for "_" — a character
/// none of the seeded holders' names contain — must return no rows under the "name contains the term"
/// contract. If "_" is honoured as a wildcard it matches every non-empty name, leaking holders the
/// contract excludes (and turning "%" into a full-table dump). Driven through the real router →
/// controller → ParadeDB → Razor view; the existing search=vanguard&amp;state=PA test proves a
/// non-matching holder does not otherwise appear, so its presence here is the wildcard leak itself.
/// </summary>
[Collection(WebHostCollection.Name)]
public class InstitutionsSearchWildcardEscapingTests
{
    private readonly WebHostFixture _fixture;

    public InstitutionsSearchWildcardEscapingTests(WebHostFixture fixture) => _fixture = fixture;

    private static Task Seed(EquiblesFinancialDbContext db)
    {
        db.Add(
            new InstitutionalHolder
            {
                Cik = "0000001",
                Name = "Vanguard Group Inc",
                City = "Malvern",
                StateOrCountry = "PA",
            }
        );
        db.Add(
            new InstitutionalHolder
            {
                Cik = "0000002",
                Name = "Berkshire Hathaway Inc",
                City = "Omaha",
                StateOrCountry = "NE",
            }
        );
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetInstitutions_SearchIsBareUnderscore_DoesNotWildcardMatchNamesWithoutUnderscore()
    {
        await _fixture.ResetAndSeedAsync(Seed);

        // Contract: name CONTAINS the term. Neither seeded name has a literal "_", so a correct
        // search returns nothing; an unescaped "_" wildcard would match both non-empty names.
        var response = await _fixture.Client.GetAsync("/institutions?search=_");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should()
            .NotContain(
                "Vanguard Group Inc",
                "an unescaped '_' must not wildcard-match a name with no literal underscore"
            );
        html.Should()
            .NotContain(
                "Berkshire Hathaway Inc",
                "an unescaped '_' must not wildcard-match a name with no literal underscore"
            );
    }
}
