using System.Net;
using Equibles.Errors.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Sibling to <see cref="SeededViewRenderingTests"/>, which renders Status/Index
/// but never follows through to Status/Show — its compiled Razor view stays 0%.
/// Pins the Show contract for an unseen error: 200, the error Message rendered,
/// the "New" badge (because <c>!Model.Seen</c>), and an antiforgery token in the
/// MarkAsSeen/Delete POST forms — a missing token silently breaks both actions.
/// </summary>
[Collection(WebHostCollection.Name)]
public class StatusShowViewRenderingTests
{
    private readonly WebHostFixture _fixture;

    public StatusShowViewRenderingTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetStatusShow_UnseenSeededError_RendersNewBadgeAndAntiforgeryForms()
    {
        var errorId = new Guid("11111111-2222-3333-4444-555555555555");

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new Error
                {
                    Id = errorId,
                    Source = ErrorSource.HoldingsScraper,
                    Context = "Holdings.ProcessDataSet",
                    Message = "Seeded unseen error for Status/Show rendering",
                    CreationTime = DateTime.UtcNow,
                    Seen = false,
                }
            );
            await Task.CompletedTask;
        });

        // BaseController's [Route("{controller=Home}/{action=Index}")] combines with
        // Show's [HttpGet("{id:guid}")] → /Status/Show/{id} (no ~/ override).
        var response = await _fixture.Client.GetAsync($"/Status/Show/{errorId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should()
            .Contain(
                "Seeded unseen error for Status/Show rendering",
                "the error message must render in the Show view"
            );
        html.Should().Contain("New", "an unseen error must show the 'New' badge, not 'Seen'");
        html.Should()
            .Contain(
                "__RequestVerificationToken",
                "the MarkAsSeen/Delete POST forms must carry an antiforgery token"
            );
    }
}
