using System.Net;
using Equibles.Data;
using Equibles.Errors.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Adversarial cover for <c>ErrorRepository.Search</c>'s OR semantics. The contract is "Context
/// OR Message contains the term", so a term present only in the Message (not the Context) must
/// still return the error. The existing wildcard test seeds a term in neither field, so a
/// regression that ANDed the two ILIKE clauses instead of ORing them would survive it — this
/// pins the Message-only match. Driven end-to-end through /Status, whose view renders the Context.
/// </summary>
[Collection(WebHostCollection.Name)]
public class StatusControllerSearchMessageMatchTests
{
    private const string ProbeContext = "AlphaProbeContext";

    private readonly WebHostFixture _fixture;

    public StatusControllerSearchMessageMatchTests(WebHostFixture fixture) => _fixture = fixture;

    private static Task Seed(EquiblesFinancialDbContext db)
    {
        // The term "gamma" appears only in Message, never in Context.
        db.Add(
            new Error
            {
                Source = ErrorSource.Other,
                Context = ProbeContext,
                Message = "syncing the gamma widget failed",
                StackTrace = "at Probe.Method()",
                Seen = false,
            }
        );
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetStatus_SearchTermMatchesMessageOnly_ReturnsTheError()
    {
        await _fixture.ResetAndSeedAsync(Seed);

        // "gamma" is in Message but not Context. The OR contract must still surface the error;
        // an AND of the two ILIKE clauses would wrongly require the term in Context too.
        var response = await _fixture.Client.GetAsync("/Status?search=gamma");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should()
            .Contain(
                ProbeContext,
                "a term matching only the Message must still return the error (Context OR Message)"
            );
    }
}
