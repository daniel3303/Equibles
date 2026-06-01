using System.Net;
using Equibles.Data;
using Equibles.Errors.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Adversarial end-to-end probe of the /status error search filter. StatusController.Index
/// hands the raw query straight to ErrorRepository.Search, which splices it into an ILIKE
/// pattern (<c>%{search}%</c>) without escaping LIKE metacharacters — unlike the sibling
/// <c>InstitutionalHolderRepository.SearchNameOrCik</c>, which deliberately escapes them.
/// A bare underscore is a SQL LIKE single-character wildcard, so a search of "_" — a
/// character neither the seeded error's Context nor its Message contains — must return no
/// rows under the "Context or Message contains the term" contract. If "_" is honoured as a
/// wildcard it matches every non-empty Context, leaking errors the filter should exclude
/// (and "%" becomes a full-table dump). Driven through the real router -> controller ->
/// ParadeDB -> Razor view, which renders @error.Context verbatim.
/// </summary>
[Collection(WebHostCollection.Name)]
public class StatusControllerSearchWildcardTests
{
    private const string ProbeContext = "WildcardLeakProbeContext";

    private readonly WebHostFixture _fixture;

    public StatusControllerSearchWildcardTests(WebHostFixture fixture) => _fixture = fixture;

    private static Task Seed(EquiblesFinancialDbContext db)
    {
        db.Add(
            new Error
            {
                Source = ErrorSource.Other,
                Context = ProbeContext,
                Message = "boom while syncing",
                StackTrace = "at Probe.Method()",
                Seen = false,
            }
        );
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetStatus_SearchIsBareUnderscore_DoesNotWildcardMatchErrorsWithoutUnderscore()
    {
        await _fixture.ResetAndSeedAsync(Seed);

        // Contract: search filters errors whose Context or Message CONTAINS the term. Neither
        // seeded field has a literal "_", so a correct (escaped) filter returns nothing; an
        // unescaped "_" LIKE wildcard matches any single character and leaks the error.
        var response = await _fixture.Client.GetAsync("/Status?search=_");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should()
            .NotContain(
                ProbeContext,
                "an unescaped '_' must not wildcard-match an error Context with no literal underscore"
            );
    }
}
