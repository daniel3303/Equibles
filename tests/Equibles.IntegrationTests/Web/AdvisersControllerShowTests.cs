using System.Net;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

[Collection(WebHostCollection.Name)]
public class AdvisersControllerShowTests
{
    private readonly WebHostFixture _fixture;

    public AdvisersControllerShowTests(WebHostFixture fixture) => _fixture = fixture;

    // Contract: GET /advisers/{crd} returns 404 when no adviser has that CRD — the
    // Show action's GetByCrd.FirstOrDefaultAsync() == null branch. AdvisersController
    // has no test coverage; this pins the not-found path end-to-end (route → controller
    // → repository) so a regression that 500s or renders an empty view is caught.
    [Fact]
    public async Task Show_UnknownCrd_Returns404()
    {
        await _fixture.ResetAndSeedAsync();

        var response = await _fixture.Client.GetAsync("/advisers/999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
