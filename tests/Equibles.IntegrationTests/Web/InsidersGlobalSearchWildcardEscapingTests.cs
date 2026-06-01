using System.Net;
using Equibles.Data;
using Equibles.InsiderTrading.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Adversarial end-to-end probe of the global search "Insiders" group. The provider wraps
/// <c>InsiderOwnerRepository.Search</c>, which splits the query into tokens and splices each raw
/// token into an ILIKE pattern (<c>%{token}%</c>) without escaping LIKE metacharacters. A bare
/// underscore is a SQL LIKE single-character wildcard, so a query of "_" — a character none of the
/// seeded owners' names contain — must return no insider hits under the "name CONTAINS the term"
/// contract. If "_" is honoured as a wildcard it matches every non-empty name, leaking insiders the
/// contract excludes (and turning "%" into a full-table dump). Driven through the real router →
/// SearchController → SearchAggregator → InsiderOwnerSearchProvider → ParadeDB → Razor view; the
/// global search page server-renders matched owner names as hit titles, so their presence here is
/// the wildcard leak itself. Mirrors the sibling adviser/institution/stock wildcard probes, but on a
/// distinct surface (the global search aggregator) and a distinct module (InsiderTrading).
/// </summary>
[Collection(WebHostCollection.Name)]
public class InsidersGlobalSearchWildcardEscapingTests
{
    private readonly WebHostFixture _fixture;

    public InsidersGlobalSearchWildcardEscapingTests(WebHostFixture fixture) => _fixture = fixture;

    private static Task Seed(EquiblesFinancialDbContext db)
    {
        db.Add(
            new InsiderOwner
            {
                OwnerCik = "0001000001",
                Name = "Warren Buffett",
                IsDirector = true,
            }
        );
        db.Add(
            new InsiderOwner
            {
                OwnerCik = "0001000002",
                Name = "Cathie Wood",
                IsTenPercentOwner = true,
            }
        );
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GlobalSearch_QueryIsBareUnderscore_DoesNotWildcardMatchInsiderNamesWithoutUnderscore()
    {
        await _fixture.ResetAndSeedAsync(Seed);

        // Contract: insider name CONTAINS the term. Neither seeded name has a literal "_", so a
        // correct search returns no insider hits; an unescaped "_" wildcard matches both non-empty
        // names. category=Insiders keeps the request on the results page (no exact-ticker redirect).
        var response = await _fixture.Client.GetAsync("/Search?q=_&category=Insiders");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should()
            .NotContain(
                "Warren Buffett",
                "an unescaped '_' must not wildcard-match an insider name with no literal underscore"
            );
        html.Should()
            .NotContain(
                "Cathie Wood",
                "an unescaped '_' must not wildcard-match an insider name with no literal underscore"
            );
    }
}
