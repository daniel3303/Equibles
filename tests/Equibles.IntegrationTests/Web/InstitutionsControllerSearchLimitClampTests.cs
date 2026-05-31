using System.Net;
using System.Text.Json;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Adversarial: the /institutions/search typeahead clamps limit to an upper bound
/// of 50. An over-large client limit (1000) against 60 matching holders must NOT
/// over-fetch — the JSON array must contain at most the clamp (≤50) rows.
/// </summary>
[Collection(WebHostCollection.Name)]
public class InstitutionsControllerSearchLimitClampTests
{
    private readonly WebHostFixture _fixture;

    public InstitutionsControllerSearchLimitClampTests(WebHostFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task GetSearch_LimitAboveUpperClamp_ReturnsAtMostFiftyRows()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            for (var i = 0; i < 60; i++)
            {
                db.Add(
                    new InstitutionalHolder
                    {
                        Cik = (9000000000L + i).ToString(),
                        Name = $"Capital Partners {i:D2}",
                        City = "Boston",
                        StateOrCountry = "MA",
                    }
                );
            }

            await db.SaveChangesAsync();
        });

        var response = await _fixture.Client.GetAsync("/institutions/search?q=Capital&limit=1000");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetArrayLength().Should().BeLessThanOrEqualTo(50);
    }
}
