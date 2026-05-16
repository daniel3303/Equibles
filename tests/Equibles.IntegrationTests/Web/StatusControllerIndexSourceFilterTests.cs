using System.Net;
using Equibles.Errors.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Contract: <c>/Status</c> is a public page and <c>source</c> is a user-supplied
/// query filter. A source that matches no <see cref="ErrorSource"/> must yield a
/// usable page (200) with non-matching errors excluded — never a 500. Index runs
/// <c>Where(e =&gt; e.Source == new ErrorSource(source))</c>: reference-<c>==</c>
/// on a value-converted reference type, exercised here against real ParadeDB.
/// </summary>
[Collection(WebHostCollection.Name)]
public class StatusControllerIndexSourceFilterTests
{
    private readonly WebHostFixture _fixture;

    public StatusControllerIndexSourceFilterTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Index_SourceFilterMatchesNoErrorSource_ReturnsUsablePage()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new Error
                {
                    Source = ErrorSource.McpTool,
                    Context = "ctx",
                    Message = "seeded error",
                    Seen = false,
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Status?source=NoSuchSource");

        response
            .StatusCode.Should()
            .Be(
                HttpStatusCode.OK,
                "an unmatched source filter must return a usable status page, not a "
                    + "500 from an untranslatable value-converter equality query"
            );
    }
}
