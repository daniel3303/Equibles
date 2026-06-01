using System.Net;
using Equibles.Congress.Data.Models;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Adversarial end-to-end probe of the global search "Congress" group. The provider wraps
/// <c>CongressMemberRepository.Search</c>, which splices the raw query into an ILIKE pattern
/// (<c>%{search}%</c>) without escaping LIKE metacharacters. A bare underscore is a SQL LIKE
/// single-character wildcard, so a query of "_" — a character neither seeded member's name
/// contains — must return no Congress hits under the "name CONTAINS the term" contract that the
/// sibling <c>CongressMemberRepositoryPostgresSearchTests</c> pins as a case-insensitive substring
/// match. If "_" is honoured as a wildcard it matches every non-empty name, leaking members the
/// contract excludes (and turning "%" into a full-table dump). Driven through the real router →
/// SearchController → SearchAggregator → CongressMemberSearchProvider → ParadeDB → Razor view; the
/// global search page server-renders matched member names as hit titles, so their presence here is
/// the wildcard leak itself. Mirrors the sibling adviser/institution/stock/insider wildcard probes,
/// but on a distinct module (Congress).
/// </summary>
[Collection(WebHostCollection.Name)]
public class CongressGlobalSearchWildcardEscapingTests
{
    private readonly WebHostFixture _fixture;

    public CongressGlobalSearchWildcardEscapingTests(WebHostFixture fixture) => _fixture = fixture;

    private static Task Seed(EquiblesFinancialDbContext db)
    {
        db.Add(
            new CongressMember
            {
                Name = "Pelosi, Nancy",
                Position = CongressPosition.Representative,
            }
        );
        db.Add(
            new CongressMember { Name = "Tuberville, Tommy", Position = CongressPosition.Senator }
        );
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GlobalSearch_QueryIsBareUnderscore_DoesNotWildcardMatchMemberNamesWithoutUnderscore()
    {
        await _fixture.ResetAndSeedAsync(Seed);

        // Contract: member name CONTAINS the term. Neither seeded name has a literal "_", so a
        // correct search returns no Congress hits; an unescaped "_" wildcard matches both non-empty
        // names. category=Congress keeps the request on the results page (no exact-ticker redirect).
        var response = await _fixture.Client.GetAsync("/Search?q=_&category=Congress");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should()
            .NotContain(
                "Pelosi, Nancy",
                "an unescaped '_' must not wildcard-match a member name with no literal underscore"
            );
        html.Should()
            .NotContain(
                "Tuberville, Tommy",
                "an unescaped '_' must not wildcard-match a member name with no literal underscore"
            );
    }
}
