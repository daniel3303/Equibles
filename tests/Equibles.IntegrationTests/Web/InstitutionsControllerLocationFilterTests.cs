using System.Net;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the location filters on the /institutions index: a state/country exact
/// match and a case-insensitive city substring, both narrowing the listed filer
/// set. Also pins that the state dropdown is populated from the distinct
/// state/country values present in the universe.
/// </summary>
[Collection(WebHostCollection.Name)]
public class InstitutionsControllerLocationFilterTests
{
    private readonly WebHostFixture _fixture;

    public InstitutionsControllerLocationFilterTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetIndex_FilterByState_ReturnsOnlyHoldersInThatState()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new InstitutionalHolder
                {
                    Cik = "0000001",
                    Name = "Malvern Capital",
                    City = "Malvern",
                    StateOrCountry = "PA",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Cik = "0000002",
                    Name = "Omaha Holdings",
                    City = "Omaha",
                    StateOrCountry = "NE",
                }
            );
            await db.SaveChangesAsync();
        });

        var response = await _fixture.Client.GetAsync("/institutions?state=PA");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Malvern Capital");
        html.Should().NotContain("Omaha Holdings");
    }

    [Fact]
    public async Task GetIndex_FilterByCity_MatchesCaseInsensitiveSubstring()
    {
        await _fixture.ResetAndSeedAsync(async db =>
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
            await db.SaveChangesAsync();
        });

        // Lowercase substring "new" must match "New York" via ILike.
        var response = await _fixture.Client.GetAsync("/institutions?city=new");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Gotham Asset Management");
        html.Should().NotContain("Bay Area Partners");
    }

    [Fact]
    public async Task GetIndex_RendersStateDropdown_FromDistinctUniverseValues()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new InstitutionalHolder
                {
                    Cik = "0000001",
                    Name = "Alpha Fund",
                    City = "Austin",
                    StateOrCountry = "TX",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Cik = "0000002",
                    Name = "Beta Fund",
                    City = "Boston",
                    StateOrCountry = "MA",
                }
            );
            await db.SaveChangesAsync();
        });

        var response = await _fixture.Client.GetAsync("/institutions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        // The location dropdown offers every state present in the universe.
        html.Should().Contain("<option value=\"TX\"");
        html.Should().Contain("<option value=\"MA\"");
    }

    [Fact]
    public async Task GetIndex_StateAndSearchCombined_ApplyTogether()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new InstitutionalHolder
                {
                    Cik = "0000001",
                    Name = "Vanguard Group",
                    City = "Malvern",
                    StateOrCountry = "PA",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Cik = "0000002",
                    Name = "Vanguard Texas LLC",
                    City = "Dallas",
                    StateOrCountry = "TX",
                }
            );
            await db.SaveChangesAsync();
        });

        // Search matches both "Vanguard" names; the PA state filter narrows to one.
        var response = await _fixture.Client.GetAsync("/institutions?search=vanguard&state=PA");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Vanguard Group");
        html.Should().NotContain("Vanguard Texas LLC");
    }
}
