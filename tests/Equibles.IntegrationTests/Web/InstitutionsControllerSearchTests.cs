using System.Net;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the /institutions/search typeahead endpoint (previously untested): a
/// lower-case query must match a title-cased holder via case-insensitive ILike
/// (a regression to case-sensitive Like would silently return nothing), and the
/// JSON must use the documented lower-case wire keys the JS picker reads.
/// </summary>
[Collection(WebHostCollection.Name)]
public class InstitutionsControllerSearchTests
{
    private readonly WebHostFixture _fixture;

    public InstitutionsControllerSearchTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetSearch_LowercaseQuery_MatchesTitleCasedHolderWithLowercaseWireKeys()
    {
        await _fixture.ResetAndSeedAsync(async db =>
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
            await db.SaveChangesAsync();
        });

        var response = await _fixture.Client.GetAsync("/institutions/search?q=van");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Vanguard Group Inc"); // lowercase "van" matches via ILike
        json.Should().NotContain("Berkshire"); // non-matching holder filtered out
        json.Should().Contain("\"cik\"").And.Contain("\"name\""); // documented lowercase wire keys
    }
}
