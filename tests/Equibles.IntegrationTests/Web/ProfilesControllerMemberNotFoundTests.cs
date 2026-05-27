using System.Net;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Sibling to ProfilesControllerInsiderNotFoundTests (PR #2209). The Member
/// action's `if (member == null) return NotFound();` guard is structurally
/// distinct: a refactor that swallowed the null-check would NRE on member.Name
/// for every unknown Guid the public-facing /congress/{id} route receives
/// (search-crawler 404 sweeps, deep-linked stale URLs, manually-typed Guids).
/// Pin the explicit NotFound response.
/// </summary>
[Collection(WebHostCollection.Name)]
public class ProfilesControllerMemberNotFoundTests
{
    private readonly WebHostFixture _fixture;

    public ProfilesControllerMemberNotFoundTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetMember_UnknownId_Returns404()
    {
        await _fixture.ResetAndSeedAsync();

        var response = await _fixture.Client.GetAsync($"/congress/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
