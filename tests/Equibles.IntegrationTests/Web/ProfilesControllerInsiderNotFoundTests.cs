using System.Net;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Companion to ProfilesControllerViewRenderingTests (which pins the happy
/// paths of the three /insiders, /institutions, /congress profile routes).
/// The Insider action's 404 guard for an unknown ownerCik is structurally
/// distinct: a refactor that swallowed the null-check (e.g. trusting the
/// repository to return a placeholder) would NRE on owner.Name / owner.City
/// for every unknown cik typed into the address bar or harvested by a search
/// crawler. Pin the explicit NotFound response so the guard cannot regress to
/// either a crash or a silent empty render.
/// </summary>
[Collection(WebHostCollection.Name)]
public class ProfilesControllerInsiderNotFoundTests
{
    private readonly WebHostFixture _fixture;

    public ProfilesControllerInsiderNotFoundTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetInsider_UnknownOwnerCik_Returns404()
    {
        await _fixture.ResetAndSeedAsync();

        var response = await _fixture.Client.GetAsync("/insiders/9999999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
