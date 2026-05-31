using System.Net;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Adversarial end-to-end probe of the /institutions city location filter. The
/// controller splices the raw city term into an ILIKE pattern (<c>%{city.Trim()}%</c>)
/// without escaping LIKE metacharacters, while the sibling
/// <c>InstitutionalHolderRepository.SearchNameOrCik</c> deliberately escapes them
/// (and documents why "50%" would otherwise match every row). A bare underscore is
/// a SQL LIKE single-character wildcard, so a city filter of "_" — a character none
/// of the seeded holders' cities contain — must return no rows under the
/// "city contains the term" contract. If "_" is honoured as a wildcard it matches
/// every non-empty city, leaking holders the contract excludes (and turning "%" into
/// a full-table dump). Driven through the real router -> controller -> ParadeDB ->
/// Razor view; the existing city=new test proves a non-matching holder does not
/// otherwise appear, so its presence here is the wildcard leak itself.
/// </summary>
[Collection(WebHostCollection.Name)]
public class InstitutionsControllerCityFilterWildcardTests
{
    private readonly WebHostFixture _fixture;

    public InstitutionsControllerCityFilterWildcardTests(WebHostFixture fixture) =>
        _fixture = fixture;

    private static Task Seed(EquiblesFinancialDbContext db)
    {
        db.Add(
            new InstitutionalHolder
            {
                Cik = "0000001",
                Name = "Gotham Asset Management",
                City = "New York",
                StateOrCountry = "NY",
            }
        );
        db.Add(
            new InstitutionalHolder
            {
                Cik = "0000002",
                Name = "Bay Area Partners",
                City = "San Francisco",
                StateOrCountry = "CA",
            }
        );
        return Task.CompletedTask;
    }

    [Fact(Skip = "GH-2913: institution city filter treats _ or % as LIKE wildcards")]
    public async Task GetInstitutions_CityIsBareUnderscore_DoesNotWildcardMatchCitiesWithoutUnderscore()
    {
        await _fixture.ResetAndSeedAsync(Seed);

        // Contract: city CONTAINS the term. Neither seeded city has a literal "_", so a correct
        // filter returns nothing; an unescaped "_" wildcard would match both non-empty cities.
        var response = await _fixture.Client.GetAsync("/institutions?city=_");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should()
            .NotContain(
                "Gotham Asset Management",
                "an unescaped '_' must not wildcard-match a city with no literal underscore"
            );
        html.Should()
            .NotContain(
                "Bay Area Partners",
                "an unescaped '_' must not wildcard-match a city with no literal underscore"
            );
    }
}
